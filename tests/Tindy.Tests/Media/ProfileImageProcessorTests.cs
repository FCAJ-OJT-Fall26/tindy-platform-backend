using SkiaSharp;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Storage;
using Xunit;

namespace Tindy.Tests.Media;

public sealed class ProfileImageProcessorTests
{
    private static byte[] SampleImage(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(640, 400);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Avatar_JpegPngWebp_BecomesSafeWebp(SKEncodedImageFormat format)
    {
        await using var input = new MemoryStream(SampleImage(format));
        var result = await ProfileImageProcessor.ProcessAsync(input, true, 5_000_000, CancellationToken.None);
        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(result.Bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(result.Bytes, 8, 4));
        using var decoded = SKBitmap.Decode(result.Bytes);
        Assert.Equal(512, decoded.Width);
        Assert.Equal(512, decoded.Height);
    }

    [Fact]
    public async Task Cover_NormalizesToThreeToOne()
    {
        await using var input = new MemoryStream(SampleImage(SKEncodedImageFormat.Png));
        var result = await ProfileImageProcessor.ProcessAsync(input, false, 10_000_000, CancellationToken.None);
        Assert.Equal(1500, result.Width);
        Assert.Equal(500, result.Height);
    }

    [Fact]
    public async Task FakeImage_IsRejected()
    {
        await using var input = new MemoryStream("Not really an image"u8.ToArray());
        var error = await Assert.ThrowsAsync<MediaRequestException>(() =>
            ProfileImageProcessor.ProcessAsync(input, true, 5_000_000, CancellationToken.None));
        Assert.Equal(415, error.StatusCode);
    }

    [Fact]
    public async Task HugeInput_IsRejectedBeforeDecoding()
    {
        await using var input = new MemoryStream(new byte[5_000_001]);
        var error = await Assert.ThrowsAsync<MediaRequestException>(() =>
            ProfileImageProcessor.ProcessAsync(input, true, 5_000_000, CancellationToken.None));
        Assert.Equal(413, error.StatusCode);
    }
}
