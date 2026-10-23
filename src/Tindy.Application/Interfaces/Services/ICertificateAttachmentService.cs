using Tindy.Application.DTOs.Profile;
using Tindy.Application.Models.Storage;

namespace Tindy.Application.Interfaces.Services;

public interface ICertificateAttachmentService
{
    Task<CertificateAttachmentResponse> UploadAsync(Guid ownerId, Guid certificateId, Stream content, string fileName, long length, CancellationToken ct);
    Task<IReadOnlyList<CertificateAttachmentResponse>> ListAsync(Guid callerId, Guid ownerId, Guid certificateId, CancellationToken ct);
    Task<CertificateAttachmentResponse> ChangeVisibilityAsync(Guid ownerId, Guid certificateId, Guid attachmentId, string visibility, CancellationToken ct);
    Task DeleteAsync(Guid ownerId, Guid certificateId, Guid attachmentId, CancellationToken ct);
    Task<MediaQuotaResponse> GetQuotaAsync(Guid ownerId, CancellationToken ct);
}

public interface ICertificateFileProcessor
{
    Task<PreparedCertificateFile> ProcessAsync(Stream content, bool pdf, long expectedLength, CancellationToken ct);
}

public sealed record PreparedCertificateFile(byte[] Bytes, int? Width, int? Height);
