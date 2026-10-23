using System.Text;
using Tindy.Application.Models.Storage;
using Tindy.Infrastructure.Storage;
using Xunit;

namespace Tindy.Tests.Media;

public sealed class LocalFileStorageTests
{
    [Theory]
    [InlineData(MediaStorageKind.Avatar, "profiles/avatars/", "image/webp")]
    [InlineData(MediaStorageKind.Cover, "profiles/covers/", "image/webp")]
    [InlineData(MediaStorageKind.CertificateImage, "certificates/images/", "image/webp")]
    [InlineData(MediaStorageKind.CertificatePdf, "certificates/pdf/", "application/pdf")]
    public async Task Save_Read_Delete_RoundTrip(MediaStorageKind kind, string expectedPrefix, string expectedContentType)
    {
        using var folder = new TemporaryMediaDirectory();
        var service = folder.CreateProvider();
        var ownerId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("storage round trip for local provider");
        using var source = new MemoryStream(bytes);

        var saved = await service.SaveAsync(new StorageSaveRequest(ownerId, kind, source));
        Assert.Equal("local", saved.StorageProvider);
        Assert.Equal("local", saved.StorageContainer);
        Assert.StartsWith(expectedPrefix + ownerId.ToString("N") + "/", saved.StorageKey);
        Assert.Equal(expectedContentType, saved.ContentType);
        Assert.Equal((long)bytes.Length, saved.FileSizeBytes);
        Assert.Null(saved.ObjectVersionId);
        Assert.Null(saved.ObjectETag);
        Assert.True(source.CanRead); // The service does not take ownership of the upload stream.

        await using (var handle = await service.OpenReadAsync(saved.StorageKey))
        {
            using var copy = new MemoryStream();
            await handle.Content.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        await service.DeleteAsync(saved.StorageKey);
        await service.DeleteAsync(saved.StorageKey); // Delete is idempotent.
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.OpenReadAsync(saved.StorageKey));
    }

    [Fact]
    public async Task Save_Rejects_Empty_And_TooLarge_Streams_Without_Orphans()
    {
        using var folder = new TemporaryMediaDirectory();
        var service = folder.CreateProvider();
        var user = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SaveAsync(new StorageSaveRequest(user, MediaStorageKind.Avatar, new MemoryStream())));

        using var oversized = new MemoryStream(new byte[5_000_001]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SaveAsync(new StorageSaveRequest(user, MediaStorageKind.Avatar, oversized)));

        Assert.Empty(Directory.EnumerateFiles(folder.StorageRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Read_Delete_Reject_Traversal_And_Forged_Keys()
    {
        using var folder = new TemporaryMediaDirectory();
        var service = folder.CreateProvider();
        foreach (var badKey in new[] { "../appsettings.json", "profiles/avatars/../../secret", "C:\\secret", "/etc/passwd", "certificates/pdf/" + Guid.NewGuid().ToString("N") + "/" + Guid.NewGuid().ToString("N") + ".webp" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.OpenReadAsync(badKey));
            await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(badKey));
        }
    }

    [Fact]
    public async Task Save_Creates_Unique_Keys_For_Concurrent_Writes()
    {
        using var folder = new TemporaryMediaDirectory();
        var service = folder.CreateProvider();
        var user = Guid.NewGuid();
        var saved = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            using var source = new MemoryStream(new byte[] { 1, 2, 3 });
            return await service.SaveAsync(new StorageSaveRequest(user, MediaStorageKind.Cover, source));
        }));

        Assert.Equal(12, saved.Select(x => x.StorageKey).Distinct(StringComparer.Ordinal).Count());
        foreach (var result in saved) await service.DeleteAsync(result.StorageKey);
    }

    [Fact]
    public void LocalRoot_Rejects_ApplicationDirectory()
    {
        using var folder = new TemporaryMediaDirectory();
        Assert.Throws<InvalidOperationException>(() => new LocalFileStorageService(folder.ContentRoot, folder.ContentRoot));
        Assert.Throws<InvalidOperationException>(() => new LocalFileStorageService(Path.Combine(folder.ContentRoot, "wwwroot", "media"), folder.ContentRoot));
        Assert.Throws<InvalidOperationException>(() => new LocalFileStorageService(folder.TemporaryRoot, folder.ContentRoot));
    }

    [Fact]
    public async Task Save_Respects_Cancellation()
    {
        using var folder = new TemporaryMediaDirectory();
        var service = folder.CreateProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var source = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveAsync(new StorageSaveRequest(Guid.NewGuid(), MediaStorageKind.Avatar, source), cts.Token));
        Assert.Empty(Directory.EnumerateFiles(folder.StorageRoot, "*", SearchOption.AllDirectories));
    }

    private sealed class TemporaryMediaDirectory : IDisposable
    {
        public string TemporaryRoot { get; } = Path.Combine(Path.GetTempPath(), "tindy-media-tests-" + Guid.NewGuid().ToString("N"));
        public string ContentRoot => Path.Combine(TemporaryRoot, "app");
        public string StorageRoot => Path.Combine(TemporaryRoot, "private-media");

        public TemporaryMediaDirectory()
        {
            Directory.CreateDirectory(ContentRoot);
            Directory.CreateDirectory(StorageRoot);
        }

        public LocalFileStorageService CreateProvider() => new(StorageRoot, ContentRoot);
        public void Dispose() => Directory.Delete(TemporaryRoot, recursive: true);
    }
}
