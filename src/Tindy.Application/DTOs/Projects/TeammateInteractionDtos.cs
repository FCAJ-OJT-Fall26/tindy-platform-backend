using System.Text.Json;

namespace Tindy.Application.DTOs.Projects;

public sealed record SetTeammateInteractionRequest(string Action)
{
    public static SetTeammateInteractionRequest Parse(JsonElement json)
    {
        var properties = RecruitmentRequestParser.Object(json, "action");
        if (!properties.TryGetValue("action", out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not ("saved" or "shortlisted" or "skipped"))
            throw RecruitmentRequestParser.Invalid("action", "Action must be saved, shortlisted or skipped.");
        return new(value.GetString()!);
    }
}
public sealed record TeammateInteractionDto(Guid ProjectId, Guid PositionId, Guid CandidateUserId, string Action, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record TeammateInteractionItem(Guid ProjectId, Guid PositionId, Guid CandidateUserId, string Action, DateTime CreatedAt, DateTime UpdatedAt,
    TeammateCandidatePreview? Candidate, bool IsDiscoverable);
public sealed record TeammateInteractionsPage(IReadOnlyList<TeammateInteractionItem> Items, PaginationMeta Pagination);
public sealed record ResetTeammateInteractionDto(Guid ProjectId, Guid PositionId, Guid CandidateUserId, string? Action = null);
