namespace Tindy.Application.Models.Storage;

// The caller owns Content; SaveAsync never disposes it. Keys and extensions are generated server-side.
public sealed record StorageSaveRequest(Guid OwnerUserId, MediaStorageKind Kind, Stream Content);
