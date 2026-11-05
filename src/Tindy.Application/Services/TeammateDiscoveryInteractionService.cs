using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;

namespace Tindy.Application.Services;

public sealed class TeammateDiscoveryInteractionService(ITeammateDiscoveryInteractionRepository repository)
{
    private static void RequirePosition(Guid positionId)
    {
        if (positionId == Guid.Empty) throw new ProjectException(400, "VALIDATION_FAILED", "position_id is required.");
    }
    public Task<TeammateInteractionsPage> GetAsync(Guid leaderId, Guid projectId, Guid positionId, string? action, int page, int pageSize, CancellationToken ct)
    {
        RequirePosition(positionId);
        if (action is not (null or "saved" or "shortlisted" or "skipped")) throw new ProjectException(400, "VALIDATION_FAILED", "Invalid action.");
        return repository.GetAsync(leaderId, projectId, positionId, action, page, pageSize, ct);
    }
    public Task<TeammateInteractionDto> SetAsync(Guid leaderId, Guid projectId, Guid positionId, Guid candidateId, SetTeammateInteractionRequest request, CancellationToken ct)
    {
        RequirePosition(positionId);
        if (candidateId == Guid.Empty || request.Action is not ("saved" or "shortlisted" or "skipped")) throw new ProjectException(400, "VALIDATION_FAILED", "Invalid candidate or action.");
        return repository.SetAsync(leaderId, projectId, positionId, candidateId, request.Action, ct);
    }
    public Task ResetAsync(Guid leaderId, Guid projectId, Guid positionId, Guid candidateId, CancellationToken ct)
    {
        RequirePosition(positionId);
        if (candidateId == Guid.Empty) throw new ProjectException(400, "VALIDATION_FAILED", "A candidate UUID is required.");
        return repository.ResetAsync(leaderId, projectId, positionId, candidateId, ct);
    }
}
