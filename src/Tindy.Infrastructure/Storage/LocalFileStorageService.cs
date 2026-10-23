using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Storage;

namespace Tindy.Infrastructure.Storage;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly StringComparison _comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public LocalFileStorageService(string rootPath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
            throw new InvalidOperationException("MediaStorage:Local:RootPath must be an absolute path outside the application.");
        if (string.IsNullOrWhiteSpace(contentRootPath)) throw new ArgumentException("Content root is required.", nameof(contentRootPath));

        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (_rootPath.Equals(Path.GetPathRoot(_rootPath), _comparison))
            throw new InvalidOperationException("The filesystem root cannot be used for media storage.");
        _rootPrefix = _rootPath + Path.DirectorySeparatorChar;
        var contentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRootPath));
        if (_rootPath.Equals(contentRoot, _comparison) || _rootPath.StartsWith(contentRoot + Path.DirectorySeparatorChar, _comparison)
            || contentRoot.StartsWith(_rootPrefix, _comparison))
            throw new InvalidOperationException("Local media root must be outside the application content root.");

        // Do not create a directory until the first write.
    }

    public async Task<StoredFileResult> SaveAsync(StorageSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (key, contentType, maxBytes) = StorageObjectKeys.Create(request.OwnerUserId, request.Kind);
        await using var data = await BoundedStorageContent.ReadAsync(request.Content, maxBytes, cancellationToken);
        var destination = Resolve(key);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = destination + ".partial";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await data.CopyToAsync(file, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination); // Same-directory atomic move; never overwrite.
            return new StoredFileResult("local", "local", key, data.Length, contentType, null, null);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<StorageReadHandle> OpenReadAsync(string storageKey, string? objectVersionId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (objectVersionId is not null) throw new ArgumentException("Local storage has no object versions.", nameof(objectVersionId));
        var path = Resolve(storageKey);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return Task.FromResult(new StorageReadHandle(stream));
    }

    public Task DeleteAsync(string storageKey, string? objectVersionId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (objectVersionId is not null) throw new ArgumentException("Local storage has no object versions.", nameof(objectVersionId));
        var path = Resolve(storageKey);
        File.Delete(path); // .NET's File.Delete does nothing when the file does not exist.
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        var key = StorageObjectKeys.Validate(storageKey);
        var path = Path.GetFullPath(Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_rootPrefix, _comparison))
            throw new InvalidOperationException("Storage key resolves outside the local media root.");
        return path;
    }
}
