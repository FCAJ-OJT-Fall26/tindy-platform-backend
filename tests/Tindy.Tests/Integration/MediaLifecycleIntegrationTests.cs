using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Storage;
using Tindy.Application.Services;
using Tindy.Infrastructure.Media;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Repositories;
using Tindy.Infrastructure.Services;
using Tindy.Infrastructure.Storage;
using Tindy.Tests.Media;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class MediaLifecycleIntegrationTests : IAsyncLifetime
{
    private readonly TestPostgres _postgres = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tindy-lifecycle-" + Guid.NewGuid().ToString("N"));
    private readonly ScannerDouble _scanner = new();
    private readonly IOptions<MediaLifecycleOptions> _options = Options.Create(new MediaLifecycleOptions());
    private DbContextOptions<TindyDbContext> _dbOptions = null!;
    private LocalFileStorageService Storage => new(_root, Directory.GetCurrentDirectory());
    private TindyDbContext Db() => new(_dbOptions);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dbOptions = new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed record Seeded(Guid Owner, Guid Media, Guid Attachment, Guid Certificate, string Key);

    private async Task<Seeded> Seed(string provider = "local", string status = "processing")
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var media = Guid.NewGuid();
        var attachment = Guid.NewGuid();
        var certificate = Guid.NewGuid();
        var target = Storage.CreateWriteTarget(owner, MediaStorageKind.CertificatePdf);
        var bytes = "%PDF-1.7\nminimal test document\n%%EOF"u8.ToArray();
        if (provider == "local") await Storage.SaveAsync(new StorageSaveRequest(owner, MediaStorageKind.CertificatePdf, new MemoryStream(bytes), target));
        var now = DateTime.UtcNow;
        db.Users.Add(new User
        {
            UserId = owner, Status = "active", IsOnboardingCompleted = true, CreatedAt = now,
            Profile = new UserProfile { UserId = owner, DisplayName = "Media Test", CreatedAt = now },
            Certificates = [new UserCertificate { CertificateId = certificate, UserId = owner, Title = "Test", Issuer = "Test", CreatedAt = now }]
        });
        db.UserCertificateAttachments.Add(new UserCertificateAttachment
        {
            AttachmentId = attachment, CertificateId = certificate, OwnerUserId = owner, CreatedAt = now,
            MediaAsset = new MediaAsset
            {
                MediaAssetId = media, OwnerUserId = owner, Purpose = "certificate", StorageProvider = provider,
                StorageContainer = provider == "s3" ? GuardDutyEventTests.Configuration().BucketName : "local", StorageKey = target.StorageKey,
                ObjectEtag = provider == "s3" ? "\"test-etag\"" : null, ObjectVersionId = provider == "s3" ? "version-1" : null,
                ContentType = "application/pdf", FileSizeBytes = bytes.Length, ReservedBytes = status == "active" ? 0 : bytes.Length,
                Status = status, ScanStatus = status == "active" ? "clean" : "pending", ScanProvider = provider == "s3" ? "guardduty" : "clamav",
                CreatedAt = now, UploadExpiresAt = status == "pending_upload" ? now.AddMinutes(-1) : null
            }
        });
        await db.SaveChangesAsync();
        return new(owner, media, attachment, certificate, target.StorageKey);
    }

    private MediaLifecycleService Lifecycle(TindyDbContext db, IFileStorageService? storage = null, ILogger<MediaLifecycleService>? logger = null) =>
        new(db, storage ?? Storage, _scanner, _options, logger ?? NullLogger<MediaLifecycleService>.Instance);

    [Theory]
    [InlineData(MalwareScanResult.Clean, "active", "clean")]
    [InlineData(MalwareScanResult.ThreatDetected, "rejected", "threat_detected")]
    [InlineData(MalwareScanResult.Unsupported, "rejected", "unsupported")]
    [InlineData(MalwareScanResult.Timeout, "processing", "failed")]
    [InlineData(MalwareScanResult.Unavailable, "processing", "failed")]
    [InlineData(MalwareScanResult.Failed, "processing", "failed")]
    public async Task LocalPdfScan_TransitionsAndPrivateReadAreFailClosed(MalwareScanResult verdict, string status, string scanStatus)
    {
        var item = await Seed();
        _scanner.Result = verdict;
        await using var db = Db();
        await Lifecycle(db).ScanLocalAsync(item.Media, default);
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media);
        Assert.Equal(status, asset.Status);
        Assert.Equal(scanStatus, asset.ScanStatus);
        var quota = await new CertificateAttachmentService(new CertificateAttachmentRepository(db), Storage, new CertificateFileProcessor()).GetQuotaAsync(item.Owner, default);
        var reader = new ProfileMediaService(db, Storage, new HttpClientFactoryDouble());
        if (verdict == MalwareScanResult.Clean)
        {
            Assert.Equal(asset.FileSizeBytes, quota.UsedBytes);
            Assert.Equal(0, quota.ReservedBytes);
            await using var handle = (await reader.ReadAsync(item.Owner, item.Media, default)).Handle;
            Assert.True(handle.Content.Length > 0);
        }
        else
        {
            var denied = await Assert.ThrowsAsync<MediaRequestException>(() => reader.ReadAsync(item.Owner, item.Media, default));
            Assert.Equal(404, denied.StatusCode);
            Assert.Equal(status == "processing" ? asset.FileSizeBytes : 0, quota.ReservedBytes);
            Assert.Equal(0, quota.UsedBytes);
            Assert.Single(await db.UserCertificateAttachments.Where(a => a.AttachmentId == item.Attachment).ToListAsync());
        }
    }

    [Fact]
    public async Task DuplicateScan_DoesNotReactivateDeletedFile()
    {
        var item = await Seed();
        await using var db = Db();
        await Lifecycle(db).ScanLocalAsync(item.Media, default);
        var service = new CertificateAttachmentService(new CertificateAttachmentRepository(db), Storage, new CertificateFileProcessor());
        await service.DeleteAsync(item.Owner, item.Certificate, item.Attachment, default);
        await Lifecycle(db).ScanLocalAsync(item.Media, default);
        Assert.Equal("pending_delete", (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media)).Status);
        Assert.Equal(1, _scanner.Calls);
    }

    [Fact]
    public async Task CleanupFailure_PersistsRetry_ThenRemovesPhysicalFileBeforeMetadata()
    {
        var item = await Seed();
        await using var db = Db();
        var asset = await db.MediaAssets.SingleAsync(a => a.MediaAssetId == item.Media);
        CertificateAttachmentService.MarkForDeletion(asset);
        db.UserCertificateAttachments.Remove(await db.UserCertificateAttachments.SingleAsync(a => a.AttachmentId == item.Attachment));
        await db.SaveChangesAsync();
        var storage = new FailingDeleteStorage(Storage);
        await Lifecycle(db, storage).CleanupAsync(item.Media, default);
        asset = await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media);
        Assert.Equal(1, asset.CleanupAttempts);
        Assert.Equal("IOException", asset.LastCleanupError);
        Assert.True(asset.NextCleanupAt > DateTime.UtcNow);
        await using (var handle = await Storage.OpenReadAsync(item.Key)) Assert.True(handle.Content.CanRead);
        await db.MediaAssets.Where(a => a.MediaAssetId == item.Media).ExecuteUpdateAsync(u => u.SetProperty(a => a.NextCleanupAt, DateTime.UtcNow.AddSeconds(-1)));
        await Lifecycle(db, storage).CleanupAsync(item.Media, default);
        Assert.False(await db.MediaAssets.AnyAsync(a => a.MediaAssetId == item.Media));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Storage.OpenReadAsync(item.Key));
    }

    [Fact]
    public async Task ExpiredUpload_CleansRelationshipAndObject()
    {
        var item = await Seed(status: "pending_upload");
        await using var db = Db();
        await Lifecycle(db).CleanupAsync(item.Media, default);
        Assert.False(await db.MediaAssets.AnyAsync(a => a.MediaAssetId == item.Media));
        Assert.False(await db.UserCertificateAttachments.AnyAsync(a => a.AttachmentId == item.Attachment));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Storage.OpenReadAsync(item.Key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedMediaCleanup_PreservesSlotAndMetadata_UntilOwnerDeletes(bool failFirstDelete)
    {
        var item = await Seed();
        _scanner.Result = MalwareScanResult.ThreatDetected;
        await using var db = Db();
        await Lifecycle(db).ScanLocalAsync(item.Media, default);
        await db.MediaAssets.Where(a => a.MediaAssetId == item.Media).ExecuteUpdateAsync(u => u.SetProperty(a => a.NextCleanupAt, DateTime.UtcNow.AddSeconds(-1)));
        IFileStorageService storage = failFirstDelete ? new FailingDeleteStorage(Storage) : Storage;
        await Lifecycle(db, storage).RunCleanupCycleAsync(default);
        if (failFirstDelete)
        {
            var failed = await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media);
            Assert.Equal("rejected", failed.Status);
            Assert.Equal("IOException", failed.LastCleanupError);
            Assert.NotNull(failed.NextCleanupAt);
            await db.MediaAssets.Where(a => a.MediaAssetId == item.Media).ExecuteUpdateAsync(u => u.SetProperty(a => a.NextCleanupAt, DateTime.UtcNow.AddSeconds(-1)));
            await Lifecycle(db, storage).RunCleanupCycleAsync(default);
        }
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media);
        Assert.Equal("rejected", asset.Status);
        Assert.Null(asset.NextCleanupAt);
        Assert.Null(asset.LastCleanupError);
        Assert.Single(await db.UserCertificateAttachments.Where(a => a.AttachmentId == item.Attachment).ToListAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => Storage.OpenReadAsync(item.Key));
        await Lifecycle(db, storage).RunCleanupCycleAsync(default);
        Assert.Equal(asset.CleanupAttempts, (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media)).CleanupAttempts);
        var service = new CertificateAttachmentService(new CertificateAttachmentRepository(db), Storage, new CertificateFileProcessor());
        var bytes = CertificateFileProcessorTests.Image();
        var conflict = await Assert.ThrowsAsync<MediaRequestException>(() => service.UploadAsync(item.Owner, item.Certificate,
            new MemoryStream(bytes), "replacement.png", bytes.Length, default));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("MEDIA_ATTACHMENT_LIMIT", conflict.ErrorCode);
        Assert.Equal(0, (await service.GetQuotaAsync(item.Owner, default)).ReservedBytes);
        await service.DeleteAsync(item.Owner, item.Certificate, item.Attachment, default);
        await Lifecycle(db).CleanupAsync(item.Media, default);
        Assert.False(await db.MediaAssets.AnyAsync(a => a.MediaAssetId == item.Media));
        Assert.Equal("active", (await service.UploadAsync(item.Owner, item.Certificate,
            new MemoryStream(bytes), "replacement.png", bytes.Length, default)).Status);
    }

    [Fact]
    public async Task Cleanup_PreservesActiveMediaAndLiveReferences()
    {
        var active = await Seed(status: "active");
        var referenced = await Seed(status: "pending_delete");
        await using var db = Db();
        await Lifecycle(db).CleanupAsync(active.Media, default);
        await Lifecycle(db).CleanupAsync(referenced.Media, default);
        Assert.True(await db.MediaAssets.AnyAsync(a => a.MediaAssetId == active.Media));
        Assert.True(await db.MediaAssets.AnyAsync(a => a.MediaAssetId == referenced.Media));
        await using var handle = await Storage.OpenReadAsync(referenced.Key);
        Assert.True(handle.Content.CanRead);
    }

    [Fact]
    public async Task ProcessingOverTwoHours_LogsWarningAndNeverMarksClean()
    {
        var item = await Seed();
        _scanner.Result = MalwareScanResult.Timeout;
        await using var db = Db();
        await db.MediaAssets.Where(a => a.MediaAssetId == item.Media).ExecuteUpdateAsync(u => u.SetProperty(a => a.CreatedAt, DateTime.UtcNow.AddHours(-3)));
        var logger = new CaptureLogger<MediaLifecycleService>();
        await Lifecycle(db, logger: logger).RunOnceAsync(default);
        Assert.Contains(logger.Messages, message => message.Contains("two hours", StringComparison.Ordinal));
        Assert.Equal("processing", (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media)).Status);
    }

    [Theory]
    [InlineData("NO_THREATS_FOUND", "active", "clean")]
    [InlineData("THREATS_FOUND", "rejected", "threat_detected")]
    [InlineData("UNSUPPORTED", "rejected", "unsupported")]
    [InlineData("ACCESS_DENIED", "rejected", "access_denied")]
    [InlineData("FAILED", "rejected", "failed")]
    public async Task GuardDuty_ValidResultIsIdempotent_AndBoundToStoredObject(string verdict, string status, string scanStatus)
    {
        var item = await Seed("s3");
        var body = GuardDutyEventTests.Event(item.Key, verdict);
        await using var db = Db();
        var handler = new GuardDutyEventHandler(db, Options.Create(GuardDutyEventTests.Configuration()), _options);
        Assert.Equal(ScanEventDisposition.Processed, await handler.ProcessAsync(body, default));
        Assert.Equal(ScanEventDisposition.Ignored, await handler.ProcessAsync(body, default));
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media);
        Assert.Equal(status, asset.Status);
        Assert.Equal(scanStatus, asset.ScanStatus);
        Assert.Equal(0, asset.ReservedBytes);
    }

    [Fact]
    public async Task GuardDuty_RejectsWrongETagVersion_AndLateEventAfterDelete()
    {
        var item = await Seed("s3");
        await using var db = Db();
        var handler = new GuardDutyEventHandler(db, Options.Create(GuardDutyEventTests.Configuration()), _options);
        Assert.Equal(ScanEventDisposition.Ignored, await handler.ProcessAsync(GuardDutyEventTests.Event(item.Key, version: "old-version"), default));
        Assert.Equal(ScanEventDisposition.Ignored, await handler.ProcessAsync(GuardDutyEventTests.Event(item.Key, etag: "wrong-etag"), default));
        Assert.Equal("processing", (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media)).Status);
        await new CertificateAttachmentService(new CertificateAttachmentRepository(db), Storage, new CertificateFileProcessor()).DeleteAsync(item.Owner, item.Certificate, item.Attachment, default);
        Assert.Equal(ScanEventDisposition.Ignored, await handler.ProcessAsync(GuardDutyEventTests.Event(item.Key), default));
        Assert.Equal("pending_delete", (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media)).Status);
    }

    [Fact]
    public async Task GuardDuty_EventBeforeUploadFinalizeIsRetried_AndUnknownObjectIsIgnored()
    {
        var item = await Seed("s3", "pending_upload");
        await using var db = Db();
        var handler = new GuardDutyEventHandler(db, Options.Create(GuardDutyEventTests.Configuration()), _options);
        Assert.Equal(ScanEventDisposition.Retry, await handler.ProcessAsync(GuardDutyEventTests.Event(item.Key), default));
        Assert.Equal(ScanEventDisposition.Ignored, await handler.ProcessAsync(GuardDutyEventTests.Event(Storage.CreateWriteTarget(item.Owner, MediaStorageKind.CertificatePdf).StorageKey), default));
        Assert.Equal(ScanEventDisposition.Invalid, await handler.ProcessAsync("invalid", default));
    }

    [Fact]
    public async Task OrphanReconciliation_PreservesReferencedAndNewFiles_AndRunsDaily()
    {
        var item = await Seed(status: "active");
        var orphan = await Storage.SaveAsync(new StorageSaveRequest(item.Owner, MediaStorageKind.CertificatePdf, new MemoryStream([1, 2])));
        var fresh = await Storage.SaveAsync(new StorageSaveRequest(item.Owner, MediaStorageKind.CertificatePdf, new MemoryStream([3, 4])));
        File.SetLastWriteTimeUtc(Path.Combine(_root, orphan.StorageKey.Replace('/', Path.DirectorySeparatorChar)), DateTime.UtcNow.AddHours(-25));
        File.SetLastWriteTimeUtc(Path.Combine(_root, item.Key.Replace('/', Path.DirectorySeparatorChar)), DateTime.UtcNow.AddHours(-25));
        await File.WriteAllTextAsync(Path.Combine(_root, "operator-file.txt"), "keep");
        await using var db = Db();
        var schedule = new MediaOrphanSchedule();
        var reconciler = new MediaOrphanReconciler(db, Storage, schedule, Lifecycle(db), NullLogger<MediaOrphanReconciler>.Instance);
        await reconciler.RunIfDueAsync(default);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Storage.OpenReadAsync(orphan.StorageKey));
        await using (var handle = await Storage.OpenReadAsync(item.Key)) Assert.True(handle.Content.CanRead);
        await using (var handle = await Storage.OpenReadAsync(fresh.StorageKey)) Assert.True(handle.Content.CanRead);
        Assert.True(File.Exists(Path.Combine(_root, "operator-file.txt")));
        Assert.True(schedule.NextRunUtc > DateTime.UtcNow.AddHours(23));
        File.SetLastWriteTimeUtc(Path.Combine(_root, fresh.StorageKey.Replace('/', Path.DirectorySeparatorChar)), DateTime.UtcNow.AddHours(-25));
        await reconciler.RunIfDueAsync(default);
        await using var preserved = await Storage.OpenReadAsync(fresh.StorageKey);
        Assert.True(preserved.Content.CanRead);
    }

    [Fact]
    public async Task WorkerCancellation_RollsBackInterruptedScan_AndStopsPromptly()
    {
        var item = await Seed();
        _scanner.Block = true;
        using var services = new ServiceCollection()
            .AddScoped(_ => Db())
            .AddSingleton<IFileStorageService>(Storage)
            .AddSingleton<IFileMalwareScanner>(_scanner)
            .AddSingleton(_options)
            .AddLogging()
            .AddScoped<MediaLifecycleService>()
            .BuildServiceProvider();
        using var worker = new MediaLifecycleWorker(services.GetRequiredService<IServiceScopeFactory>(), _options, NullLogger<MediaLifecycleWorker>.Instance);
        await worker.StartAsync(default);
        await _scanner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
        Assert.True(_scanner.Cancelled);
        await using var check = Db();
        var asset = await check.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media);
        Assert.Equal("processing", asset.Status);
        Assert.Equal("pending", asset.ScanStatus);
    }

    [Fact]
    public async Task Reconciliation_RetiresOldUnreferencedMetadata_AndCleanupRemovesPartialWrite()
    {
        var item = await Seed(status: "active");
        await using var db = Db();
        db.UserCertificateAttachments.Remove(await db.UserCertificateAttachments.SingleAsync(a => a.AttachmentId == item.Attachment));
        var asset = await db.MediaAssets.SingleAsync(a => a.MediaAssetId == item.Media);
        asset.CreatedAt = DateTime.UtcNow.AddHours(-25);
        await db.SaveChangesAsync();
        var partial = Path.Combine(_root, item.Key.Replace('/', Path.DirectorySeparatorChar)) + ".partial";
        await File.WriteAllTextAsync(partial, "interrupted");
        await Lifecycle(db).ReconcileMetadataAsync(default);
        Assert.Equal("pending_delete", (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == item.Media)).Status);
        await Lifecycle(db).CleanupAsync(item.Media, default);
        Assert.False(File.Exists(partial));
        Assert.False(await db.MediaAssets.AnyAsync(a => a.MediaAssetId == item.Media));
    }

    [Theory]
    [InlineData("test-etag", true)]
    [InlineData("replaced-object-etag", false)]
    public async Task S3PrivateRead_RequiresTheScannedObjectETag(string etag, bool readable)
    {
        var item = await Seed("s3", "active");
        await using var db = Db();
        var storage = new TagReadStorage(etag);
        var reader = new ProfileMediaService(db, storage, new HttpClientFactoryDouble());
        if (readable)
        {
            await using var handle = (await reader.ReadAsync(item.Owner, item.Media, default)).Handle;
            Assert.True(handle.Content.CanRead);
        }
        else
        {
            var error = await Assert.ThrowsAsync<MediaRequestException>(() => reader.ReadAsync(item.Owner, item.Media, default));
            Assert.Equal(404, error.StatusCode);
            Assert.False(storage.Stream.CanRead);
        }
    }

    private sealed class TagReadStorage(string etag) : IFileStorageService
    {
        public MemoryStream Stream { get; } = new([1, 2]);
        public bool CanAccess(string provider, string container) => provider == "s3" && container == GuardDutyEventTests.Configuration().BucketName;
        public StorageWriteTarget CreateWriteTarget(Guid ownerUserId, MediaStorageKind kind) => throw new NotSupportedException();
        public Task<StoredFileResult> SaveAsync(StorageSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageReadHandle> OpenReadAsync(string key, string? version = null, CancellationToken cancellationToken = default) => Task.FromResult(new StorageReadHandle(Stream, objectETag: etag));
        public Task DeleteAsync(string key, string? version = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ScannerDouble : IFileMalwareScanner
    {
        public MalwareScanResult Result { get; set; } = MalwareScanResult.Clean;
        public int Calls { get; private set; }
        public bool Block { get; set; }
        public bool Cancelled { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken ct)
        {
            Calls++;
            Entered.TrySetResult();
            if (Block)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            return Result;
        }
    }

    private sealed class FailingDeleteStorage(IFileStorageService inner) : IFileStorageService
    {
        private bool _failed;
        public StorageWriteTarget CreateWriteTarget(Guid ownerUserId, MediaStorageKind kind) => inner.CreateWriteTarget(ownerUserId, kind);
        public bool CanAccess(string provider, string container) => inner.CanAccess(provider, container);
        public Task<StoredFileResult> SaveAsync(StorageSaveRequest request, CancellationToken ct = default) => inner.SaveAsync(request, ct);
        public Task<StorageReadHandle> OpenReadAsync(string key, string? version = null, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, version, cancellationToken);
        public Task DeleteAsync(string key, string? version = null, CancellationToken cancellationToken = default)
        {
            if (!_failed) { _failed = true; throw new IOException("Transient delete failure."); }
            return inner.DeleteAsync(key, version, cancellationToken);
        }
    }

    private sealed class HttpClientFactoryDouble : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
