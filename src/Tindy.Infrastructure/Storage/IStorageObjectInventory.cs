namespace Tindy.Infrastructure.Storage;

// Operational storage API; never exposed by HTTP. Only validated generated keys are enumerated.
public sealed record StorageInventoryObject(string StorageKey, DateTime LastModifiedUtc, bool Partial = false);

public interface IStorageObjectInventory
{
    IAsyncEnumerable<StorageInventoryObject> EnumerateAsync(CancellationToken ct);
    Task DeleteOrphanAsync(StorageInventoryObject item, CancellationToken ct);
}
