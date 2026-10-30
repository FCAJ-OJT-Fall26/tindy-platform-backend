namespace Tindy.Application.DTOs.Projects;

public sealed record CreateProjectApplicationRequest(Guid PositionId, string? Message = null);
public sealed record ProjectApplicationDto(Guid ApplicationId, Guid ProjectId, Guid PositionId, Guid ApplicantId,
    string Status, string? Message, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ReviewedAt, Guid? ReviewedBy);
public sealed record ApplicantSkill(Guid SkillId, string Name);
public sealed record ApplicantPreview(Guid UserId, string DisplayName, Guid? PreferredRoleId, string? PreferredRoleName,
    int? AvailableHoursPerWeek, IReadOnlyList<ApplicantSkill> Skills, int ExperiencesCount, int CertificatesCount,
    string ProfileUrl, string ExperiencesUrl, string CertificatesUrl);
public sealed record ApplicationReviewData(ProjectApplicationDto Application, ApplicantPreview Applicant, DiscoveryPosition Position,
    int ExpectedCommitmentHours);
public sealed record ApplicationReviewDto(ProjectApplicationDto Application, ApplicantPreview Applicant,
    DiscoveryPosition AppliedPosition, PositionMatch Matching);
public sealed record ApplicationReviewPage(IReadOnlyList<ApplicationReviewDto> Items, PaginationMeta Pagination);
public sealed record MyApplicationsPage(IReadOnlyList<ProjectApplicationDto> Items, PaginationMeta Pagination);
public sealed record ApplicationReviewQuery(Guid? PositionId = null, string Status = "pending", string SortBy = "match", int Page = 1, int PageSize = 20);
