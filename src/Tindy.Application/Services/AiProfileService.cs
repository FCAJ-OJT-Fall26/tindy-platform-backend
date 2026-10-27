using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class AiProfileService(ICatalogRepository catalog, IAiExtractionBatchProvider provider) : IAiProfileService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<ProfileDraftResponse> ExtractAsync(AiProfileInput input, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(120));
        try { return await ExtractCoreAsync(AiExtractionInput.Normalize(input), timer, budget.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            throw new AiException(AiFailure.Timeout)
            { Diagnostics = new(null, null, null, 8192, timer.ElapsedMilliseconds, "request_deadline", "request") };
        }
    }

    private async Task<ProfileDraftResponse> ExtractCoreAsync(AiProfileInput input, Stopwatch timer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // The repository shares a scoped DbContext: finish both reads before starting either model call.
        var skills = (await catalog.GetActiveSkillsAsync(null, null, token)).Where(x => x.IsActive)
            .Select(x => new AiCatalogItem(x.SkillId, x.Name)).ToArray();
        var roles = (await catalog.GetActiveProfessionalRolesAsync(null, token)).Where(x => x.IsActive)
            .Select(x => new AiCatalogItem(x.ProfessionalRoleId, x.Name)).ToArray();
        var skillMap = new AiSkillReferenceMap(skills);
        var roleMap = new AiRoleReferenceMap(roles);
        using var siblings = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var batch = provider.BeginBatch(siblings.Token);
        Exception? firstFailure = null;

        async Task<ProfileDraftResponse> Stage(string name, Func<Task<(string Raw, AiCallDiagnostics Diagnostics)>> call,
            Func<string, ProfileDraftResponse> parse)
        {
            AiCallDiagnostics? diagnostics = null;
            try
            {
                var output = await call();
                diagnostics = output.Diagnostics with { Stage = name };
                return parse(output.Raw) with { Diagnostics = diagnostics };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                if (error is AiException ai)
                    ai.Diagnostics = (ai.Diagnostics ?? diagnostics ?? new(null, null, null, 4096, timer.ElapsedMilliseconds)) with
                    { Stage = name, FailureReason = ai.ValidationReason ?? ai.Diagnostics?.FailureReason ?? "stage_failed" };
                Interlocked.CompareExchange(ref firstFailure, error, null);
                await siblings.CancelAsync();
                throw;
            }
        }

        var profileTask = Stage("profile", async () =>
        {
            var output = await batch.ExtractProfileScalarsAsync(input, roleMap.References);
            return (output.RawJson, output.Diagnostics);
        }, json => AiScalarProfileDraftValidator.Parse(json, roleMap));
        var listsTask = Stage("lists", async () =>
        {
            var output = await batch.ExtractListsAsync(input, skillMap.References, roleMap.References);
            return (output.RawJson, output.Diagnostics);
        }, json => AiListDraftValidator.Parse(json, skillMap, roleMap));

        try { await Task.WhenAll(profileTask, listsTask); }
        catch
        {
            // Both tasks have settled. A sibling cancellation must never mask the original failure.
            if (firstFailure is not null) ExceptionDispatchInfo.Capture(firstFailure).Throw();
            throw;
        }
        token.ThrowIfCancellationRequested();
        var scalar = await profileTask;
        var lists = await listsTask;
        var warnings = scalar.Warnings.Concat(lists.Warnings.Select(warning =>
            warning.Field == "profile" && warning.Code == "INSUFFICIENT_EVIDENCE"
                ? new ProfileDraftWarning("$", warning.Code, "The source does not contain enough evidence for skills, experiences or certificates.")
                : warning)).Distinct().ToArray();
        if (warnings.Length > AiProfileLimits.MaxWarnings)
            warnings = warnings.Take(AiProfileLimits.MaxWarnings - 1).Append(new ProfileDraftWarning("$", "LIMIT_EXCEEDED",
                "Additional extraction warnings were omitted.")).ToArray();
        var merged = new ProfileDraftResponse
        {
            Profile = scalar.Profile with { SkillIds = lists.Profile.SkillIds },
            Experiences = lists.Experiences, Certificates = lists.Certificates, Warnings = warnings
        };
        var json = JsonSerializer.Serialize(merged, Json);
        try
        {
            var validated = ProfileDraftValidator.Parse(json, skills.Select(x => x.Id).ToHashSet(), roleMap.ActiveIds);
            var p = scalar.Diagnostics!;
            var l = lists.Diagnostics!;
            return validated with { Diagnostics = new("tool_use", Sum(p.InputTokens, l.InputTokens), Sum(p.OutputTokens, l.OutputTokens),
                p.MaxOutputTokens + l.MaxOutputTokens, timer.ElapsedMilliseconds,
                Stages: new Dictionary<string, AiCallDiagnostics> { ["profile"] = p, ["lists"] = l }) };
        }
        catch (AiException error) when (error.Failure == AiFailure.InvalidOutput)
        {
            throw new AiException(AiFailure.InvalidOutput, json, "merged_draft")
            { ValidationReason = "invalid_merged_draft", Diagnostics = new(null, null, null, 8192, timer.ElapsedMilliseconds, "invalid_merged_draft", "merge") };
        }
    }

    private static int? Sum(int? left, int? right) => left.HasValue && right.HasValue ? left.Value + right.Value : null;
}
