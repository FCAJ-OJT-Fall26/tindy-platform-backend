using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Services;
using Tindy.Infrastructure.Extensions;
using Tindy.Manual.AiListExtraction;

if (args is ["--self-test"])
{
    var skillId = Guid.NewGuid();
    var expected = new Expectations(["Python"], ["PostgreSQL"], [new("Example", "Developer", "2021-01", null, true)], [new("Certificate")]);
    var catalog = new Dictionary<Guid, string> { [skillId] = "Python" };
    var valid = new ProfileDraftResponse
    {
        Profile = new() { SkillIds = [skillId] },
        Experiences = [new() { Description = "Worked at Example", RoleTitle = "Developer", StartDate = "2021-01", IsOngoing = true }],
        Certificates = [new() { Title = "Certificate" }]
    };
    if (Review.Check(valid, catalog, expected).Count != 0 ||
        Review.Check(valid with { Certificates = [new() { Title = "Certificate", IssuedAt = "2021-01-01" }] }, catalog, expected).Count == 0 ||
        Review.Check(valid, new Dictionary<Guid, string> { [skillId] = "PostgreSQL" }, expected).Count == 0)
        throw new InvalidOperationException("Review self-test failed.");
    var scalarRules = expected with { Profile = new()
    {
        ["display_name"] = JsonSerializer.SerializeToElement("Nguyễn An"),
        ["preferred_role_id"] = JsonSerializer.SerializeToElement<object?>(null)
    } };
    if (Review.CheckScalars(new() { DisplayName = "Nguyễn An" }, scalarRules).Count != 0 ||
        Review.CheckScalars(new() { DisplayName = "Wrong person" }, scalarRules).Count == 0 ||
        Review.CheckScalars(new() { DisplayName = "Nguyễn An", PreferredRoleId = Guid.NewGuid() }, scalarRules).Count == 0)
        throw new InvalidOperationException("Scalar review self-test failed.");
    Console.WriteLine("PASS: reviewer accepts grounded profile/lists and rejects wrong identity, inferred role, invented dates and database skills.");
    return 0;
}
if (!args.Contains("--live", StringComparer.Ordinal))
{
    Console.WriteLine("Explicit live test: --live [--full-profile] --pdf-file <path> --expectations <json> --runs <1-3> --report <outside-repo.json>\nRun from backend/. Reads Development User Secrets and active catalog without account/database writes. Default: lists only (one model call); --full-profile: production combined service (two parallel calls). --self-test is fully offline.");
    return args.Length == 0 || args is ["--help"] ? 0 : 1;
}
var options = new Dictionary<string, string>(StringComparer.Ordinal);
var fullProfile = args.Contains("--full-profile", StringComparer.Ordinal);
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--live" or "--full-profile") continue;
    if (args[i] is not ("--pdf-file" or "--expectations" or "--runs" or "--report") ||
        i + 1 >= args.Length || !options.TryAdd(args[i], args[++i]))
        throw new ArgumentException("Invalid or duplicate option.");
}
if (!options.TryGetValue("--pdf-file", out var pdfPath) || !options.TryGetValue("--expectations", out var expectedPath))
    throw new ArgumentException("Provide --pdf-file and --expectations.");
