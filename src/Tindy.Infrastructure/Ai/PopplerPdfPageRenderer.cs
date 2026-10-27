using System.ComponentModel;
using System.Diagnostics;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;

namespace Tindy.Infrastructure.Ai;

public interface IPdfPageRenderer
{
    Task<IReadOnlyList<AiDocumentImage>> RenderAsync(byte[] pdf, CancellationToken cancellationToken = default);
}

public sealed class PopplerPdfPageRenderer(string executable = "pdftoppm") : IPdfPageRenderer
{
    public const int MaxPages = 20;
    public const int MaxImageEdge = 1536;
    public const int MaxImageBytes = 3 * 1024 * 1024;
    public const int MaxTotalImageBytes = 20 * 1024 * 1024;

    public async Task<IReadOnlyList<AiDocumentImage>> RenderAsync(byte[] pdf, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pdf.Length > AiProfileLimits.MaxPdfBytes || !pdf.AsSpan().StartsWith("%PDF-"u8))
            throw new AiException(AiFailure.DocumentRejected);
        var directory = Directory.CreateTempSubdirectory("tindy-ai-pdf-");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var source = Path.Combine(directory.FullName, "source.pdf");
            await File.WriteAllBytesAsync(source, pdf, deadline.Token);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            // Render one extra page to detect and reject over-limit documents, never silently truncate.
            foreach (var argument in new[] { "-q", "-png", "-r", "160", "-scale-to", MaxImageEdge.ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "1", "-l",
                         (MaxPages + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), source, Path.Combine(directory.FullName, "page") })
                start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            process.Start();
            using var abort = deadline.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            });
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token),
                    process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, deadline.Token),
                    process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token));
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (process.ExitCode != 0) throw new AiException(AiFailure.DocumentRejected);
            var files = directory.GetFiles("page-*.png").OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
            if (files.Length is 0 or > MaxPages || files.Any(file => file.Length is <= 0 or > MaxImageBytes) ||
                files.Sum(file => file.Length) > MaxTotalImageBytes)
                throw new AiException(AiFailure.DocumentRejected);
            var images = new List<AiDocumentImage>(files.Length);
            foreach (var file in files)
            {
                var bytes = await File.ReadAllBytesAsync(file.FullName, deadline.Token);
                if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new AiException(AiFailure.DocumentRejected);
                images.Add(new("png", bytes));
            }
            return images.AsReadOnly();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException(AiFailure.Timeout) { Diagnostics = new(null, null, null, 0, 20_000, "pdf_render_timeout", "document") };
        }
        catch (Win32Exception)
        {
            throw new AiException(AiFailure.Unavailable) { Diagnostics = new(null, null, null, 0, 0, "pdf_renderer_unavailable", "document") };
        }
        finally { directory.Delete(recursive: true); }
    }
}
