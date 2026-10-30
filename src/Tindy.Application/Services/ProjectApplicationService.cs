using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;

namespace Tindy.Application.Services;

public sealed class ProjectApplicationService(IProjectApplicationRepository repository)
{
    public Task<ProjectApplicationDto> ApplyAsync(Guid userId, Guid projectId, CreateProjectApplicationRequest request, CancellationToken ct = default)
    {
        if (request.PositionId == Guid.Empty || request.Message?.Length > 2000)
            throw new ProjectException(400, "VALIDATION_FAILED", "A position ID is required; message cannot exceed 2000 characters.");
        return repository.CreateAsync(userId, projectId, request with { Message = request.Message?.Trim() }, ct);
    }
    public Task<ProjectApplicationDto> WithdrawAsync(Guid userId, Guid applicationId, CancellationToken ct = default) => repository.WithdrawAsync(userId, applicationId, ct);
    public Task<ProjectApplicationDto> AcceptAsync(Guid userId, Guid projectId, Guid applicationId, CancellationToken ct = default) => repository.DecideAsync(userId, projectId, applicationId, true, ct);
    public Task<ProjectApplicationDto> RejectAsync(Guid userId, Guid projectId, Guid applicationId, CancellationToken ct = default) => repository.DecideAsync(userId, projectId, applicationId, false, ct);
    public Task<MyApplicationsPage> GetMineAsync(Guid userId, int page, int pageSize, CancellationToken ct = default) => repository.GetMineAsync(userId, page, pageSize, ct);

    public async Task<ApplicationReviewPage> ReviewAsync(Guid leaderId, Guid projectId, ApplicationReviewQuery query, CancellationToken ct = default)
    {
        if (query.Status is not ("pending" or "accepted" or "rejected" or "withdrawn" or "all") || query.SortBy is not ("match" or "newest"))
            throw new ProjectException(400, "VALIDATION_FAILED", "Invalid application status or sort_by (match/newest).");
        var records = await repository.GetReviewAsync(leaderId, projectId, query, ct);
        var scored = records.Select(r => new ApplicationReviewDto(r.Application, r.Applicant, r.Position,
            PositionMatchScorer.Score(new(r.Applicant.PreferredRoleId, r.Applicant.AvailableHoursPerWeek,
                r.Applicant.Skills.Select(s => s.SkillId).ToArray()), r.Position, r.ExpectedCommitmentHours)));
        var sorted = query.SortBy == "match"
            ? scored.OrderByDescending(r => r.Matching.MatchScore).ThenByDescending(r => r.Application.CreatedAt).ThenBy(r => r.Application.ApplicationId)
            : scored.OrderByDescending(r => r.Application.CreatedAt).ThenBy(r => r.Application.ApplicationId);
        var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, 50);
        var total = records.Count; var pages = (int)Math.Ceiling((double)total / size);
        return new(sorted.Skip((int)Math.Min((long)(page - 1) * size, int.MaxValue)).Take(size).ToArray(), new(page, size, total, pages, page < pages, page > 1));
    }
}
