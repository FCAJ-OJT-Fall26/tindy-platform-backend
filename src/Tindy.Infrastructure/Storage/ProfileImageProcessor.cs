using SkiaSharp;
using Tindy.Application.Exceptions;

namespace Tindy.Infrastructure.Storage;

/// <summary>Only JPEG/PNG/WebP; decode with pixel guard, crop and re-encode without source metadata.</summary>
public static class ProfileImageProcessor
{
    public sealed record Result(byte[] Bytes, int Width, int Height);
    private const int MaxPixels = 20_000_000;

    public static async Task<Result> ProcessAsync(Stream input, bool avatar, long maximumInputBytes, CancellationToken ct)
    {
        using var received = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (received.Length > maximumInputBytes - read)
                throw new MediaRequestException(413, "MEDIA_FILE_TOO_LARGE", "Image exceeds the allowed upload size.");
            await received.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bytes = received.ToArray();
        if (bytes.Length == 0) throw new MediaRequestException(400, "MEDIA_EMPTY", "Image cannot be empty.");

        // The client-provided Content-Type and extension are not trusted.
        var jpeg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        var png = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 });
        var webp = bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (!jpeg && !png && !webp)
            throw new MediaRequestException(415, "MEDIA_UNSUPPORTED_TYPE", "Only JPEG, PNG and WebP images are supported.");

        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null) throw new InvalidDataException("Invalid image data.");
            if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
                throw new MediaRequestException(415, "MEDIA_UNSUPPORTED_TYPE", "Unsupported image codec.");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
                throw new MediaRequestException(400, "MEDIA_DIMENSIONS_INVALID", "Image dimensions are not allowed.");

            using var source = SKBitmap.Decode(codec);
            if (source is null) throw new InvalidDataException("Unable to decode image.");
            int width = avatar ? 512 : 1500;
            int height = avatar ? 512 : 500;
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null) throw new InvalidOperationException("Unable to create image surface.");
            surface.Canvas.Clear(SKColors.Transparent);
            // Center crop: frontend may present a crop UI before upload.
            var sourceAspect = (float)source.Width / source.Height;
            var targetAspect = (float)width / height;
            SKRect crop;
            if (sourceAspect > targetAspect)
            {
                var croppedWidth = source.Height * targetAspect;
                crop = new SKRect((source.Width - croppedWidth) / 2, 0, (source.Width + croppedWidth) / 2, source.Height);
            }
            else
            {
                var croppedHeight = source.Width / targetAspect;
                crop = new SKRect(0, (source.Height - croppedHeight) / 2, source.Width, (source.Height + croppedHeight) / 2);
            }
            using var paint = new SKPaint { IsAntialias = true };
            using var sourceImage = SKImage.FromBitmap(source);
            surface.Canvas.DrawImage(sourceImage, crop, new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
            surface.Canvas.Flush();
            using var snapshot = surface.Snapshot();
            using var encoded = snapshot.Encode(SKEncodedImageFormat.Webp, 85);
            if (encoded is null || encoded.Size == 0) throw new InvalidOperationException("WebP encoding failed.");
            return new Result(encoded.ToArray(), width, height);
        }
        catch (MediaRequestException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new MediaRequestException(400, "MEDIA_IMAGE_INVALID", "Image is corrupted or cannot be decoded.");
        }
    }
}
