using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;

    public ProjectService(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<CreateProjectResponse> CreateProjectAsync(
        Guid currentUserId,
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateRequest(request);

        // Validate Roles and Skills
        foreach (var pos in request.Positions)
        {
            var roleExists = await _projectRepository.RoleExistsAsync(pos.RoleId, cancellationToken);
            if (!roleExists)
            {
                throw new ProjectException(400, "ROLE_NOT_FOUND", $"Role {pos.RoleId} does not exist in catalog.");
            }

            if (pos.Skills is { Count: > 0 })
            {
                var skillIds = pos.Skills.Select(s => s.SkillId).Distinct().ToList();
                var validSkillIds = await _projectRepository.ValidateSkillIdsAsync(skillIds, cancellationToken);
                if (validSkillIds.Count != skillIds.Count)
                {
                    throw new ProjectException(400, "SKILL_NOT_FOUND", "One or more skills do not exist in catalog.");
                }
            }
        }

        var projectId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var project = new Project
        {
            ProjectId = projectId,
            LeaderId = currentUserId,
            Name = request.Name.Trim(),
            Tagline = request.Tagline?.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category.Trim(),
            DifficultyLevel = string.IsNullOrWhiteSpace(request.DifficultyLevel) ? "intermediate" : request.DifficultyLevel.Trim().ToLowerInvariant(),
            ExpectedCommitmentHours = request.ExpectedCommitmentHours,
            Duration = string.IsNullOrWhiteSpace(request.Duration) ? "3 months" : request.Duration.Trim(),
            InitialTeamSize = Math.Max(1, request.InitialTeamSize),
            MaxTeamCapacity = request.MaxTeamCapacity,
            ColorTheme = string.IsNullOrWhiteSpace(request.ColorTheme) ? "indigo" : request.ColorTheme.Trim(),
            Status = "recruiting",
            RecruitmentClosingDays = request.RecruitmentClosingDays > 0 ? request.RecruitmentClosingDays : 14,
            Goals = request.Goals?.ToList() ?? new List<string>(),
            CreatedAt = now
        };

        // Add creator as Project Lead member
        project.Members.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = currentUserId,
            RoleTitle = "Project Lead",
            IsLeader = true,
            JoinedAt = now,
            Status = "active"
        });

        // Add initial positions and skills
        foreach (var pos in request.Positions)
        {
            var position = new ProjectPosition
            {
                PositionId = Guid.NewGuid(),
                ProjectId = projectId,
                RoleId = pos.RoleId,
                Title = pos.Title.Trim(),
                RequiredCount = Math.Max(1, pos.RequiredCount),
                FilledCount = 0,
                ExperienceLevel = string.IsNullOrWhiteSpace(pos.ExperienceLevel) ? "intermediate" : pos.ExperienceLevel.Trim().ToLowerInvariant(),
                Status = "open",
                CreatedAt = now
            };

            if (pos.Skills != null)
            {
                foreach (var sk in pos.Skills)
                {
                    position.PositionSkills.Add(new ProjectPositionSkill
                    {
                        PositionId = position.PositionId,
                        SkillId = sk.SkillId,
                        IsRequired = sk.IsRequired
                    });
                }
            }

            project.Positions.Add(position);
        }

        await _projectRepository.CreateAsync(project, cancellationToken);

        // Fetch details to return complete leader summary
        var created = await _projectRepository.GetByIdWithDetailsAsync(projectId, cancellationToken) ?? project;

        var leaderDisplayName = created.Leader?.Profile?.DisplayName ?? created.Leader?.Username ?? "Leader";
        var leaderAvatar = created.Leader?.Profile?.AvatarUrl;

        return new CreateProjectResponse(
            created.ProjectId,
            created.Name,
            created.Tagline,
            created.Category,
            created.Status,
            new ProjectLeaderSummaryDto(
                created.LeaderId,
                leaderDisplayName,
                created.Leader?.PrimaryEmail,
                leaderAvatar,
                GetInitials(leaderDisplayName)),
            created.Members.Count(m => m.Status == "active"),
            created.MaxTeamCapacity,
            created.Positions.Count,
            created.CreatedAt);
    }

    public async Task<PagedProjectsResponse> GetProjectsAsync(
        GetProjectsQuery query,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _projectRepository.GetPagedProjectsAsync(query, cancellationToken);

        var list = items.Select(p =>
        {
            var leaderDisplayName = p.Leader?.Profile?.DisplayName ?? p.Leader?.Username ?? "Leader";
            var leaderAvatar = p.Leader?.Profile?.AvatarUrl;
            var daysLeft = Math.Max(0, p.RecruitmentClosingDays - (int)(DateTime.UtcNow - p.CreatedAt).TotalDays);

            var openPositions = p.Positions
                .Where(pos => pos.Status == "open")
                .Select(pos => new OpenPositionSummaryDto(
                    pos.PositionId,
                    pos.Title,
                    pos.Role?.Name ?? pos.Title,
                    pos.RequiredCount,
                    pos.PositionSkills.Where(s => s.IsRequired).Select(s => s.Skill.Name).ToList(),
                    pos.PositionSkills.Where(s => !s.IsRequired).Select(s => s.Skill.Name).ToList()))
                .ToList();

            return new ProjectSummaryDto(
                p.ProjectId,
                p.Name,
                p.Tagline,
                p.Description,
                p.Category,
                p.DifficultyLevel,
                p.ExpectedCommitmentHours,
                p.Duration,
                p.Members.Count(m => m.Status == "active"),
                p.MaxTeamCapacity,
                p.ColorTheme,
                GetInitials(p.Name),
                new ProjectLeaderSummaryDto(
                    p.LeaderId,
                    leaderDisplayName,
                    p.Leader?.PrimaryEmail,
                    leaderAvatar,
                    GetInitials(leaderDisplayName)),
                openPositions,
                p.CreatedAt,
                daysLeft);
        }).ToList();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var pagination = new PaginationMeta(
            page,
            pageSize,
            totalCount,
            totalPages,
            page < totalPages,
            page > 1);

        return new PagedProjectsResponse(list, pagination);
    }

    public async Task<ProjectDetailDto> GetProjectByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdWithDetailsAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        return MapToDetailDto(project);
    }

    public async Task<ProjectDetailDto> UpdateProjectAsync(
        Guid currentUserId,
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdWithDetailsAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        if (project.LeaderId != currentUserId)
        {
            throw new ProjectException(403, "PROJECT_ACCESS_DENIED", "Only the project leader can update this project.");
        }

        ValidateUpdateRequest(request);

        var currentTeamSize = project.Members.Count(m => m.Status == "active");
        if (request.MaxTeamCapacity < currentTeamSize)
        {
            throw new ProjectException(400, "PROJECT_CAPACITY_EXCEEDED", $"Max team capacity ({request.MaxTeamCapacity}) cannot be less than current team size ({currentTeamSize}).");
        }

        project.Name = request.Name.Trim();
        project.Tagline = request.Tagline?.Trim();
        project.Description = request.Description.Trim();
        project.Category = request.Category.Trim();
        project.DifficultyLevel = string.IsNullOrWhiteSpace(request.DifficultyLevel) ? project.DifficultyLevel : request.DifficultyLevel.Trim().ToLowerInvariant();
        project.ExpectedCommitmentHours = request.ExpectedCommitmentHours;
        project.Duration = string.IsNullOrWhiteSpace(request.Duration) ? project.Duration : request.Duration.Trim();
        project.MaxTeamCapacity = request.MaxTeamCapacity;
        project.ColorTheme = string.IsNullOrWhiteSpace(request.ColorTheme) ? project.ColorTheme : request.ColorTheme.Trim();
        if (request.Goals != null)
        {
            project.Goals = request.Goals.ToList();
        }

        await _projectRepository.UpdateAsync(project, cancellationToken);

        return MapToDetailDto(project);
    }

    public async Task DeleteProjectAsync(
        Guid currentUserId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdWithDetailsAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        if (project.LeaderId != currentUserId)
        {
            throw new ProjectException(403, "PROJECT_ACCESS_DENIED", "Only the project leader can archive this project.");
        }

        project.Status = "archived";
        project.ArchivedAt = DateTime.UtcNow;

        foreach (var pos in project.Positions)
        {
            pos.Status = "closed";
        }

        await _projectRepository.UpdateAsync(project, cancellationToken);
    }

    public async Task<MyProjectsResponse> GetMyProjectsAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var leadingProjects = await _projectRepository.GetLeadingProjectsAsync(currentUserId, cancellationToken);
        var joinedMemberships = await _projectRepository.GetJoinedMembershipsAsync(currentUserId, cancellationToken);

        var leadingDtos = leadingProjects.Select(p => new MyLeadingProjectDto(
            p.ProjectId,
            p.Name,
            p.Status,
            p.Members.Count(m => m.Status == "active"),
            p.MaxTeamCapacity,
            0, // Default pending count until applications module
            p.CreatedAt)).ToList();

        var joinedDtos = joinedMemberships.Select(m => new MyJoinedProjectDto(
            m.ProjectId,
            m.Project.Name,
            m.Project.Status,
            m.RoleTitle,
            m.Project.Leader?.Profile?.DisplayName ?? m.Project.Leader?.Username ?? "Leader",
            m.JoinedAt)).ToList();

        return new MyProjectsResponse(leadingDtos, joinedDtos);
    }

    private static ProjectDetailDto MapToDetailDto(Project project)
    {
        var leaderDisplayName = project.Leader?.Profile?.DisplayName ?? project.Leader?.Username ?? "Leader";
        var leaderAvatar = project.Leader?.Profile?.AvatarUrl;
        var daysLeft = Math.Max(0, project.RecruitmentClosingDays - (int)(DateTime.UtcNow - project.CreatedAt).TotalDays);

        var teamMembers = project.Members
            .Where(m => m.Status == "active")
            .OrderByDescending(m => m.IsLeader)
            .ThenBy(m => m.JoinedAt)
            .Select(m =>
            {
                var memberName = m.User?.Profile?.DisplayName ?? m.User?.Username ?? "Member";
                return new ProjectTeamMemberDto(
                    m.UserId,
                    memberName,
                    m.RoleTitle,
                    m.User?.Profile?.AvatarUrl,
                    GetInitials(memberName),
                    m.IsLeader,
                    m.JoinedAt);
            }).ToList();

        var positions = project.Positions.Select(pos => new ProjectPositionDto(
            pos.PositionId,
            pos.ProjectId,
            pos.RoleId,
            pos.Title,
            pos.Description,
            pos.RequiredCount,
            pos.FilledCount,
            pos.ExperienceLevel,
            pos.Status,
            pos.PositionSkills.Select(ps => new PositionSkillDto(
                ps.SkillId,
                ps.Skill?.Name ?? "",
                ps.IsRequired)).ToList())).ToList();

        return new ProjectDetailDto(
            project.ProjectId,
            project.Name,
            project.Tagline,
            project.Description,
            project.Category,
            project.DifficultyLevel,
            project.Status,
            project.ExpectedCommitmentHours,
            project.Duration,
            teamMembers.Count,
            project.MaxTeamCapacity,
            project.ColorTheme,
            GetInitials(project.Name),
            project.CreatedAt,
            daysLeft,
            new ProjectLeaderSummaryDto(
                project.LeaderId,
                leaderDisplayName,
                project.Leader?.PrimaryEmail,
                leaderAvatar,
                GetInitials(leaderDisplayName)),
            project.Goals,
            teamMembers,
            positions);
    }

    private static void ValidateCreateRequest(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 3 || request.Name.Trim().Length > 150)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Project name must be between 3 and 150 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length < 20)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Project description must be at least 20 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Category is required.");
        }

        if (request.ExpectedCommitmentHours < 1 || request.ExpectedCommitmentHours > 40)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Expected commitment hours must be between 1 and 40.");
        }

        if (request.MaxTeamCapacity < request.InitialTeamSize)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Max team capacity must be greater than or equal to initial team size.");
        }

        if (request.Positions == null || request.Positions.Count == 0)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "At least one recruitment position is required.");
        }

        foreach (var pos in request.Positions)
        {
            if (string.IsNullOrWhiteSpace(pos.Title))
            {
                throw new ProjectException(400, "VALIDATION_FAILED", "Position title is required.");
            }
            if (pos.RequiredCount < 1)
            {
                throw new ProjectException(400, "VALIDATION_FAILED", "Position required count must be at least 1.");
            }
        }
    }

    private static void ValidateUpdateRequest(UpdateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 3 || request.Name.Trim().Length > 150)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Project name must be between 3 and 150 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length < 20)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Project description must be at least 20 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Category is required.");
        }

        if (request.ExpectedCommitmentHours < 1 || request.ExpectedCommitmentHours > 40)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Expected commitment hours must be between 1 and 40.");
        }
    }

    private static string GetInitials(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "PR";
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }
}
