using SkiaSharp;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Services;

namespace Tindy.Infrastructure.Storage;

public sealed class CertificateFileProcessor : ICertificateFileProcessor
{
    public async Task<PreparedCertificateFile> ProcessAsync(Stream content, bool pdf, long expectedLength, CancellationToken ct)
    {
        MemoryStream received;
        try { received = await BoundedStorageContent.ReadAsync(content, CertificateAttachmentService.MaxFileBytes, ct); }
        catch (InvalidDataException) { throw new MediaRequestException(413, "MEDIA_FILE_TOO_LARGE", "File is empty or exceeds the 10 MB limit."); }
        using (received)
        {
            var bytes = received.ToArray();
            if (bytes.LongLength != expectedLength)
                throw new MediaRequestException(400, "MEDIA_LENGTH_INVALID", "File length does not match the upload.");
            if (pdf)
            {
                // This is a format gate, not a malware verdict. Every PDF still requires scanning.
                if (bytes.Length < 14 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) || bytes[5] is < (byte)'1' or > (byte)'2' ||
                    bytes[6] != '.' || bytes[7] is < (byte)'0' or > (byte)'9' ||
                    bytes.AsSpan(Math.Max(0, bytes.Length - 1024)).LastIndexOf("%%EOF"u8) < 0)
                    throw new MediaRequestException(415, "MEDIA_UNSUPPORTED_TYPE", "Invalid PDF signature.");
                return new(bytes, null, null);
            }

            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
                throw new MediaRequestException(415, "MEDIA_UNSUPPORTED_TYPE", "Only JPEG, PNG and WebP images are supported.");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 20_000_000 || codec.FrameCount > 1)
                throw new MediaRequestException(400, "MEDIA_DIMENSIONS_INVALID", "Image dimensions or animation are not allowed.");
            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new MediaRequestException(400, "MEDIA_IMAGE_INVALID", "Image is corrupted or cannot be decoded.");
            // Preserve every pixel, including text/QR codes; no resize, crop or lossy compression.
            using var pixmap = bitmap.PeekPixels();
            using var encoded = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100));
            if (encoded is null || encoded.Size == 0)
                throw new MediaRequestException(400, "MEDIA_IMAGE_INVALID", "Image cannot be encoded.");
            ct.ThrowIfCancellationRequested();
            return new(encoded.ToArray(), info.Width, info.Height);
        }
    }
}
