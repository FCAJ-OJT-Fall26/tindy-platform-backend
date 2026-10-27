using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authorization;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Integration;

// Real routing, JWT validation, account authorization, form parsing, rate limiting and
// exception handling; only the application service is replaced. No database or AWS calls.
public sealed class AiProfileHttpTests
{
    private const string Endpoint = "/api/ai/extract-profile";

    [Theory]
    [InlineData(OnboardingAccessStatus.Incomplete)]
    [InlineData(OnboardingAccessStatus.Completed)]
    public async Task TextOnly_ReturnsSnakeCaseDraft_ForActiveAccounts(OnboardingAccessStatus state)
    {
        using var app = new Fixture();
        app.Base.Accounts.Status = state;
        using var client = app.Client();
        using var response = await client.PostAsync(Endpoint, Form("  Tôi viết API bằng C#.  "));
        Assert.True(response.StatusCode == HttpStatusCode.OK, string.Join("\n", app.Base.Logs.Entries));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Draft User", body.RootElement.GetProperty("data").GetProperty("profile")
            .GetProperty("display_name").GetString());
        Assert.False(body.RootElement.GetProperty("data").GetProperty("profile").TryGetProperty("bio", out _));
        Assert.Equal("Tôi viết API bằng C#.", app.Service.LastInput!.Text);
        Assert.Null(app.Service.LastInput.Pdf);
        Assert.Equal(1, app.Service.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("README: I implemented the API")]
    public async Task PdfAndOptionalText_ReachService(string? text)
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var form = Form(text);
        var bytes = "%PDF-1.7\nfixture"u8.ToArray();
        AddPdf(form, bytes);
        using var response = await client.PostAsync(Endpoint, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes, app.Service.LastInput!.Pdf);
        Assert.Equal(text, app.Service.LastInput.Text);
    }

    [Fact]
    public async Task MissingOrInvalidJwt_Returns401_BeforeExtraction()
    {
        using var app = new Fixture();
        using var client = app.Client(authenticated: false);
        for (var i = 0; i < 7; i++)
        {
            using var response = await client.PostAsync(Endpoint, Form("source"));
            await AssertProblem(response, HttpStatusCode.Unauthorized, "AUTH_UNAUTHORIZED");
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        using var invalid = await client.PostAsync(Endpoint, Form("source"));
        await AssertProblem(invalid, HttpStatusCode.Unauthorized, "AUTH_UNAUTHORIZED");
        Assert.Equal(0, app.Service.Calls);
    }

    [Fact]
    public async Task InactiveAccount_Returns403_BeforeExtraction()
    {
        using var app = new Fixture();
        app.Base.Accounts.Status = OnboardingAccessStatus.InactiveOrMissing;
        using var client = app.Client();
        using var response = await client.PostAsync(Endpoint, Form("source"));
        await AssertProblem(response, HttpStatusCode.Forbidden, "AUTH_FORBIDDEN");
        Assert.Equal(0, app.Service.Calls);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("unknown")]
    [InlineData("duplicate_text")]
    [InlineData("duplicate_file")]
    [InlineData("wrong_file_field")]
    [InlineData("file_as_text")]
    [InlineData("text_as_file")]
    [InlineData("empty_pdf")]
    [InlineData("long_text")]
    public async Task InvalidForms_Return400_WithoutCallingService(string kind)
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var form = Form(kind == "blank" ? " \t\n" : null);
        switch (kind)
        {
            case "unknown": form.Add(new StringContent("source"), "prompt"); break;
            case "duplicate_text": form.Add(new StringContent("one"), "text"); form.Add(new StringContent("two"), "text"); break;
            case "duplicate_file": AddPdf(form); AddPdf(form); break;
            case "wrong_file_field": AddPdf(form, name: "upload"); break;
            case "file_as_text": form.Add(new StringContent("source"), "file"); break;
            case "text_as_file": AddPdf(form, name: "text"); break;
            case "empty_pdf": AddPdf(form, []); break;
            case "long_text": form.Add(new StringContent(new string('x', AiProfileLimits.MaxTextLength + 1)), "text"); break;
        }
        using var response = await client.PostAsync(Endpoint, form);
        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.Equal(0, app.Service.Calls);
    }

    [Fact]
    public async Task MalformedMultipart_Returns400()
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var content = new StringContent("broken", Encoding.UTF8, "multipart/form-data");
        using var response = await client.PostAsync(Endpoint, content);
        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.Equal(0, app.Service.Calls);
    }

    [Fact]
    public async Task JsonInput_Returns415()
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var response = await client.PostAsync(Endpoint, new StringContent("{}", Encoding.UTF8, "application/json"));
        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "AI_UNSUPPORTED_MEDIA");
        Assert.Equal(0, app.Service.Calls);
    }

    [Fact]
    public async Task UnsupportedFileMime_Returns415()
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var form = Form();
        form.Add(new StringContent("%PDF-pretend"), "file", "cv.pdf");
        using var response = await client.PostAsync(Endpoint, form);
        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "AI_UNSUPPORTED_MEDIA");
        Assert.Equal(0, app.Service.Calls);
    }

    [Fact]
    public async Task SpoofedPdf_Returns422()
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var form = Form();
        AddPdf(form, "not a PDF"u8.ToArray());
        using var response = await client.PostAsync(Endpoint, form);
        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "AI_DOCUMENT_REJECTED");
        Assert.Equal(0, app.Service.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBody_Returns413_WithOrWithoutContentLength(bool chunked)
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var form = Form();
        AddPdf(form, new byte[AiProfileLimits.MaxRequestBytes + 1]);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        if (chunked) request.Headers.TransferEncodingChunked = true;
        using var response = await client.SendAsync(request);
        await AssertProblem(response, HttpStatusCode.RequestEntityTooLarge, "AI_PAYLOAD_TOO_LARGE");
        Assert.Equal(0, app.Service.Calls);
    }

    [Fact]
    public async Task OversizedPdfWithinBodyLimit_Returns413()
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var form = Form();
        AddPdf(form, new byte[AiProfileLimits.MaxPdfBytes + 1]);
        using var response = await client.PostAsync(Endpoint, form);
        await AssertProblem(response, HttpStatusCode.RequestEntityTooLarge, "AI_PAYLOAD_TOO_LARGE");
        Assert.Equal(0, app.Service.Calls);
    }

    [Fact]
    public async Task ExactPdfAndTextLimits_AreAccepted()
    {
        using var app = new Fixture();
        using var client = app.Client();
        using var form = Form(new string('x', AiProfileLimits.MaxTextLength));
        var pdf = new byte[AiProfileLimits.MaxPdfBytes];
        "%PDF-1.7"u8.CopyTo(pdf);
        AddPdf(form, pdf);
        using var response = await client.PostAsync(Endpoint, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AiProfileLimits.MaxTextLength, app.Service.LastInput!.Text!.Length);
        Assert.Equal(AiProfileLimits.MaxPdfBytes, app.Service.LastInput.Pdf!.Length);
    }

    [Theory]
    [InlineData(AiFailure.Unavailable, HttpStatusCode.ServiceUnavailable, "AI_UNAVAILABLE")]
    [InlineData(AiFailure.Busy, HttpStatusCode.TooManyRequests, "AI_BUSY")]
    [InlineData(AiFailure.Provider, HttpStatusCode.BadGateway, "AI_PROVIDER_ERROR")]
    [InlineData(AiFailure.InvalidOutput, HttpStatusCode.BadGateway, "AI_INVALID_OUTPUT")]
    [InlineData(AiFailure.Timeout, HttpStatusCode.GatewayTimeout, "AI_TIMEOUT")]
    [InlineData(AiFailure.DocumentRejected, HttpStatusCode.UnprocessableEntity, "AI_DOCUMENT_REJECTED")]
    public async Task ServiceFailures_ReturnSafeProblemDetails(AiFailure failure, HttpStatusCode status, string code)
    {
        using var app = new Fixture();
        app.Service.Failure = new AiException(failure);
        using var client = app.Client();
        using var response = await client.PostAsync(Endpoint, Form("private-source-canary"));
        await AssertProblem(response, status, code);
        Assert.DoesNotContain("private-source-canary", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(app.Base.Logs.Entries, entry => entry.Contains("private-source-canary"));
    }

    [Fact]
    public async Task Quota_IsFivePerAuthenticatedUser_AndDoesNotBlockAnotherUser()
    {
        using var app = new Fixture();
        using var client = app.Client();
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsync(Endpoint, Form("source"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var rejected = await client.PostAsync(Endpoint, Form("source"));
        await AssertProblem(rejected, HttpStatusCode.TooManyRequests, "AI_RATE_LIMITED");
        Assert.Equal(5, app.Service.Calls);
        using var other = app.Client(userId: Guid.NewGuid());
        using var accepted = await other.PostAsync(Endpoint, Form("source"));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(6, app.Service.Calls);
    }

    [Fact]
    public async Task RealApplicationService_ValidatesCombinedInputAndCatalogBoundOutput()
    {
        using var app = new Fixture(realService: true);
        using var client = app.Client();
        using var form = Form("  Tôi tên Lan và đã xây API bằng C#.  ");
        AddPdf(form);
        using var response = await client.PostAsync(Endpoint, form);
        await AssertSuccessfulDraft(response, app);
        Assert.Equal("Tôi tên Lan và đã xây API bằng C#.", app.Provider.LastInput!.Text);
        Assert.NotNull(app.Provider.LastInput.Pdf);
        Assert.Equal("s1", Assert.Single(app.Provider.Skills!).Key);
        Assert.Equal(new[] { "skills", "roles" }, app.Catalog.Reads);
        Assert.Equal(2, app.Provider.Calls);
        Assert.True(response.Headers.Contains("X-AI-Diagnostics"));
        using var diagnostics = JsonDocument.Parse(response.Headers.GetValues("X-AI-Diagnostics").Single());
        Assert.Equal(8192, diagnostics.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(2, diagnostics.RootElement.GetProperty("stages").EnumerateObject().Count());
    }

    [Fact]
    public async Task RealApplicationService_RejectsInvalidProviderOutput_OverHttp()
    {
        using var app = new Fixture(realService: true);
        app.Provider.InvalidOutput = true;
        using var client = app.Client();
        using var response = await client.PostAsync(Endpoint, Form("source"));
        await AssertProblem(response, HttpStatusCode.BadGateway, "AI_INVALID_OUTPUT");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("{invalid}", json.RootElement.GetProperty("raw_model_output").GetString());
        Assert.Equal("lists", json.RootElement.GetProperty("ai_diagnostics").GetProperty("stage").GetString());
        Assert.DoesNotContain(app.Base.Logs.Entries, entry => entry.Contains("{invalid}"));
    }

    [Fact]
    public async Task DisabledRealBedrockProvider_Returns503_WithoutNetworkOrDatabase()
    {
        using var app = new Fixture(realService: true, realDisabledProvider: true);
        using var client = app.Client();
        using var response = await client.PostAsync(Endpoint, Form("source"));
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "AI_UNAVAILABLE");
        Assert.Null(app.Provider.LastInput);
    }

    private static async Task AssertSuccessfulDraft(HttpResponseMessage response, Fixture app)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, string.Join("\n", app.Base.Logs.Entries));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var profile = body.RootElement.GetProperty("data").GetProperty("profile");
        Assert.Equal("Lan", profile.GetProperty("display_name").GetString());
        Assert.Equal(app.Catalog.SkillId, profile.GetProperty("skill_ids")[0].GetGuid());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("preferred_role_id").ValueKind);
        Assert.Equal(4, body.RootElement.GetProperty("data").EnumerateObject().Count());
        var experience = body.RootElement.GetProperty("data").GetProperty("experiences")[0];
        Assert.Equal("Backend Developer", experience.GetProperty("role_title").GetString());
        Assert.Contains("Lập trình viên backend", experience.GetProperty("description").GetString());
        Assert.False(experience.TryGetProperty("role_key", out _));
        Assert.False(experience.TryGetProperty("role_title_source", out _));
        foreach (var field in profile.EnumerateObject().Where(field => field.Name is not "skill_ids" and not "display_name"))
            Assert.Equal(JsonValueKind.Null, field.Value.ValueKind);
    }

    private static MultipartFormDataContent Form(string? text = null)
    {
        var form = new MultipartFormDataContent();
        if (text is not null) form.Add(new StringContent(text), "text");
        return form;
    }

    private static void AddPdf(MultipartFormDataContent form, byte[]? bytes = null, string name = "file")
    {
        var content = new ByteArrayContent(bytes ?? "%PDF-1.7\nfixture"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, name, "cv.pdf");
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error_code").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed class Fixture : IDisposable
    {
        public OAuthHttpApiFactory Base { get; } = new(oauthEnabled: false);
        public ExtractionService Service { get; } = new();
        public ExtractionProvider Provider { get; } = new();
        public CatalogRepository Catalog { get; } = new();
        private readonly WebApplicationFactory<Program> _app;
        public Fixture(bool realService = false, bool realDisabledProvider = false) =>
            _app = Base.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (!realService)
            {
                services.RemoveAll<IAiProfileService>();
                services.AddSingleton<IAiProfileService>(Service);
            }
            else
            {
                services.RemoveAll<ICatalogRepository>();
                services.AddSingleton<ICatalogRepository>(Catalog);
                if (realDisabledProvider)
                    services.PostConfigure<BedrockOptions>(options => options.Enabled = false);
                else
                {
                    services.RemoveAll<IAiExtractionBatchProvider>();
                    services.AddSingleton<IAiExtractionBatchProvider>(Provider);
                }
            }
        }));

        public HttpClient Client(bool authenticated = true, Guid? userId = null)
        {
            var client = _app.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            if (authenticated)
            {
                using var scope = _app.Services.CreateScope();
                var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
                    .GenerateAccessToken(userId ?? Base.UserId, ["Member"]).AccessToken;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            return client;
        }

        public void Dispose() { _app.Dispose(); Base.Dispose(); }
    }

    private sealed class ExtractionService : IAiProfileService
    {
        public int Calls { get; private set; }
        public AiProfileInput? LastInput { get; private set; }
        public Exception? Failure { get; set; }
        public Task<ProfileDraftResponse> ExtractAsync(AiProfileInput input, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastInput = input;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new ProfileDraftResponse { Profile = new ProfileDraft { DisplayName = "Draft User" } });
        }
    }

    private sealed class ExtractionProvider : IAiExtractionBatchProvider, IAiExtractionBatchSession
    {
        public IAiExtractionBatchSession BeginBatch(CancellationToken cancellationToken = default) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<AiScalarProfileModelOutput> ExtractProfileScalarsAsync(AiProfileInput input, IReadOnlyList<AiRoleReference> roles)
        {
            Calls++;
            return Task.FromResult(new AiScalarProfileModelOutput("""{"profile":{"display_name":"Lan"},"warnings":[]}""", new("tool_use", 80, 30, 4096, 1)));
        }
        public AiProfileInput? LastInput { get; private set; }
        public IReadOnlyList<AiSkillReference>? Skills { get; private set; }
        public bool InvalidOutput { get; set; }
        public int Calls { get; private set; }
        public Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input, IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles)
        {
            Calls++;
            LastInput = input;
            Skills = skills;
            var output = InvalidOutput ? "{invalid}" : JsonSerializer.Serialize(new
            {
                skill_ids = new Dictionary<string, string> { [skills.Single().Key] = skills.Single().Name },
                experiences = new[] { new { role_key = roles.Single().Key, role_title_source = "Lập trình viên backend", description = "Built APIs", skill_ids = new Dictionary<string, string> { [skills.Single().Key] = skills.Single().Name } } },
                certificates = new[] { new { title = "Certificate" } },
                warnings = Array.Empty<object>()
            });
            return Task.FromResult(new AiListModelOutput(output, new("tool_use", 100, 50, 4096, 1)));
        }
    }

    private sealed class CatalogRepository : ICatalogRepository
    {
        public Guid SkillId { get; } = Guid.NewGuid();
        public Guid RoleId { get; } = Guid.NewGuid();
        public List<string> Reads { get; } = [];
        public Task<IReadOnlyList<SkillCategory>> GetActiveSkillCategoriesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Extraction does not need skill categories.");
        public Task<IReadOnlyList<Skill>> GetActiveSkillsAsync(Guid? categoryId, string? normalizedSearch,
            CancellationToken cancellationToken = default)
        {
            Reads.Add("skills");
            return Task.FromResult<IReadOnlyList<Skill>>([new Skill { SkillId = SkillId, Name = "C#", NameNormalized = "C#" }]);
        }
        public Task<IReadOnlyList<ProfessionalRole>> GetActiveProfessionalRolesAsync(string? normalizedSearch,
            CancellationToken cancellationToken = default)
        {
            Reads.Add("roles");
            return Task.FromResult<IReadOnlyList<ProfessionalRole>>([new ProfessionalRole
            { ProfessionalRoleId = RoleId, Name = "Backend Developer", NameNormalized = "BACKEND DEVELOPER" }]);
        }
    }
}