var runs = int.Parse(options.GetValueOrDefault("--runs", "1"));
if (runs is < 1 or > 3) throw new ArgumentException("Runs must be between 1 and 3; there is no automatic retry.");
string? reportDestination = null;
if (options.TryGetValue("--report", out var destination))
{
    reportDestination = Path.GetFullPath(destination);
    var repo = Path.GetFullPath(Environment.CurrentDirectory) + Path.DirectorySeparatorChar;
    if (reportDestination.StartsWith(repo, StringComparison.Ordinal)) throw new ArgumentException("Reports must be outside the backend repository.");
    if (!Directory.Exists(Path.GetDirectoryName(reportDestination))) throw new ArgumentException("The report directory must already exist.");
}
var expectedRules = JsonSerializer.Deserialize<Expectations>(await File.ReadAllTextAsync(expectedPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new ArgumentException("Invalid expectations JSON.");
if (fullProfile && expectedRules.Profile is not { Count: > 0 }) throw new ArgumentException("Full-profile mode requires profile expectations.");
var pdf = await File.ReadAllBytesAsync(pdfPath);
if (pdf.Length == 0 || pdf.Length > AiProfileLimits.MaxPdfBytes || !pdf.AsSpan().StartsWith("%PDF-"u8)) throw new ArgumentException("Expected one valid PDF up to 4 MiB.");
var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("src/Tindy.Api/appsettings.json"))
    .AddUserSecrets("3eb0078b-99cb-4318-900f-7ca7d9f005dc").AddEnvironmentVariables().Build();
var services = new ServiceCollection();
services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
services.AddInfrastructure(config);
services.AddScoped<IAiListExtractionService, AiListExtractionService>();
services.AddScoped<IAiProfileService, AiProfileService>();
await using var container = services.BuildServiceProvider();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
var reports = new List<object>();
var passed = 0;
for (var run = 1; run <= runs; run++)
{
    await using var scope = container.CreateAsyncScope();
    try
    {
        var rows = await scope.ServiceProvider.GetRequiredService<ICatalogRepository>().GetActiveSkillsAsync(null, null, cancellation.Token);
        var catalog = rows.Where(item => item.IsActive).ToDictionary(item => item.SkillId, item => item.Name);
        var result = fullProfile
            ? await scope.ServiceProvider.GetRequiredService<IAiProfileService>().ExtractAsync(new(null, pdf), cancellation.Token)
            : await scope.ServiceProvider.GetRequiredService<IAiListExtractionService>().ExtractAsync(new(null, pdf), cancellation.Token);
        var failures = Review.Check(result, catalog, expectedRules);
        var profileJson = JsonSerializer.SerializeToElement(result.Profile, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        if (fullProfile) failures = [.. failures, .. Review.CheckScalars(result.Profile, expectedRules)];
        else if (profileJson.EnumerateObject().Any(field => field.Name != "skill_ids" && field.Value.ValueKind != JsonValueKind.Null)) failures = [.. failures, "A scalar profile field was extracted during list-only phase."];
        if (result.Diagnostics?.StopReason != "tool_use") failures = [.. failures, "Provider did not finish the expected tool result."];
        if (fullProfile && (result.Diagnostics?.Stages?.Count != 2 || result.Diagnostics.Stages.Values.Any(stage => stage.StopReason != "tool_use")))
            failures = [.. failures, "Both extraction stages must finish their tools."];
        if (failures.Count == 0) passed++;
        var report = new
        {
            run, mode = fullProfile ? "full_profile" : "lists", passed = failures.Count == 0, skill_count = result.Profile.SkillIds.Count,
            scalar_fields_checked = fullProfile ? expectedRules.Profile!.Keys.ToArray() : [],
            scalar_review_passed = fullProfile ? (bool?)(Review.CheckScalars(result.Profile, expectedRules).Count == 0) : null,
            skill_names = result.Profile.SkillIds.Select(id => catalog[id]).ToArray(),
            experience_count = result.Experiences.Count, certificate_count = result.Certificates.Count,
            experience_review = result.Experiences.Select(item => new { item.RoleTitle, item.StartDate, item.EndDate, item.IsOngoing,
                has_project_name = item.ProjectName is not null, skill_names = item.SkillIds.Select(id => catalog[id]).ToArray() }).ToArray(),
            warnings = result.Warnings.Select(warning => new { warning.Field, warning.Code }).ToArray(),
            diagnostics = result.Diagnostics, failures
        };
        reports.Add(report);
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    catch (AiException error)
    {
        var report = new { run, passed = false, error_code = error.ErrorCode, validation_reason = error.ValidationReason, diagnostics = error.Diagnostics };
        reports.Add(report);
        Console.WriteLine(JsonSerializer.Serialize(report));
        // Stop immediately on provider/validation errors rather than silently consuming more live calls.
        break;
    }
}
if (reportDestination is not null)
    await File.WriteAllTextAsync(reportDestination, JsonSerializer.Serialize(new { requested_runs = runs, passed_runs = passed, reports }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Verified {passed}/{runs} live PDF runs. No profile/database writes performed.");
return passed == runs ? 0 : 1;
