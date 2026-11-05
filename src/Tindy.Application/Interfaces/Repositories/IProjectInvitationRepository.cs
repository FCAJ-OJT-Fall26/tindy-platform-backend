using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Repositories;

public interface IProjectInvitationRepository
{
    Task<ProjectInvitationWriteResult> SendAsync(Guid leaderId, Guid projectId, CreateProjectInvitationRequest request, CancellationToken ct);
    Task<ProjectInvitationWriteResult> ChangeAsync(Guid userId, Guid? projectId, Guid invitationId, string action, CancellationToken ct);
    Task<ProjectInvitationsPage> GetForLeaderAsync(Guid leaderId, Guid projectId, ProjectInvitationQuery query, CancellationToken ct);
    Task<ProjectInvitationsPage> GetMineAsync(Guid userId, ProjectInvitationQuery query, CancellationToken ct);
}
