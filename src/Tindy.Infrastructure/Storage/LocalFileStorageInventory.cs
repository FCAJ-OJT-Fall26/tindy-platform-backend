using System.Runtime.CompilerServices;

namespace Tindy.Infrastructure.Storage;

public sealed partial class LocalFileStorageService
{
    public async IAsyncEnumerable<StorageInventoryObject> EnumerateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        if (!Directory.Exists(_rootPath)) yield break;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(_rootPath, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(_rootPath, file).Replace('\\', '/');
            var partial = relative.EndsWith(".partial", StringComparison.Ordinal);
            var key = partial ? relative[..^8] : relative;
            try { StorageObjectKeys.Validate(key); }
            catch (ArgumentException) { continue; } // Unknown files belong to the operator, not this cleanup job.
            yield return new(key, File.GetLastWriteTimeUtc(file), partial);
            await Task.Yield();
        }
    }

    public Task DeleteOrphanAsync(StorageInventoryObject item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var file = Resolve(item.StorageKey) + (item.Partial ? ".partial" : "");
        // Re-check the snapshot to avoid deleting a file modified after enumeration.
        if (File.Exists(file) && File.GetLastWriteTimeUtc(file) <= item.LastModifiedUtc) File.Delete(file);
        return Task.CompletedTask;
    }
}
