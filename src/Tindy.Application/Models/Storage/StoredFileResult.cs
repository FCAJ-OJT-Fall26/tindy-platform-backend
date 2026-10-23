namespace Tindy.Application.Models.Storage;

// These values can be persisted in media_assets; never expose StorageKey in a public API DTO.
public sealed record StoredFileResult(
    string StorageProvider,
    string StorageContainer,
    string StorageKey,
    long FileSizeBytes,
    string ContentType,
    string? ObjectETag,
    string? ObjectVersionId);
