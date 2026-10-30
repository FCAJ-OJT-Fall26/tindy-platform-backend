using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Repositories;

public interface IProjectApplicationRepository
{
    Task<ProjectApplicationDto> CreateAsync(Guid userId, Guid projectId, CreateProjectApplicationRequest request, CancellationToken ct = default);
    Task<ProjectApplicationDto> WithdrawAsync(Guid userId, Guid applicationId, CancellationToken ct = default);
    Task<ProjectApplicationDto> DecideAsync(Guid leaderId, Guid projectId, Guid applicationId, bool accept, CancellationToken ct = default);
    Task<MyApplicationsPage> GetMineAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationReviewData>> GetReviewAsync(Guid leaderId, Guid projectId, ApplicationReviewQuery query, CancellationToken ct = default);
}
