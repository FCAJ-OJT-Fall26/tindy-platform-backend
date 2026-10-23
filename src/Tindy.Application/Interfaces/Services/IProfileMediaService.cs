using Tindy.Application.Models.Storage;

namespace Tindy.Application.Interfaces.Services;

public interface IProfileMediaService
{
    Task<ProfileMediaState> UploadAsync(Guid userId, bool avatar, Stream content, string fileName, long length, CancellationToken ct);
    Task RemoveAsync(Guid userId, bool avatar, CancellationToken ct);
    Task<ProfileMediaState> ChooseAvatarAsync(Guid userId, string source, string? provider, CancellationToken ct);
    Task<PrivateMediaRead> ReadAsync(Guid mediaAssetId, CancellationToken ct);
}

public sealed record ProfileMediaState(string? AvatarSource, Guid? AvatarMediaAssetId, Guid? CoverMediaAssetId);

public sealed record PrivateMediaRead(string ContentType, StorageReadHandle Handle);
