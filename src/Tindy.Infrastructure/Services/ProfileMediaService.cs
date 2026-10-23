using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Storage;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Storage;

namespace Tindy.Infrastructure.Services;

public sealed class ProfileMediaService : IProfileMediaService
{
    private const long MaxAvatarBytes = 5_000_000;
    private const long MaxCoverBytes = 10_000_000;
    private readonly TindyDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IHttpClientFactory _httpClientFactory;
    // An instance-scoped throttle for a single backend instance. Distributed upload
    // concurrency limits must be added before multi-instance production deployment.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Slots = new();

    public ProfileMediaService(TindyDbContext db, IFileStorageService storage, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _storage = storage;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ProfileMediaState> UploadAsync(Guid userId, bool avatar, Stream content,
        string fileName, long length, CancellationToken ct)
    {
        if (length <= 0) throw new MediaRequestException(400, "MEDIA_EMPTY", "Image cannot be empty.");
        var max = avatar ? MaxAvatarBytes : MaxCoverBytes;
        if (length > max) throw new MediaRequestException(413, "MEDIA_FILE_TOO_LARGE", "Image exceeds the allowed upload size.");
        return await WithUploadSlot(userId, async () =>
        {
            var prepared = await ProfileImageProcessor.ProcessAsync(content, avatar, max, ct);
            return await StoreAndSelectAsync(userId, avatar, prepared, fileName, null, ct);
        }, ct);
    }

    public async Task<ProfileMediaState> ChooseAvatarAsync(Guid userId, string source,
        string? provider, CancellationToken ct)
    {
        if (source == "default" && provider is null)
        {
            await RemoveAsync(userId, true, ct);
            var cover = await _db.UserProfiles.AsNoTracking().Where(x => x.UserId == userId)
                .Select(x => x.CoverAssetId).SingleAsync(ct);
            return new ProfileMediaState("default", null, cover);
        }
        if (source != "oauth" || provider is not ("google" or "github"))
            throw new MediaRequestException(400, "MEDIA_INVALID_SOURCE", "Use source 'default' or 'oauth' with provider 'google'/'github'.");

        var providerName = provider == "google" ? "Google" : "GitHub";
        return await WithUploadSlot(userId, async () =>
        {
            var url = await _db.ExternalAuthAccounts.AsNoTracking()
                .Where(x => x.UserId == userId && x.Provider == providerName)
                .Select(x => x.ProviderAvatarUrl).SingleOrDefaultAsync(ct);
            if (url is null) throw new MediaRequestException(409, "MEDIA_OAUTH_AVATAR_UNAVAILABLE", "Linked provider avatar is unavailable.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsTrustedAvatarHost(uri, providerName))
                throw new MediaRequestException(409, "MEDIA_OAUTH_AVATAR_UNAVAILABLE", "Provider avatar URL is not trusted.");

            var client = _httpClientFactory.CreateClient("tindy-provider-avatar");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaxAvatarBytes)
                throw new MediaRequestException(409, "MEDIA_OAUTH_AVATAR_UNAVAILABLE", "Provider avatar cannot be retrieved.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var prepared = await ProfileImageProcessor.ProcessAsync(stream, true, MaxAvatarBytes, ct);
            return await StoreAndSelectAsync(userId, true, prepared, "provider-avatar.webp", providerName, ct);
        }, ct);
    }

    private static bool IsTrustedAvatarHost(Uri uri, string provider) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        (provider == "GitHub" ? uri.Host.Equals("avatars.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            : uri.Host.Equals("lh3.googleusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static async Task<T> WithUploadSlot<T>(Guid ownerId, Func<Task<T>> operation, CancellationToken ct)
    {
        var gate = Slots.GetOrAdd(ownerId, _ => new SemaphoreSlim(2, 2));
        if (!await gate.WaitAsync(0, ct)) throw new MediaRequestException(429, "MEDIA_CONCURRENT_LIMIT", "Maximum two concurrent uploads per user.");
        try { return await operation(); }
        finally { gate.Release(); }
    }

    private async Task<ProfileMediaState> StoreAndSelectAsync(Guid userId, bool avatar,
        ProfileImageProcessor.Result image, string fileName, string? provider, CancellationToken ct)
    {
        var limit = avatar ? MaxAvatarBytes : MaxCoverBytes;
        if (image.Bytes.Length == 0 || image.Bytes.Length > limit)
            throw new MediaRequestException(413, "MEDIA_FILE_TOO_LARGE", "Processed image exceeds the storage limit.");
        var kind = avatar ? MediaStorageKind.Avatar : MediaStorageKind.Cover;
        var target = _storage.CreateWriteTarget(userId, kind);
        var mediaId = Guid.NewGuid();
        await using (var reserve = await _db.Database.BeginTransactionAsync(ct))
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {userId} FOR UPDATE", ct);
            if (!await _db.Users.AnyAsync(u => u.UserId == userId && u.Status == "active" && u.DeletedAt == null && u.IsOnboardingCompleted && u.Profile != null, ct))
                throw new MediaRequestException(404, "MEDIA_PROFILE_NOT_FOUND", "Profile not found.");
            _db.MediaAssets.Add(new MediaAsset
            {
                MediaAssetId = mediaId, OwnerUserId = userId, Purpose = avatar ? "avatar" : "cover",
                StorageProvider = target.StorageProvider, StorageContainer = target.StorageContainer, StorageKey = target.StorageKey,
                ContentType = "image/webp", ReservedBytes = image.Bytes.Length, Status = "pending_upload", ScanStatus = "not_required",
                CreatedAt = DateTime.UtcNow, UploadExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
            await _db.SaveChangesAsync(ct);
            await reserve.CommitAsync(ct);
        }
        await using var input = new MemoryStream(image.Bytes, writable: false);
        var committed = false;
        try
        {
            using var uploadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            uploadTimeout.CancelAfter(TimeSpan.FromMinutes(14));
            var stored = await _storage.SaveAsync(new StorageSaveRequest(userId, kind, input, target), uploadTimeout.Token);
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Serialize replaces, deletes and OAuth avatar-source changes for this owner.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {userId} FOR UPDATE", ct);
            var profile = await _db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == userId, ct)
                ?? throw new MediaRequestException(404, "MEDIA_PROFILE_NOT_FOUND", "Profile not found.");
            if (!await _db.Users.AnyAsync(x => x.UserId == userId && x.DeletedAt == null &&
                x.Status == "active" && x.IsOnboardingCompleted, ct))
                throw new MediaRequestException(403, "MEDIA_ACCESS_DENIED", "Profile is not available.");
            if (provider is not null && !await _db.ExternalAuthAccounts.AnyAsync(x => x.UserId == userId && x.Provider == provider, ct))
                throw new MediaRequestException(409, "MEDIA_OAUTH_UNLINKED", "The selected provider is no longer linked.");

            var now = DateTime.UtcNow;
            var asset = await _db.MediaAssets.SingleOrDefaultAsync(a => a.MediaAssetId == mediaId && a.Status == "pending_upload", ct);
            if (asset is null || asset.UploadExpiresAt <= now)
                throw new MediaRequestException(409, "MEDIA_UPLOAD_EXPIRED", "Upload reservation has expired.");
            asset.ObjectEtag = stored.ObjectETag;
            asset.ObjectVersionId = stored.ObjectVersionId;
            asset.FileSizeBytes = stored.FileSizeBytes;
            asset.ContentType = stored.ContentType;
            asset.Width = image.Width;
            asset.Height = image.Height;
            asset.ReservedBytes = 0;
            asset.Status = "active";
            asset.ActivatedAt = now;
            asset.UploadExpiresAt = null;
            var oldId = avatar ? profile.AvatarAssetId : profile.CoverAssetId;
            if (avatar)
            {
                profile.AvatarAssetId = asset.MediaAssetId;
                profile.AvatarSource = provider is null ? "uploaded" : "oauth";
                profile.AvatarOAuthProvider = provider;
                profile.AvatarUrl = null;
            }
            else
            {
                profile.CoverAssetId = asset.MediaAssetId;
                profile.CoverImageUrl = null;
            }
            profile.UpdatedAt = now;
            if (oldId.HasValue) await MarkOldAsync(userId, oldId.Value, now, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            committed = true;
            return new ProfileMediaState(profile.AvatarSource, profile.AvatarAssetId, profile.CoverAssetId);
        }
        catch
        {
            // Durable cleanup instead of deleting an object after an ambiguous DB commit.
            if (!committed)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    _db.ChangeTracker.Clear();
                    await using var rollback = await _db.Database.BeginTransactionAsync(cleanup.Token);
                    await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {userId} FOR UPDATE", cleanup.Token);
                    var pending = await _db.MediaAssets.SingleOrDefaultAsync(a => a.MediaAssetId == mediaId && a.Status == "pending_upload", cleanup.Token);
                    if (pending is not null) CertificateAttachmentService.MarkForDeletion(pending);
                    await _db.SaveChangesAsync(cleanup.Token);
                    await rollback.CommitAsync(cleanup.Token);
                }
                catch { /* The persisted upload reservation still expires after 15 minutes. */ }
            }
            throw;
        }
    }

    public async Task RemoveAsync(Guid userId, bool avatar, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {userId} FOR UPDATE", ct);
        var profile = await _db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == userId, ct)
            ?? throw new MediaRequestException(404, "MEDIA_PROFILE_NOT_FOUND", "Profile not found.");
        var oldId = avatar ? profile.AvatarAssetId : profile.CoverAssetId;
        if (avatar)
        {
            profile.AvatarSource = "default";
            profile.AvatarAssetId = null;
            profile.AvatarOAuthProvider = null;
            profile.AvatarUrl = null;
        }
        else
        {
            profile.CoverAssetId = null;
            profile.CoverImageUrl = null;
        }
        profile.UpdatedAt = DateTime.UtcNow;
        if (oldId.HasValue) await MarkOldAsync(userId, oldId.Value, DateTime.UtcNow, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task MarkOldAsync(Guid ownerId, Guid mediaId, DateTime now, CancellationToken ct)
    {
        var old = await _db.MediaAssets.SingleOrDefaultAsync(x => x.MediaAssetId == mediaId && x.OwnerUserId == ownerId, ct);
        if (old is null) return;
        old.Status = "pending_delete";
        old.DeleteRequestedAt = now;
        old.NextCleanupAt = now;
        old.UpdatedAt = now;
    }

    public async Task<PrivateMediaRead> ReadAsync(Guid callerUserId, Guid mediaAssetId, CancellationToken ct)
    {
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == callerUserId && u.Status == "active" &&
            u.DeletedAt == null && u.IsOnboardingCompleted && u.Profile != null, ct))
            throw new MediaRequestException(404, "MEDIA_NOT_FOUND", "Media not found.");
        // Caller authorization and a live relationship are required even for a known UUID.
        var asset = await _db.MediaAssets.AsNoTracking()
            .Where(a => a.MediaAssetId == mediaAssetId && a.Status == "active" &&
                (a.ContentType != "application/pdf" || a.ScanStatus == "clean") &&
                _db.Users.Any(u => u.UserId == a.OwnerUserId && u.Status == "active" &&
                    u.DeletedAt == null && u.IsOnboardingCompleted && u.Profile != null) &&
                (((a.Purpose == "avatar" || a.Purpose == "cover") &&
                    _db.UserProfiles.Any(p => p.UserId == a.OwnerUserId && (a.Purpose == "avatar" ? p.AvatarAssetId == a.MediaAssetId : p.CoverAssetId == a.MediaAssetId))) ||
                 (a.Purpose == "certificate" && _db.UserCertificateAttachments.Any(t => t.MediaAssetId == a.MediaAssetId &&
                    t.OwnerUserId == a.OwnerUserId && (t.OwnerUserId == callerUserId || t.Visibility == "members") &&
                    t.Certificate.UserId == a.OwnerUserId))))
            .Select(a => new { a.StorageProvider, a.StorageContainer, a.StorageKey, a.ObjectVersionId, a.ObjectEtag, a.ContentType })
            .SingleOrDefaultAsync(ct);
        if (asset is null || asset.ContentType is not ("image/webp" or "application/pdf") ||
            !_storage.CanAccess(asset.StorageProvider, asset.StorageContainer))
            throw new MediaRequestException(404, "MEDIA_NOT_FOUND", "Media not found.");
        try
        {
            var handle = await _storage.OpenReadAsync(asset.StorageKey, asset.ObjectVersionId, ct);
            if (asset.StorageProvider == "s3" && (string.IsNullOrWhiteSpace(asset.ObjectEtag) ||
                asset.ObjectEtag.Trim('"') != handle.ObjectETag?.Trim('"')))
            {
                await handle.DisposeAsync();
                throw new MediaRequestException(404, "MEDIA_NOT_FOUND", "Media not found.");
            }
            return new PrivateMediaRead(asset.ContentType, handle);
        }
        catch (FileNotFoundException) { throw new MediaRequestException(404, "MEDIA_NOT_FOUND", "Media not found."); }
    }
}
