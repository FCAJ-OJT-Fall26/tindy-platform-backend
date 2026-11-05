using System.Text.Json;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Projects;

public sealed record CreateProjectInvitationRequest(Guid PositionId, Guid InvitedUserId, string? Message)
{
    public static CreateProjectInvitationRequest Parse(JsonElement json)
    {
        var properties = RecruitmentRequestParser.Object(json, "position_id", "invited_user_id", "message");
        var position = RecruitmentRequestParser.Id(properties, "position_id");
        var recipient = RecruitmentRequestParser.Id(properties, "invited_user_id");
        string? message = null;
        if (properties.TryGetValue("message", out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 2000)
                throw RecruitmentRequestParser.Invalid("message", "Message must be a string or null, at most 2000 characters before trimming.");
            message = value.GetString()!.Trim();
            if (message.Length == 0) message = null;
        }
        return new(position, recipient, message);
    }
}
public sealed record ProjectInvitationDto(Guid InvitationId, Guid ProjectId, Guid PositionId, Guid InvitedUserId, Guid InvitingLeaderId,
    string? Message, string Status, DateTime CreatedAt, DateTime UpdatedAt, DateTime ExpiresAt, DateTime? RespondedAt);
public sealed record ProjectInvitationsPage(IReadOnlyList<ProjectInvitationDto> Items, PaginationMeta Pagination);
public sealed record ProjectInvitationQuery(string Status = "all", Guid? PositionId = null, Guid? InvitedUserId = null, Guid? ProjectId = null, int Page = 1, int PageSize = 20);

// Expiration-only writes must commit before the service reports their business error.
public sealed record ProjectInvitationWriteResult(ProjectInvitationDto? Invitation, ProjectException? Error = null)
{
    public ProjectInvitationDto RequireSuccess() => Error == null ? Invitation! : throw Error;
}
