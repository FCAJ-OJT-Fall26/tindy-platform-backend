using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ExperienceRepository : IExperienceRepository
{
    private readonly TindyDbContext _context;

    public ExperienceRepository(TindyDbContext context) => _context = context;

    public Task<bool> IsEligibleOwnerAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.Users.AsNoTracking().AnyAsync(user => user.UserId == userId &&
            user.Status == "active" && user.DeletedAt == null && user.IsOnboardingCompleted && user.Profile != null,
            cancellationToken);

    public async Task<IReadOnlyList<ExperienceResponse>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var experiences = await ForOwner(userId)
            .OrderByDescending(experience => experience.StartDate.HasValue)
            .ThenByDescending(experience => experience.StartDate)
            .ThenByDescending(experience => experience.CreatedAt)
            .ThenBy(experience => experience.ExperienceId)
            .ToListAsync(cancellationToken);
        return experiences.Select(ToResponse).ToArray();
    }

    public async Task<ExperienceResponse?> GetOwnedAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken = default)
    {
        var experience = await ForOwner(userId)
            .SingleOrDefaultAsync(item => item.ExperienceId == experienceId, cancellationToken);
        return experience is null ? null : ToResponse(experience);
    }

    public Task<ProfileExperience?> FindOwnedForUpdateAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken = default) =>
        _context.ProfileExperiences
            .Include(experience => experience.ExperienceSkills)
            .SingleOrDefaultAsync(experience => experience.UserId == userId && experience.ExperienceId == experienceId,
                cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default) =>
        await _context.Skills.AsNoTracking()
            .Where(skill => skill.IsActive && skillIds.Contains(skill.SkillId))
            .Select(skill => skill.SkillId)
            .ToArrayAsync(cancellationToken);

    public void Add(ProfileExperience experience) => _context.ProfileExperiences.Add(experience);

    public void SyncSkills(ProfileExperience experience, IReadOnlyCollection<Guid> skillIds)
    {
        var requested = skillIds.ToHashSet();
        var current = experience.ExperienceSkills.Select(link => link.SkillId).ToHashSet();
        foreach (var link in experience.ExperienceSkills.Where(link => !requested.Contains(link.SkillId)).ToArray())
            _context.ProfileExperienceSkills.Remove(link);
        foreach (var id in requested.Where(id => !current.Contains(id)))
            _context.ProfileExperienceSkills.Add(new ProfileExperienceSkill { ExperienceId = experience.ExperienceId, SkillId = id });
    }

    public void Remove(ProfileExperience experience) => _context.ProfileExperiences.Remove(experience);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Guid ownerUserId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // Same lock order as Profile/Onboarding writes: lock owner before mutation.
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {ownerUserId} FOR UPDATE", cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private IQueryable<ProfileExperience> ForOwner(Guid userId) =>
        _context.ProfileExperiences.AsNoTracking().AsSplitQuery()
            .Where(experience => experience.UserId == userId)
            .Include(experience => experience.ExperienceSkills)
                .ThenInclude(link => link.Skill);

    private static ExperienceResponse ToResponse(ProfileExperience experience) =>
        new(experience.ExperienceId, experience.ProjectName, experience.RoleTitle,
            experience.Description,
            experience.ExperienceSkills.OrderBy(link => link.Skill.Name).ThenBy(link => link.SkillId)
                .Select(link => new ExperienceTechnologyResponse(link.SkillId, link.Skill.Name)).ToArray(),
            experience.RepoUrl,
            experience.StartDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            experience.EndDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            experience.IsOngoing,
            experience.CreatedAt,
            experience.UpdatedAt);
}
