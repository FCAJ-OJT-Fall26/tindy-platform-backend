using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Storage;

namespace Tindy.Infrastructure.Storage;

public sealed class S3FileStorageService : IFileStorageService, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly bool _ownsClient;

    public S3FileStorageService(IAmazonS3 s3, string bucketName, bool ownsClient = false)
    {
        _s3 = s3 ?? throw new ArgumentNullException(nameof(s3));
        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("An S3 bucket name is required.", nameof(bucketName));
        _bucket = bucketName;
        _ownsClient = ownsClient;
    }

    public StorageWriteTarget CreateWriteTarget(Guid ownerUserId, MediaStorageKind kind) =>
        new("s3", _bucket, StorageObjectKeys.Create(ownerUserId, kind).Key);

    public bool CanAccess(string storageProvider, string storageContainer) => storageProvider == "s3" && storageContainer == _bucket;

    public async Task<StoredFileResult> SaveAsync(StorageSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (generatedKey, contentType, maxBytes) = StorageObjectKeys.Create(request.OwnerUserId, request.Kind);
        StorageObjectKeys.ValidateTarget(request, "s3", _bucket);
        var key = request.Target?.StorageKey ?? generatedKey;
        await using var data = await BoundedStorageContent.ReadAsync(request.Content, maxBytes, cancellationToken);
        var length = data.Length;
        try
        {
            var response = await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = data,
                AutoCloseStream = false,
                ContentType = contentType,
                IfNoneMatch = "*" // Never overwrite another object.
                // Never add a public ACL or public URL here.
            }, cancellationToken);

            return new StoredFileResult("s3", _bucket, key, length, contentType, response.ETag, response.VersionId);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // IfNoneMatch prevented an overwrite; never delete the existing object.
            throw;
        }
        catch
        {
            // An interrupted PUT can leave an orphan. This is best-effort only;
            // STEP 05 adds durable orphan reconciliation and retries.
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await DeleteAsync(key, cancellationToken: cleanupTimeout.Token);
            }
            catch { /* Preserve the original upload exception. */ }
            throw;
        }
    }

    public async Task<StorageReadHandle> OpenReadAsync(string storageKey, string? objectVersionId = null, CancellationToken cancellationToken = default)
    {
        var key = StorageObjectKeys.Validate(storageKey);
        try
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                VersionId = objectVersionId
            }, cancellationToken);
            // Disposing the handle closes BOTH the response stream and the HTTP response.
            if (response.ResponseStream is null)
            {
                response.Dispose();
                throw new IOException("S3 returned no content stream.");
            }
            return new StorageReadHandle(response.ResponseStream, response, response.ETag);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Media object not found.", ex);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _s3.Dispose();
    }

    public async Task DeleteAsync(string storageKey, string? objectVersionId = null, CancellationToken cancellationToken = default)
    {
        var key = StorageObjectKeys.Validate(storageKey);
        try
        {
            if (objectVersionId is null)
            {
                // A crash may occur after PUT but before its VersionId is persisted.
                // Immutable generated keys allow resolving that exact current version.
                var metadata = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, cancellationToken);
                objectVersionId = metadata.VersionId;
            }
            // Explicit version ensures cleanup removes the stored version, not just a delete marker.
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                VersionId = objectVersionId
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Idempotent delete, including already-removed object versions.
        }
    }
}
