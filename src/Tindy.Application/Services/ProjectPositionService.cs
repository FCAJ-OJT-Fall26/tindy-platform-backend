using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class ProjectPositionService : IProjectPositionService
{
    private readonly IProjectRepository _projectRepository;

    public ProjectPositionService(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<IReadOnlyList<ProjectPositionDto>> GetPositionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        var positions = await _projectRepository.GetPositionsByProjectIdAsync(projectId, cancellationToken);
        return positions.Select(MapToPositionDto).ToList();
    }

    public async Task<ProjectPositionDto> CreatePositionAsync(
        Guid currentUserId,
        Guid projectId,
        CreatePositionRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        if (project.LeaderId != currentUserId)
        {
            throw new ProjectException(403, "FORBIDDEN", "Only the project leader can add positions to this project.");
        }

        var roleExists = await _projectRepository.RoleExistsAsync(request.RoleId, cancellationToken);
        if (!roleExists)
        {
            throw new ProjectException(400, "ROLE_NOT_FOUND", $"Role {request.RoleId} does not exist in catalog.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 2 || request.Title.Trim().Length > 100)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Position title must be between 2 and 100 characters.");
        }

        if (request.RequiredCount < 1)
        {
            throw new ProjectException(400, "VALIDATION_FAILED", "Required count must be at least 1.");
        }

        var expLevel = NormalizeExperienceLevel(request.ExperienceLevel);

        var position = new ProjectPosition
        {
            PositionId = Guid.NewGuid(),
            ProjectId = projectId,
            RoleId = request.RoleId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            RequiredCount = request.RequiredCount,
            FilledCount = 0,
            ExperienceLevel = expLevel,
            Status = "open",
            CreatedAt = DateTime.UtcNow
        };

        await _projectRepository.AddPositionAsync(position, cancellationToken);

        var created = await _projectRepository.GetPositionWithDetailsAsync(projectId, position.PositionId, cancellationToken);
        return MapToPositionDto(created ?? position);
    }

    public async Task<ProjectPositionDto> UpdatePositionAsync(
        Guid currentUserId,
        Guid projectId,
        Guid positionId,
        UpdatePositionRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        if (project.LeaderId != currentUserId)
        {
            throw new ProjectException(403, "FORBIDDEN", "Only the project leader can update positions in this project.");
        }

        var position = await _projectRepository.GetPositionWithDetailsAsync(projectId, positionId, cancellationToken);
        if (position is null)
        {
            throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found in this project.");
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            if (request.Title.Trim().Length < 2 || request.Title.Trim().Length > 100)
            {
                throw new ProjectException(400, "VALIDATION_FAILED", "Position title must be between 2 and 100 characters.");
            }
            position.Title = request.Title.Trim();
        }

        if (request.RequiredCount.HasValue)
        {
            if (request.RequiredCount.Value < 1)
            {
                throw new ProjectException(400, "VALIDATION_FAILED", "Required count must be at least 1.");
            }
            if (request.RequiredCount.Value < position.FilledCount)
                throw new ProjectException(409, "POSITION_CAPACITY_EXCEEDED", "Required count cannot be below filled count.");
            position.RequiredCount = request.RequiredCount.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.ExperienceLevel))
        {
            position.ExperienceLevel = NormalizeExperienceLevel(request.ExperienceLevel);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim().ToLowerInvariant();
            if (st is not ("open" or "filled" or "closed"))
            {
                throw new ProjectException(400, "VALIDATION_FAILED", "Status must be open, filled, or closed.");
            }
            position.Status = st;
        }

        if (request.Description != null)
        {
            position.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        position.UpdatedAt = DateTime.UtcNow;
        await _projectRepository.UpdatePositionAsync(position, cancellationToken);

        return MapToPositionDto(position);
    }

    public async Task DeletePositionAsync(
        Guid currentUserId,
        Guid projectId,
        Guid positionId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        if (project.LeaderId != currentUserId)
        {
            throw new ProjectException(403, "FORBIDDEN", "Only the project leader can delete positions from this project.");
        }

        var position = await _projectRepository.GetPositionWithDetailsAsync(projectId, positionId, cancellationToken);
        if (position is null)
        {
            throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found in this project.");
        }

        await _projectRepository.DeletePositionAsync(position, cancellationToken);
    }

    public async Task<AddPositionSkillResponse> AddPositionSkillAsync(
        Guid currentUserId,
        Guid projectId,
        Guid positionId,
        AddPositionSkillRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        if (project.LeaderId != currentUserId)
        {
            throw new ProjectException(403, "FORBIDDEN", "Only the project leader can manage skills for this position.");
        }

        var position = await _projectRepository.GetPositionWithDetailsAsync(projectId, positionId, cancellationToken);
        if (position is null)
        {
            throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found in this project.");
        }

        var skill = await _projectRepository.GetSkillByIdAsync(request.SkillId, cancellationToken);
        if (skill is null)
        {
            throw new ProjectException(404, "SKILL_NOT_FOUND", $"Skill {request.SkillId} not found in catalog.");
        }

        var existing = await _projectRepository.GetPositionSkillAsync(positionId, request.SkillId, cancellationToken);
        if (existing != null)
        {
            existing.IsRequired = request.IsRequired;
            existing.MinimumExperienceLevel = request.MinimumExperienceLevel;
            await _projectRepository.UpdatePositionAsync(position, cancellationToken);
            return new AddPositionSkillResponse(positionId, skill.SkillId, skill.Name, existing.IsRequired);
        }

        var link = new ProjectPositionSkill
        {
            PositionId = positionId,
            SkillId = request.SkillId,
            IsRequired = request.IsRequired,
            MinimumExperienceLevel = request.MinimumExperienceLevel
        };

        await _projectRepository.AddPositionSkillAsync(link, cancellationToken);
        return new AddPositionSkillResponse(positionId, skill.SkillId, skill.Name, link.IsRequired);
    }

    public async Task RemovePositionSkillAsync(
        Guid currentUserId,
        Guid projectId,
        Guid positionId,
        Guid skillId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.Status == "archived")
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        }

        if (project.LeaderId != currentUserId)
        {
            throw new ProjectException(403, "FORBIDDEN", "Only the project leader can remove skills from this position.");
        }

        var position = await _projectRepository.GetPositionWithDetailsAsync(projectId, positionId, cancellationToken);
        if (position is null)
        {
            throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found in this project.");
        }

        var link = await _projectRepository.GetPositionSkillAsync(positionId, skillId, cancellationToken);
        if (link != null)
        {
            await _projectRepository.DeletePositionSkillAsync(link, cancellationToken);
        }
    }

    private static ProjectPositionDto MapToPositionDto(ProjectPosition pos)
    {
        return new ProjectPositionDto(
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
                ps.IsRequired)).ToList());
    }

    private static string NormalizeExperienceLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return "intermediate";
        var norm = level.Trim().ToLowerInvariant();
        if (norm.Contains("beginner")) return "beginner_friendly";
        if (norm.Contains("advanced")) return "advanced";
        return "intermediate";
    }
}
