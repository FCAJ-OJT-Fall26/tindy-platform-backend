namespace Tindy.Manual.AiProfileExtraction;

internal sealed record RunOptions(Uri BaseUrl, Uri? PdfUrl, string? TextFile)
{
    public static RunOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--base-url" or "--pdf-url" or "--text-file") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(args[i + 1]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Invalid, missing or duplicate option. Run with --help for usage.");
        }

        if (!Uri.TryCreate(values.GetValueOrDefault("--base-url", "http://localhost:5217"),
            UriKind.Absolute, out var baseUrl) ||
            !(baseUrl.Scheme == "https" || (baseUrl.Scheme == "http" && baseUrl.IsLoopback)) ||
            baseUrl.UserInfo.Length != 0 || baseUrl.Query.Length != 0 || baseUrl.Fragment.Length != 0 ||
            baseUrl.AbsolutePath != "/")
            throw new ArgumentException("Base URL must be an HTTPS origin (or HTTP on loopback), without credentials, path, query or fragment.");

        Uri? pdfUrl = null;
        if (values.TryGetValue("--pdf-url", out var pdf) &&
            (!Uri.TryCreate(pdf, UriKind.Absolute, out pdfUrl) || !IsPdfUrl(pdfUrl)))
            throw new ArgumentException("PDF URL must use HTTPS without embedded credentials.");

        var textFile = values.GetValueOrDefault("--text-file");
        if (pdfUrl is null && textFile is null)
            throw new ArgumentException("Provide --pdf-url and/or --text-file. Run with --help for usage.");

        return new RunOptions(baseUrl, pdfUrl, textFile);
    }

    internal static bool IsPdfUrl(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.UserInfo.Length == 0;
}
