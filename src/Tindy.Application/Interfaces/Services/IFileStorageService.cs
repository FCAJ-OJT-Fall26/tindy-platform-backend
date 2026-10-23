using Tindy.Application.Models.Storage;

namespace Tindy.Application.Interfaces.Services;

// Infrastructure owns the storage technology; Application knows only this contract.
public interface IFileStorageService
{
    Task<StoredFileResult> SaveAsync(StorageSaveRequest request, CancellationToken cancellationToken = default);
    Task<StorageReadHandle> OpenReadAsync(string storageKey, string? objectVersionId = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, string? objectVersionId = null, CancellationToken cancellationToken = default);
}
