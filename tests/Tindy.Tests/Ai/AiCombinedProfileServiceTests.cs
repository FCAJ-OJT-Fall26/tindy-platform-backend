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

public sealed class AiCombinedProfileServiceTests
{
    [Fact]
    public async Task ReadsCatalogSequentiallyBeforeStartingParallelStagesAndMergesOldPublicShape()
    {
        var catalog = new Catalog();
        var batch = new Batch(catalog);
        var draft = await new AiProfileService(catalog, batch).ExtractAsync(new("  SQL developer  ", null));
        Assert.Equal(new[] { "skills", "roles" }, catalog.Reads);
        Assert.Equal(2, batch.Calls);
        Assert.Equal("SQL developer", batch.Input!.Text);
        Assert.Equal("Lan", draft.Profile.DisplayName);
        Assert.Equal(catalog.Role, draft.Profile.PreferredRoleId);
        Assert.Equal(new[] { catalog.Skill }, draft.Profile.SkillIds);
        Assert.Single(draft.Experiences);
        Assert.Single(draft.Certificates);
        Assert.True(batch.Disposed);
        Assert.Equal(200, draft.Diagnostics!.InputTokens);
        Assert.Equal(8192, draft.Diagnostics.MaxOutputTokens);
        Assert.Equal(2, draft.Diagnostics.Stages!.Count);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(draft, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        Assert.Equal(new[] { "profile", "experiences", "certificates", "warnings" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.Equal(17, json.RootElement.GetProperty("profile").EnumerateObject().Count());
        Assert.DoesNotContain("r1", json.RootElement.ToString());
        Assert.DoesNotContain("s1", json.RootElement.ToString());
    }

    [Theory]
    [InlineData("profile", false)]
    [InlineData("lists", false)]
    [InlineData("profile", true)]
    [InlineData("lists", true)]
    public async Task StageFailureCancelsAndDrainsSiblingAndRetainsActualFailingOutput(string failing, bool providerFailure)
    {
        var catalog = new Catalog();
        var batch = new Batch(catalog) { Failing = failing, ProviderFailure = providerFailure };
        var error = await Assert.ThrowsAsync<AiException>(() => new AiProfileService(catalog, batch).ExtractAsync(new("source", null)));
        Assert.Equal(providerFailure ? AiFailure.Timeout : AiFailure.InvalidOutput, error.Failure);
        Assert.Equal(failing, error.Diagnostics!.Stage);
        if (providerFailure) Assert.Equal(8192, error.Diagnostics.MaxOutputTokens);
        if (!providerFailure) Assert.Equal("{invalid}", error.RawOutput);
        Assert.Equal(1, batch.SiblingCancelled);
        Assert.True(batch.Disposed);
    }

    [Fact]
    public async Task ClientCancellationIsNotConvertedToProviderFailure()
    {
        var catalog = new Catalog();
        var batch = new Batch(catalog) { WaitForCancellation = true };
        using var client = new CancellationTokenSource();
        var task = new AiProfileService(catalog, batch).ExtractAsync(new("source", null), client.Token);
        await batch.BothStarted.Task;
        client.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(batch.Disposed);
        Assert.Equal(2, batch.SiblingCancelled);
    }

    [Fact]
    public async Task WarningsAreDeduplicatedCappedAndEmptyListsWarningIsScoped()
    {
        var catalog = new Catalog();
        var batch = new Batch(catalog) { ManyWarnings = true };
        var draft = await new AiProfileService(catalog, batch).ExtractAsync(new("source", null));
        Assert.Equal(50, draft.Warnings.Count);
        Assert.Equal("LIMIT_EXCEEDED", draft.Warnings[^1].Code);
        Assert.Equal(50, draft.Warnings.Distinct().Count());
        Assert.Single(draft.Warnings, warning => warning.Message == "Shared warning");

        batch = new Batch(catalog) { EmptyLists = true };
        draft = await new AiProfileService(catalog, batch).ExtractAsync(new("source", null));
        Assert.Equal("Lan", draft.Profile.DisplayName);
        Assert.Contains(draft.Warnings, x => x.Field == "$" && x.Message.Contains("skills, experiences"));
        Assert.DoesNotContain(draft.Warnings, x => x.Field == "profile" && x.Message.Contains("enough personal"));
    }

    [Fact]
    public async Task InvalidInputOrPreCancelledRequestDoesNotReadCatalogOrReserveBatch()
    {
        var catalog = new Catalog();
        var batch = new Batch(catalog);
        var service = new AiProfileService(catalog, batch);
        await Assert.ThrowsAsync<ProfileValidationException>(() => service.ExtractAsync(new(null, null)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExtractAsync(new("source", null), cancellation.Token));
        Assert.Empty(catalog.Reads);
        Assert.Equal(0, batch.Calls);
    }

    private sealed class Batch(Catalog catalog) : IAiExtractionBatchProvider, IAiExtractionBatchSession
    {
        private CancellationToken _token;
        public string? Failing;
        public bool ProviderFailure, WaitForCancellation, ManyWarnings, EmptyLists, Disposed;
        public int Calls, SiblingCancelled;
        public AiProfileInput? Input;
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IAiExtractionBatchSession BeginBatch(CancellationToken cancellationToken = default)
        {
            Assert.Equal(new[] { "skills", "roles" }, catalog.Reads.TakeLast(2));
            _token = cancellationToken;
            return this;
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        private async Task<string> Run(string stage, AiProfileInput input, string json)
        {
            Input = input;
            if (++Calls == 2) BothStarted.TrySetResult();
            await BothStarted.Task;
            if (WaitForCancellation || (Failing is not null && Failing != stage))
            {
                try { await Task.Delay(Timeout.Infinite, _token); }
                catch (OperationCanceledException) { Interlocked.Increment(ref SiblingCancelled); throw; }
            }
            if (stage == Failing)
            {
                if (ProviderFailure) throw new AiException(AiFailure.Timeout);
                return "{invalid}";
            }
            return json;
        }
        public async Task<AiScalarProfileModelOutput> ExtractProfileScalarsAsync(AiProfileInput input, IReadOnlyList<AiRoleReference> roles)
        {
            var node = JsonNode.Parse("""{"profile":{"display_name":"Lan","preferred_role_key":"r1","preferred_role_statement":"I want to work as a SQL Developer."},"warnings":[]}""")!;
            if (ManyWarnings) node["warnings"] = Warnings("profile", 30);
            return new(await Run("profile", input, node.ToJsonString()), new("tool_use", 100, 50, 4096, 5));
        }
        public async Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input, IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles)
        {
            var node = JsonNode.Parse(EmptyLists
                ? """{"skill_ids":{},"experiences":[],"certificates":[],"warnings":[]}"""
                : """{"skill_ids":{"s1":"SQL"},"experiences":[{"role_key":"r1","role_title_source":"SQL Developer","skill_ids":{"s1":"SQL"}}],"certificates":[{"title":"SQL certificate"}],"warnings":[]}""")!;
            if (ManyWarnings) node["warnings"] = Warnings("skill_ids", 30);
            return new(await Run("lists", input, node.ToJsonString()), new("tool_use", 100, 50, 4096, 5));
        }
        private static JsonArray Warnings(string field, int count) => new(Enumerable.Range(0, count)
            .Select(i => (JsonNode)new JsonObject { ["field"] = field, ["code"] = "MISSING_INFORMATION", ["message"] = "Missing " + i })
            .Prepend(new JsonObject { ["field"] = "$", ["code"] = "MISSING_INFORMATION", ["message"] = "Shared warning" }).ToArray());
    }

    private sealed class Catalog : ICatalogRepository
    {
        public Guid Skill = Guid.NewGuid(), Role = Guid.NewGuid();
        public List<string> Reads = [];
        public Task<IReadOnlyList<SkillCategory>> GetActiveSkillCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<IReadOnlyList<Skill>> GetActiveSkillsAsync(Guid? categoryId, string? normalizedSearch, CancellationToken cancellationToken = default)
        {
            Reads.Add("skills");
            await Task.Yield();
            return [new() { SkillId = Skill, Name = "SQL" }];
        }
        public Task<IReadOnlyList<ProfessionalRole>> GetActiveProfessionalRolesAsync(string? normalizedSearch, CancellationToken cancellationToken = default)
        {
            Assert.Equal("skills", Reads[^1]);
            Reads.Add("roles");
            return Task.FromResult<IReadOnlyList<ProfessionalRole>>([new() { ProfessionalRoleId = Role, Name = "SQL Developer" }]);
        }
    }
}
