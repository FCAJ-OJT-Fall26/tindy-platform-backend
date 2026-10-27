using System.Text;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class PopplerPdfPageRendererTests
{
    [Fact]
    public async Task RendersARealSyntheticPdfToPng()
    {
        var pages = await new PopplerPdfPageRenderer().RenderAsync(Pdf(1));
        var page = Assert.Single(pages);
        Assert.Equal("png", page.Format);
        Assert.True(page.Bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.InRange(page.Bytes.Length, 1, PopplerPdfPageRenderer.MaxImageBytes);
        Assert.InRange(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(page.Bytes.AsSpan(16, 4)), 1, PopplerPdfPageRenderer.MaxImageEdge);
        Assert.InRange(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(page.Bytes.AsSpan(20, 4)), 1, PopplerPdfPageRenderer.MaxImageEdge);
    }

    [Fact]
    public async Task RejectsExtraPagesInsteadOfSilentlyDiscardingThem()
    {
        var error = await Assert.ThrowsAsync<AiException>(() => new PopplerPdfPageRenderer().RenderAsync(Pdf(21)));
        Assert.Equal(AiFailure.DocumentRejected, error.Failure);
    }

    [Fact]
    public async Task MalformedPdfAndMissingRendererHaveSafeErrors()
    {
        var invalid = await Assert.ThrowsAsync<AiException>(() => new PopplerPdfPageRenderer().RenderAsync("%PDF-invalid"u8.ToArray()));
        Assert.Equal(AiFailure.DocumentRejected, invalid.Failure);
        var missing = await Assert.ThrowsAsync<AiException>(() => new PopplerPdfPageRenderer("/nonexistent-tindy-renderer").RenderAsync(Pdf(1)));
        Assert.Equal(AiFailure.Unavailable, missing.Failure);
        Assert.Equal("pdf_renderer_unavailable", missing.Diagnostics!.FailureReason);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedToAnInvalidDocument()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PopplerPdfPageRenderer().RenderAsync(Pdf(1), cancellation.Token));
    }

    internal static byte[] Pdf(int pageCount)
    {
        const string stream = "BT /F1 12 Tf 5 50 Td (Synthetic test) Tj ET";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pageCount} /Kids [{string.Join(' ', Enumerable.Range(5, pageCount).Select(i => $"{i} 0 R"))}] >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        objects.AddRange(Enumerable.Repeat("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F1 4 0 R >> >> /Contents 3 0 R >>", pageCount));
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++) { offsets.Add(text.Length); text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length;
        text.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
