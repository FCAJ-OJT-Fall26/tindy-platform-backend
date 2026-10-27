using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiProfileServiceTests
{
    private static readonly Guid SkillId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string EmptyDraft = """{"profile":{},"experiences":[],"certificates":[],"warnings":[]}""";

    [Theory]
    [InlineData("I use C#.", false)]
    [InlineData(null, true)]
    [InlineData("# My personal README", true)]
    public async Task AcceptsTextPdfAndCombinedSourcesWithoutWriting(string? text, bool pdf)
    {
        var provider = new FakeProvider();
        var catalog = new FakeCatalog();
        var input = new AiProfileInput(text, pdf ? "%PDF-1.7 test"u8.ToArray() : null);
        var result = await new AiProfileService(new AiListExtractionService(catalog, provider)).ExtractAsync(input);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(input, provider.Input);
        Assert.Equal("s1", Assert.Single(provider.Skills!).Key);
        Assert.Equal("C#", Assert.Single(provider.Skills!).Name);
        Assert.Equal(1, catalog.Reads);
        Assert.Contains(result.Warnings, x => x.Code == "INSUFFICIENT_EVIDENCE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task EmptyInputIsRejectedBeforeCatalogOrProvider(string? text)
    {
        var catalog = new FakeCatalog();
        var provider = new FakeProvider();
        await Assert.ThrowsAsync<ProfileValidationException>(() =>
            new AiProfileService(new AiListExtractionService(catalog, provider)).ExtractAsync(new(text, null)));
        Assert.Equal(0, catalog.Reads);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task TextLimitAppliesBeforeTrimming()
    {
        var provider = new FakeProvider();
        await Assert.ThrowsAsync<ProfileValidationException>(() =>
            new AiProfileService(new AiListExtractionService(new FakeCatalog(), provider)).ExtractAsync(new(new string(' ', 20_001), null)));
        Assert.Equal(0, provider.Calls);
        await new AiProfileService(new AiListExtractionService(new FakeCatalog(), provider)).ExtractAsync(new(new string('a', 20_000), null));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task TrimsSourceTextAndPropagatesCancellationToken()
    {
        var provider = new FakeProvider();
        using var cancellation = new CancellationTokenSource();
        await new AiProfileService(new AiListExtractionService(new FakeCatalog(), provider)).ExtractAsync(new("  My README\n", null), cancellation.Token);
        Assert.Equal("My README", provider.Input!.Text);
        Assert.Equal(cancellation.Token, provider.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("%PDX-1.7")]
    public async Task RejectsInvalidPdfMagicEvenWithValidText(string pdf)
    {
        var provider = new FakeProvider();
        var error = await Assert.ThrowsAsync<AiException>(() => new AiProfileService(new AiListExtractionService(new FakeCatalog(), provider))
            .ExtractAsync(new("I know C#", Encoding.UTF8.GetBytes(pdf))));
        Assert.Equal(AiFailure.DocumentRejected, error.Failure);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task PdfSizeBoundaryIsEnforced()
    {
        var pdf = new byte[AiProfileLimits.MaxPdfBytes];
        "%PDF-"u8.CopyTo(pdf);
        var provider = new FakeProvider();
        var service = new AiProfileService(new AiListExtractionService(new FakeCatalog(), provider));
        await service.ExtractAsync(new(null, pdf));
        Assert.Equal(1, provider.Calls);
        var error = await Assert.ThrowsAsync<AiException>(() => service.ExtractAsync(new(null, new byte[pdf.Length + 1])));
        Assert.Equal(AiFailure.PayloadTooLarge, error.Failure);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task OversizedProviderOutputIsRejected()
    {
        var provider = new FakeProvider { Output = new string('a', AiProfileLimits.MaxOutputBytes + 1) };
        var error = await Assert.ThrowsAsync<AiException>(() =>
            new AiProfileService(new AiListExtractionService(new FakeCatalog(), provider)).ExtractAsync(new("A", null)));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.True(error.RawOutputTruncated);
        Assert.Equal(AiProfileLimits.MaxOutputBytes, Encoding.UTF8.GetByteCount(error.RawOutput!));
    }

    [Theory]
    [InlineData("{invalid}")]
    [InlineData("```json\n{}\n```")]
    [InlineData("{\"profile\":{\"skill_ids\":[\"ffffffff-ffff-ffff-ffff-ffffffffffff\"]},\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    public async Task InvalidDraftRetainsExactModelTextForDevelopmentDiagnostics(string output)
    {
        var provider = new FakeProvider { Output = output };
        var error = await Assert.ThrowsAsync<AiException>(() =>
            new AiProfileService(new AiListExtractionService(new FakeCatalog(), provider)).ExtractAsync(new("A", null)));
        Assert.Equal(output, error.RawOutput);
        Assert.False(error.RawOutputTruncated);
        Assert.DoesNotContain(output, error.ToString());
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void AcceptsNullableSuggestionsAndExactSupportedFields()
    {
        var json = $$"""
        {"profile":{"display_name":"An","skill_ids":["{{SkillId}}"],"preferred_role_id":"{{RoleId}}",
        "public_email":"an@example.com","github_url":"https://github.com/an","available_hours_per_week":40},
        "experiences":[{"project_name":"Tindy","start_date":"2025-01","end_date":"2025-02","skill_ids":["{{SkillId}}"]}],
        "certificates":[{"title":"Certificate","issued_at":"2024-02-29","expires_at":"2025-03-01"}],"warnings":[]}
        """;
        var draft = Parse(json);
        Assert.Equal("An", draft.Profile.DisplayName);
        Assert.Null(draft.Experiences[0].RoleTitle);
        Assert.Equal("2024-02-29", draft.Certificates[0].IssuedAt);
        Assert.Empty(draft.Warnings);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("```json\n{}\n```")]
    [InlineData("{\"profile\":{},\"experiences\":[],\"certificates\":[],\"warnings\":[],\"bio\":\"invented\"}")]
    [InlineData("{\"profile\":{},\"profile\":{},\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"display_name\":\"A\",\"display_name\":\"B\"},\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    public void RejectsMalformedMissingUnknownAndDuplicateJson(string json) => AssertInvalid(json);

    public static IEnumerable<object[]> InvalidDrafts()
    {
        foreach (var group in new[] { "profile", "experiences", "certificates", "warnings" })
        {
            var draft = JsonNode.Parse(EmptyDraft)!;
            draft[group] = null;
            yield return [draft.ToJsonString()];
        }
        foreach (var (field, value) in new (string, object)[]
        {
            ("display_name", new string('a', 101)), ("headline", new string('a', 256)),
            ("display_name", "  "), ("display_name", "a\u0000b"),
            ("available_hours_per_week", 0), ("available_hours_per_week", 41),
            ("available_hours_per_week", "20"), ("preferred_role_id", Guid.NewGuid().ToString()),
            ("github_url", "javascript:alert(1)"), ("personal_website_url", "https://name:secret@example.com"),
            ("public_email", "Name <a@example.com>"), ("public_email", "invalid"),
            ("bio", "Not supported"), ("resume_url", "https://example.com/cv"), ("user_id", Guid.NewGuid()),
            ("professional_interests", new string('a', 5001)), ("DisplayName", "Wrong case")
        })
        {
            var draft = JsonNode.Parse(EmptyDraft)!;
            draft["profile"]![field] = JsonSerializer.SerializeToNode(value);
            yield return [draft.ToJsonString()];
        }
        foreach (var ids in new JsonNode?[] { null, JsonNode.Parse("[null]"),
            JsonSerializer.SerializeToNode(new[] { SkillId, SkillId }), JsonSerializer.SerializeToNode(new[] { Guid.NewGuid() }) })
        {
            var draft = JsonNode.Parse(EmptyDraft)!;
            draft["profile"]!["skill_ids"] = ids;
            yield return [draft.ToJsonString()];
        }
        foreach (var experience in new[] { "null", "{}", "{\"start_date\":\"2025\"}", "{\"start_date\":\"2025-13\"}",
            "{\"start_date\":\"2025-02\",\"end_date\":\"2025-01\"}",
            "{\"end_date\":\"2025-01\",\"is_ongoing\":true}", "{\"skill_ids\":null}" })
        {
            var draft = JsonNode.Parse(EmptyDraft)!;
            draft["experiences"] = JsonNode.Parse("[" + experience + "]");
            yield return [draft.ToJsonString()];
        }
        foreach (var certificate in new[] { "null", "{}", "{\"issued_at\":\"2025-02-29\"}",
            "{\"issued_at\":\"2025-01\"}", "{\"issued_at\":\"2025-01-01\",\"expires_at\":\"2024-01-01\"}" })
        {
            var draft = JsonNode.Parse(EmptyDraft)!;
            draft["certificates"] = JsonNode.Parse("[" + certificate + "]");
            yield return [draft.ToJsonString()];
        }
        foreach (var warning in new[] { "null", "{}", "{\"field\":\"profile\",\"code\":\"\",\"message\":\"bad\"}",
            "{\"field\":\"profile\",\"code\":\"UNKNOWN\",\"message\":\"bad\"}",
            "{\"field\":\"profile.bio\",\"code\":\"CONFLICT\",\"message\":\"bad\"}",
            "{\"field\":\"experiences[0].project_name\",\"code\":\"CONFLICT\",\"message\":\"bad\"}" })
        {
            var draft = JsonNode.Parse(EmptyDraft)!;
            draft["warnings"] = JsonNode.Parse("[" + warning + "]");
            yield return [draft.ToJsonString()];
        }
        foreach (var collection in new[] { "experiences", "certificates", "warnings" })
        {
            var draft = JsonNode.Parse(EmptyDraft)!;
            draft[collection] = new JsonArray(Enumerable.Range(0, collection == "warnings" ? 51 : 21)
                .Select(_ => (JsonNode)new JsonObject()).ToArray());
            yield return [draft.ToJsonString()];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidDrafts))]
    public void RejectsInvalidStructuredValues(string json) => AssertInvalid(json);

    [Fact]
    public void RejectsConflictWarningPairedWithPopulatedScalar()
    {
        AssertInvalid("""{"profile":{"display_name":"An"},"experiences":[],"certificates":[],"warnings":[{"field":"profile.display_name","code":"CONFLICT","message":"Sources disagree."}]}""");
    }

    [Fact]
    public void AcceptsConflictOnNullFieldAndWarningForOmittedArrayItems()
    {
        var draft = Parse("""{"profile":{},"experiences":[{"project_name":"Supported project"}],"certificates":[],"warnings":[{"field":"profile.display_name","code":"CONFLICT","message":"Sources disagree."},{"field":"experiences","code":"CONFLICT","message":"One disputed project was omitted."},{"field":"experiences[0].start_date","code":"INCOMPLETE_DATE","message":"Month is unknown."}]}""");
        Assert.Null(draft.Profile.DisplayName);
        Assert.Equal(3, draft.Warnings.Count);
    }

    private static ProfileDraftResponse Parse(string json) => ProfileDraftValidator.Parse(json, new HashSet<Guid> { SkillId }, new HashSet<Guid> { RoleId });
    private static void AssertInvalid(string json) => Assert.Equal(AiFailure.InvalidOutput, Assert.Throws<AiException>(() => Parse(json)).Failure);

    private sealed class FakeProvider : IAiListExtractionProvider
    {
        public string Output { get; init; } = """{"skill_ids":[],"experiences":[],"certificates":[],"warnings":[]}""";
        public int Calls { get; private set; }
        public AiProfileInput? Input { get; private set; }
        public IReadOnlyList<AiSkillReference>? Skills { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input, IReadOnlyList<AiSkillReference> skills,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Input = input;
            Skills = skills;
            Token = cancellationToken;
            return Task.FromResult(new AiListModelOutput(Output, new("tool_use", 100, 50, 4096, 1)));
        }
    }

    private sealed class FakeCatalog : ICatalogRepository
    {
        public int Reads { get; private set; }
        public Task<IReadOnlyList<SkillCategory>> GetActiveSkillCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Skill>> GetActiveSkillsAsync(Guid? categoryId, string? normalizedSearch, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<Skill>>([new() { SkillId = SkillId, Name = "C#" }, new() { SkillId = Guid.NewGuid(), Name = "inactive", IsActive = false }]);
        }
        public Task<IReadOnlyList<ProfessionalRole>> GetActiveProfessionalRolesAsync(string? normalizedSearch, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<ProfessionalRole>>([new() { ProfessionalRoleId = RoleId, Name = "Backend Developer" }, new() { ProfessionalRoleId = Guid.NewGuid(), Name = "inactive", IsActive = false }]);
        }
    }
}
