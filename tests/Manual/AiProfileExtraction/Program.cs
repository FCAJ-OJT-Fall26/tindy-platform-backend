using Tindy.Manual.AiProfileExtraction;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("AI profile extraction manual test (calls live Bedrock through your backend).\n" +
        "  --pdf-url <https-url> and/or --text-file <path>\n" +
        "  --base-url <url> (default: http://localhost:5217)\n" +
        "Set TINDY_ACCESS_TOKEN to your Tindy JWT.\n" +
        "  --self-test runs offline checks without credentials.");
    return 0;
}

if (args is ["--self-test"])
    return await SelfTests.RunAsync(Console.Out);

RunOptions options;
try
{
    options = RunOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

// Disable redirects on both clients: only the PDF flow follows redirects explicitly.
using var apiClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
{
    Timeout = TimeSpan.FromSeconds(90)
};
using var pdfClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
{
    Timeout = Timeout.InfiniteTimeSpan
};
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
return await new ExtractionRunner(apiClient, pdfClient, Console.Out)
    .RunAsync(options, Environment.GetEnvironmentVariable("TINDY_ACCESS_TOKEN"), cancellation.Token);
