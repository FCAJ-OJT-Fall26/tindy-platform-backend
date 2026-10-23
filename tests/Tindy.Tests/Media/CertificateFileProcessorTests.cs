using SkiaSharp;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Storage;
using Xunit;

namespace Tindy.Tests.Media;

public sealed class CertificateFileProcessorTests
{
    internal static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(321, 123);
        bitmap.Erase(SKColors.White);
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(320, 122, SKColors.Blue);
        using var data = bitmap.Encode(format, 100);
        return data.ToArray();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Certificate_ReencodesWithoutCropping(SKEncodedImageFormat format)
    {
        var bytes = Image(format);
        var result = await new CertificateFileProcessor().ProcessAsync(new MemoryStream(bytes), false, bytes.Length, default);
        using var decoded = SKBitmap.Decode(result.Bytes);
        Assert.Equal(321, decoded.Width);
        Assert.Equal(123, decoded.Height);
        if (format == SKEncodedImageFormat.Png)
        {
            Assert.Equal(SKColors.Red, decoded.GetPixel(0, 0));
            Assert.Equal(SKColors.Blue, decoded.GetPixel(320, 122));
        }
    }

    [Theory]
    [InlineData("%PDF-1.7\nnot-an-image", false, 415)]
    [InlineData("fake.pdf", true, 415)]
    [InlineData("%PDF-1.7\nmissing trailer", true, 415)]
    [InlineData("%PDF-1.x\n%%EOF", true, 415)]
    [InlineData("<svg/>", false, 415)]
    public async Task ForgedContent_IsRejected(string text, bool pdf, int status)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var error = await Assert.ThrowsAsync<MediaRequestException>(() => new CertificateFileProcessor()
            .ProcessAsync(new MemoryStream(bytes), pdf, bytes.Length, default));
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task ActualStreamLength_IsBoundedAndMustMatch()
    {
        var processor = new CertificateFileProcessor();
        var error = await Assert.ThrowsAsync<MediaRequestException>(() => processor.ProcessAsync(new MemoryStream(new byte[10_000_001]), true, 1, default));
        Assert.Equal(413, error.StatusCode);
        var bytes = Image();
        error = await Assert.ThrowsAsync<MediaRequestException>(() => processor.ProcessAsync(new MemoryStream(bytes), false, 1, default));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task ExcessivePixelCount_IsRejectedBeforeDecode()
    {
        using var bitmap = new SKBitmap(5001, 4000);
        bitmap.Erase(SKColors.White);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        var error = await Assert.ThrowsAsync<MediaRequestException>(() => new CertificateFileProcessor().ProcessAsync(new MemoryStream(bytes), false, bytes.Length, default));
        Assert.Equal("MEDIA_DIMENSIONS_INVALID", error.ErrorCode);
    }
}
