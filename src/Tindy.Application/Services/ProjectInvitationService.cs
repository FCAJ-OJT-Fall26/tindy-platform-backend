using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;

namespace Tindy.Application.Services;

public sealed class ProjectInvitationService(IProjectInvitationRepository repository)
{
    public async Task<ProjectInvitationDto> SendAsync(Guid leaderId, Guid projectId, CreateProjectInvitationRequest request, CancellationToken ct = default)
    {
        if (request.PositionId == Guid.Empty || request.InvitedUserId == Guid.Empty || request.Message?.Length > 2000)
            throw new ProjectException(400, "VALIDATION_FAILED", "A position and recipient are required; message cannot exceed 2000 characters.");
        var message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim();
        return (await repository.SendAsync(leaderId, projectId, request with { Message = message }, ct)).RequireSuccess();
    }

    public async Task<ProjectInvitationDto> ChangeAsync(Guid userId, Guid? projectId, Guid invitationId, string action, CancellationToken ct = default)
    {
        if (invitationId == Guid.Empty || (projectId.HasValue ? action != "cancel" : action is not ("accept" or "decline")))
            throw new ProjectException(400, "VALIDATION_FAILED", "Invalid invitation operation.");
        return (await repository.ChangeAsync(userId, projectId, invitationId, action, ct)).RequireSuccess();
    }

    public Task<ProjectInvitationsPage> GetForLeaderAsync(Guid leaderId, Guid projectId, ProjectInvitationQuery query, CancellationToken ct = default)
    {
        Validate(query);
        return repository.GetForLeaderAsync(leaderId, projectId, query, ct);
    }
    public Task<ProjectInvitationsPage> GetMineAsync(Guid userId, ProjectInvitationQuery query, CancellationToken ct = default)
    {
        Validate(query);
        return repository.GetMineAsync(userId, query, ct);
    }
    private static void Validate(ProjectInvitationQuery query)
    {
        if (query.Status is not ("all" or "pending" or "accepted" or "declined" or "cancelled" or "expired") ||
            query.PositionId == Guid.Empty || query.InvitedUserId == Guid.Empty || query.ProjectId == Guid.Empty)
            throw new ProjectException(400, "VALIDATION_FAILED", "Invalid invitation filters.");
    }
}
