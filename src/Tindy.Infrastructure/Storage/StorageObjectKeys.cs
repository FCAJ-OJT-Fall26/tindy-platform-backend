using System.Text.RegularExpressions;
using Tindy.Application.Models.Storage;

namespace Tindy.Infrastructure.Storage;

internal static partial class StorageObjectKeys
{
    // Strictly server-generated shape. Never accept a filename or path from the client.
    [GeneratedRegex(@"\A(?:profiles/(?:avatars|covers)|certificates/(?:images|pdf))/[0-9a-f]{32}/[0-9a-f]{32}\.(?:webp|pdf)\z", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedKeyPattern();

    public static (string Key, string ContentType, long MaxBytes) Create(Guid ownerId, MediaStorageKind kind)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("A valid owner ID is required.", nameof(ownerId));

        var (prefix, extension, contentType, maxBytes) = kind switch
        {
            MediaStorageKind.Avatar => ("profiles/avatars", "webp", "image/webp", 5_000_000L),
            MediaStorageKind.Cover => ("profiles/covers", "webp", "image/webp", 10_000_000L),
            MediaStorageKind.CertificateImage => ("certificates/images", "webp", "image/webp", 10_000_000L),
            MediaStorageKind.CertificatePdf => ("certificates/pdf", "pdf", "application/pdf", 10_000_000L),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Unsupported storage kind.")
        };
        return ($"{prefix}/{ownerId:N}/{Guid.NewGuid():N}.{extension}", contentType, maxBytes);
    }

    public static string Validate(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !GeneratedKeyPattern().IsMatch(storageKey))
            throw new ArgumentException("Invalid storage object key.", nameof(storageKey));

        // Additional cross-field checks: a PDF may only live under the PDF prefix.
        if (storageKey.EndsWith(".pdf", StringComparison.Ordinal) != storageKey.StartsWith("certificates/pdf/", StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage object key.", nameof(storageKey));
        return storageKey;
    }
}
