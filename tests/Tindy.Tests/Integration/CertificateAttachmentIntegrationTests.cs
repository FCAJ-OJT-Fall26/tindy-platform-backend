using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Storage;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Repositories;
using Tindy.Infrastructure.Services;
using Tindy.Infrastructure.Storage;
using Tindy.Tests.Media;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class CertificateAttachmentIntegrationTests : IAsyncLifetime
{
    private readonly TestPostgres _postgres = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tindy-attachments-" + Guid.NewGuid().ToString("N"));
    private DbContextOptions<TindyDbContext> _options = null!;
    private IFileStorageService Storage => new LocalFileStorageService(_root, Directory.GetCurrentDirectory());
    private TindyDbContext Db() => new(_options);
    private CertificateAttachmentService Service(TindyDbContext db, IFileStorageService? storage = null) =>
        new(new CertificateAttachmentRepository(db), storage ?? Storage, new CertificateFileProcessor());

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<(Guid Owner, Guid Certificate)> Seed()
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var certificate = Guid.NewGuid();
        db.Users.Add(new User
        {
            UserId = owner, Status = "active", IsOnboardingCompleted = true, CreatedAt = DateTime.UtcNow,
            Profile = new UserProfile { UserId = owner, DisplayName = "Test Owner", CreatedAt = DateTime.UtcNow },
            Certificates = [new UserCertificate { CertificateId = certificate, UserId = owner, Title = "Test", Issuer = "Issuer", CreatedAt = DateTime.UtcNow }]
        });
        await db.SaveChangesAsync();
        return (owner, certificate);
    }

    private async Task<Tindy.Application.DTOs.Profile.CertificateAttachmentResponse> Upload(Guid owner, Guid certificate, bool pdf = false, int size = 0, IFileStorageService? storage = null)
    {
        await using var db = Db();
        var bytes = pdf ? Pdf(size) : CertificateFileProcessorTests.Image();
        return await Service(db, storage).UploadAsync(owner, certificate, new MemoryStream(bytes), pdf ? "cert.pdf" : "cert.png", bytes.Length, default);
    }

    private static byte[] Pdf(int size)
    {
        var bytes = new byte[Math.Max(size, 32)];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        "\n%%EOF"u8.CopyTo(bytes.AsSpan(bytes.Length - 6));
        return bytes;
    }

    [Fact]
    public async Task ImageUpload_OwnerOnly_MembersVisibility_AndPrivateRead()
    {
        var (owner, certificate) = await Seed();
        var (member, _) = await Seed();
        var uploaded = await Upload(owner, certificate);
        Assert.Equal("active", uploaded.Status);
        Assert.Equal("owner_only", uploaded.Visibility);
        await using var db = Db();
        var service = Service(db);
        Assert.Single(await service.ListAsync(owner, owner, certificate, default));
        Assert.Empty(await service.ListAsync(member, owner, certificate, default));
        var reader = new ProfileMediaService(db, Storage, new TestHttpClientFactory());
        var denied = await Assert.ThrowsAsync<MediaRequestException>(() => reader.ReadAsync(member, uploaded.MediaAssetId, default));
        Assert.Equal(404, denied.StatusCode);
        await using (var handle = (await reader.ReadAsync(owner, uploaded.MediaAssetId, default)).Handle) Assert.True(handle.Content.CanRead);
        await service.ChangeVisibilityAsync(owner, certificate, uploaded.AttachmentId, "members", default);
        Assert.Single(await service.ListAsync(member, owner, certificate, default));
        await using (var handle = (await reader.ReadAsync(member, uploaded.MediaAssetId, default)).Handle) Assert.True(handle.Content.CanRead);
        await Assert.ThrowsAsync<MediaRequestException>(() => reader.ReadAsync(Guid.NewGuid(), uploaded.MediaAssetId, default));
    }

    [Fact]
    public async Task PdfUpload_IsReserved_AndCannotBeSharedOrReadBeforeScan()
    {
        var (owner, certificate) = await Seed();
        var uploaded = await Upload(owner, certificate, true, 1024);
        Assert.Equal("processing", uploaded.Status);
        await using var db = Db();
        var service = Service(db);
        var quota = await service.GetQuotaAsync(owner, default);
        Assert.Equal(0, quota.UsedBytes);
        Assert.Equal(1024, quota.ReservedBytes);
        Assert.Equal(200_000_000 - 1024, quota.AvailableBytes);
        var error = await Assert.ThrowsAsync<MediaRequestException>(() => service.ChangeVisibilityAsync(owner, certificate, uploaded.AttachmentId, "members", default));
        Assert.Equal(409, error.StatusCode);
        await Assert.ThrowsAsync<MediaRequestException>(() => new ProfileMediaService(db, Storage, new TestHttpClientFactory()).ReadAsync(owner, uploaded.MediaAssetId, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneAttachmentLimit_IncludesActiveAndProcessingFiles(bool pdf)
    {
        var (owner, certificate) = await Seed();
        await Upload(owner, certificate, pdf);
        var error = await Assert.ThrowsAsync<MediaRequestException>(() => Upload(owner, certificate));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("MEDIA_ATTACHMENT_LIMIT", error.ErrorCode);
    }

    [Fact]
    public async Task RejectedPdf_OccupiesSlotUntilExplicitDeletion()
    {
        var (owner, certificate) = await Seed();
        var pdf = await Upload(owner, certificate, true);
        await using var db = Db();
        await db.MediaAssets.Where(a => a.MediaAssetId == pdf.MediaAssetId).ExecuteUpdateAsync(update => update
            .SetProperty(a => a.Status, "rejected").SetProperty(a => a.ScanStatus, "threat_detected")
            .SetProperty(a => a.ReservedBytes, 0L));
        var error = await Assert.ThrowsAsync<MediaRequestException>(() => Upload(owner, certificate));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("MEDIA_ATTACHMENT_LIMIT", error.ErrorCode);
        Assert.Equal(0, (await Service(db).GetQuotaAsync(owner, default)).ReservedBytes);
        await Service(db).DeleteAsync(owner, certificate, pdf.AttachmentId, default);
        Assert.Equal("active", (await Upload(owner, certificate)).Status);
    }

    [Fact]
    public async Task CrossOwnerMutations_AreNotFound()
    {
        var (owner, certificate) = await Seed();
        var (stranger, _) = await Seed();
        var attachment = await Upload(owner, certificate);
        await using var db = Db();
        var service = Service(db);
        await Assert.ThrowsAsync<MediaRequestException>(() => Upload(stranger, certificate));
        await Assert.ThrowsAsync<MediaRequestException>(() => service.ChangeVisibilityAsync(stranger, certificate, attachment.AttachmentId, "members", default));
        await Assert.ThrowsAsync<MediaRequestException>(() => service.DeleteAsync(stranger, certificate, attachment.AttachmentId, default));
        Assert.Single(await service.ListAsync(owner, owner, certificate, default));
    }

    [Fact]
    public async Task Delete_ReleasesQuota_PreservesMetadata_AndOtherAttachments()
    {
        var (owner, certificate) = await Seed();
        var first = await Upload(owner, certificate, true, 1000);
        Guid otherCertificate;
        await using (var seed = Db())
        {
            otherCertificate = Guid.NewGuid();
            seed.UserCertificates.Add(new UserCertificate { CertificateId = otherCertificate, UserId = owner,
                Title = "Other", Issuer = "Test", CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        var second = await Upload(owner, otherCertificate);
        await using var db = Db();
        var service = Service(db);
        await service.DeleteAsync(owner, certificate, first.AttachmentId, default);
        Assert.Empty(await service.ListAsync(owner, owner, certificate, default));
        Assert.Equal(second.AttachmentId, Assert.Single(await service.ListAsync(owner, owner, otherCertificate, default)).AttachmentId);
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == first.MediaAssetId);
        Assert.Equal("pending_delete", asset.Status);
        Assert.NotNull(asset.NextCleanupAt);
        Assert.Equal(0, (await service.GetQuotaAsync(owner, default)).ReservedBytes);
        var repeated = await Assert.ThrowsAsync<MediaRequestException>(() => service.DeleteAsync(owner, certificate, first.AttachmentId, default));
        Assert.Equal(404, repeated.StatusCode); // Same convention as Certificate DELETE.
        var replacement = await Upload(owner, certificate);
        Assert.Equal("active", replacement.Status);
        Assert.NotEqual(first.AttachmentId, replacement.AttachmentId);
    }

    [Fact]
    public async Task DeleteCertificate_DetachesAllMediaInSameTransaction()
    {
        var (owner, certificate) = await Seed();
        var attachment = await Upload(owner, certificate, true);
        await using var db = Db();
        await new CertificateService(new CertificateRepository(db)).DeleteAsync(owner, certificate);
        Assert.False(await db.UserCertificateAttachments.AnyAsync(a => a.CertificateId == certificate));
        Assert.Equal("pending_delete", (await db.MediaAssets.SingleAsync(a => a.MediaAssetId == attachment.MediaAssetId)).Status);
        Assert.Equal(0, (await Service(db).GetQuotaAsync(owner, default)).ReservedBytes);
    }

    [Fact]
    public async Task ConcurrentUploads_CannotExceedQuotaAcrossCertificates()
    {
        var (owner, certificate) = await Seed();
        await using (var db = Db())
        {
            db.UserCertificates.Add(new UserCertificate { CertificateId = Guid.NewGuid(), UserId = owner, Title = "Second", Issuer = "Test", CreatedAt = DateTime.UtcNow });
            db.MediaAssets.Add(new MediaAsset
            {
                MediaAssetId = Guid.NewGuid(), OwnerUserId = owner, Purpose = "certificate", StorageProvider = "local", StorageContainer = "local",
                StorageKey = Storage.CreateWriteTarget(owner, MediaStorageKind.CertificateImage).StorageKey, Status = "active", ScanStatus = "not_required",
                ContentType = "image/webp", FileSizeBytes = 195_000_000, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        Guid second;
        await using (var db = Db()) second = await db.UserCertificates.Where(c => c.UserId == owner && c.CertificateId != certificate).Select(c => c.CertificateId).SingleAsync();
        async Task<bool> TryUpload(Guid cert)
        {
            try { await Upload(owner, cert, true, 3_000_000); return true; }
            catch (MediaRequestException e) when (e.ErrorCode == "MEDIA_QUOTA_EXCEEDED") { return false; }
        }
        var results = await Task.WhenAll(TryUpload(certificate), TryUpload(second));
        Assert.Single(results, x => x);
        await using var check = Db();
        var quota = await Service(check).GetQuotaAsync(owner, default);
        Assert.Equal(198_000_000, quota.UsedBytes + quota.ReservedBytes);
    }

    [Fact]
    public async Task ConcurrentUploads_CannotCreateTwoAttachments()
    {
        var (owner, certificate) = await Seed();
        async Task<bool> TryUpload()
        {
            try { await Upload(owner, certificate, true); return true; }
            catch (MediaRequestException e) when (e.ErrorCode == "MEDIA_ATTACHMENT_LIMIT") { return false; }
        }
        Assert.Single(await Task.WhenAll(TryUpload(), TryUpload()), x => x);
        await using var db = Db();
        Assert.Single(await db.UserCertificateAttachments.Where(a => a.CertificateId == certificate).ToListAsync());
        Assert.Single(await db.MediaAssets.Where(a => a.OwnerUserId == owner).ToListAsync());
    }

    [Fact]
    public async Task DatabaseUniqueIndex_BlocksBypassingService_AndRepositoryReturnsConflict()
    {
        var (owner, certificate) = await Seed();
        await Upload(owner, certificate);
        UserCertificateAttachment Duplicate() => new()
        {
            AttachmentId = Guid.NewGuid(), CertificateId = certificate, OwnerUserId = owner, CreatedAt = DateTime.UtcNow,
            MediaAsset = new MediaAsset { MediaAssetId = Guid.NewGuid(), OwnerUserId = owner, Purpose = "certificate",
                StorageProvider = "local", StorageContainer = "local", StorageKey = "duplicate/" + Guid.NewGuid(),
                Status = "pending_upload", ScanStatus = "not_required", CreatedAt = DateTime.UtcNow }
        };
        await using (var db = Db())
        {
            db.UserCertificateAttachments.Add(Duplicate());
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("ux_user_certificate_attachments_certificate_id", postgres.ConstraintName);
        }
        await using (var db = Db())
        {
            var repository = new CertificateAttachmentRepository(db);
            var error = await Assert.ThrowsAsync<MediaRequestException>(() => repository.ExecuteLockedAsync(owner, async ct =>
            {
                repository.Add(Duplicate());
                await repository.SaveAsync(ct);
                return true;
            }, default));
            Assert.Equal(409, error.StatusCode);
            Assert.Equal("MEDIA_ATTACHMENT_LIMIT", error.ErrorCode);
        }
        await using var check = Db();
        Assert.Single(await check.MediaAssets.Where(a => a.OwnerUserId == owner).ToListAsync());
    }

    [Fact]
    public async Task PendingUpload_OccupiesSlotBeforeStorageWriteCompletes()
    {
        var (owner, certificate) = await Seed();
        var gate = new GatedStorage(Storage, 1);
        var first = Upload(owner, certificate, storage: gate);
        try
        {
            await gate.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var error = await Assert.ThrowsAsync<MediaRequestException>(() => Upload(owner, certificate));
            Assert.Equal(409, error.StatusCode);
            Assert.Equal("MEDIA_ATTACHMENT_LIMIT", error.ErrorCode);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.Equal("active", (await first).Status);
    }

    [Fact]
    public async Task FailedUpload_ReleasesReservation_AndKeepsCleanupTarget()
    {
        var (owner, certificate) = await Seed();
        await using var db = Db();
        var service = Service(db);
        await Assert.ThrowsAsync<MediaRequestException>(() => service.UploadAsync(owner, certificate, new MemoryStream("garbage"u8.ToArray()), "cert.png", 7, default));
        var quota = await service.GetQuotaAsync(owner, default);
        Assert.Equal(0, quota.ReservedBytes);
        Assert.Equal(200_000_000, quota.AvailableBytes);
        Assert.Empty(await service.ListAsync(owner, owner, certificate, default));
        Assert.Equal("pending_delete", (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.OwnerUserId == owner)).Status);
    }

    [Theory]
    [InlineData(10_000_001, "file.pdf", 413)]
    [InlineData(1, "file.exe", 415)]
    public async Task SizeAndType_AreRejected(long length, string name, int status)
    {
        var (owner, certificate) = await Seed();
        await using var db = Db();
        var error = await Assert.ThrowsAsync<MediaRequestException>(() => Service(db).UploadAsync(owner, certificate, Stream.Null, name, length, default));
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task ThirdInFlightUpload_IsRejected_WhileTwoWritesAreReserved()
    {
        var (owner, certificate) = await Seed();
        var certificates = new[] { certificate, Guid.NewGuid(), Guid.NewGuid() };
        await using (var db = Db())
        {
            foreach (var id in certificates.Skip(1)) db.UserCertificates.Add(new UserCertificate
                { CertificateId = id, UserId = owner, Title = "Other", Issuer = "Test", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var gate = new GatedStorage(Storage);
        var first = Upload(owner, certificate, storage: gate);
        var second = Upload(owner, certificates[1], storage: gate);
        try
        {
            await gate.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var error = await Assert.ThrowsAsync<MediaRequestException>(() => Upload(owner, certificates[2]));
            Assert.Equal(429, error.StatusCode);
            Assert.Equal("MEDIA_CONCURRENT_LIMIT", error.ErrorCode);
        }
        finally { gate.Release.TrySetResult(); }
        await Task.WhenAll(first, second);
    }

    private sealed class GatedStorage(IFileStorageService inner, int expectedUploads = 2) : IFileStorageService
    {
        private int _count;
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StorageWriteTarget CreateWriteTarget(Guid ownerUserId, MediaStorageKind kind) => inner.CreateWriteTarget(ownerUserId, kind);
        public bool CanAccess(string provider, string container) => inner.CanAccess(provider, container);
        public async Task<StoredFileResult> SaveAsync(StorageSaveRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == expectedUploads) BothEntered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await inner.SaveAsync(request, cancellationToken);
        }
        public Task<StorageReadHandle> OpenReadAsync(string key, string? version = null, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, version, cancellationToken);
        public Task DeleteAsync(string key, string? version = null, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, version, cancellationToken);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
