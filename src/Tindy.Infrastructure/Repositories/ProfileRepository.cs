using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ProfileRepository : IProfileRepository
{
    private readonly TindyDbContext _context;

    public ProfileRepository(TindyDbContext context) => _context = context;

    public Task<MyProfileResponse?> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Project only the permitted fields. Never Include Credentials, OAuth accounts or roles.
        // AsSplitQuery avoids Cartesian expansion of the nested skill collection.
        return _context.Users
            .AsNoTracking()
            .Where(user => user.UserId == userId && user.Status == "active" &&
                user.DeletedAt == null && user.IsOnboardingCompleted && user.Profile != null)
            .AsSplitQuery()
            .Select(user => new MyProfileResponse(
                user.UserId,
                user.Username,
                user.Profile!.DisplayName,
                user.Profile.University,
                user.Profile.PhoneNumber,
                user.Profile.Headline,
                user.Profile.Bio,
                user.Profile.Location,
                user.Profile.CurrentRole,
                user.Profile.PreferredProfessionalRole == null
                    ? null
                    : new PreferredRoleSummary(
                        user.Profile.PreferredProfessionalRole.ProfessionalRoleId,
                        user.Profile.PreferredProfessionalRole.Name),
                user.Profile.AvailableHoursPerWeek,
                user.Profile.ProfessionalInterests,
                user.Profile.LearningGoals,
                user.Profile.PreviousProjectsAndContribution,
                user.Profile.TechnologiesToLearn,
                user.Profile.GithubUrl,
                user.Profile.LinkedinUrl,
                user.Profile.PersonalWebsiteUrl,
                user.Profile.ResumeUrl,
                user.Profile.PublicEmail,
                user.Profile.AvatarUrl,
                user.Profile.CoverImageUrl,
                user.Profile.UserSkills
                    .OrderBy(link => link.Skill.Name)
                    .ThenBy(link => link.SkillId)
                    .Select(link => new ProfileSkillSummary(link.SkillId, link.Skill.Name))
                    .ToList(),
                _context.ProfileExperiences.Count(experience => experience.UserId == user.UserId),
                _context.UserCertificates.Count(certificate => certificate.UserId == user.UserId),
                user.Profile.AvatarSource, user.Profile.AvatarAssetId, user.Profile.CoverAssetId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<MemberProfileResponse?> GetMemberProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Project only fields permitted to other authenticated members. Filtering
        // at the query level treats missing, suspended, deleted, and incomplete
        // target profiles identically (404 from Application service).
        return _context.Users
            .AsNoTracking()
            .Where(user => user.UserId == userId && user.Status == "active" &&
                user.DeletedAt == null && user.IsOnboardingCompleted && user.Profile != null)
            .AsSplitQuery()
            .Select(user => new MemberProfileResponse(
                user.UserId,
                user.Username,
                user.Profile!.DisplayName,
                user.Profile.University,
                user.Profile.Headline,
                user.Profile.Bio,
                user.Profile.Location,
                user.Profile.CurrentRole,
                user.Profile.PreferredProfessionalRole == null
                    ? null
                    : new PreferredRoleSummary(
                        user.Profile.PreferredProfessionalRole.ProfessionalRoleId,
                        user.Profile.PreferredProfessionalRole.Name),
                user.Profile.AvailableHoursPerWeek,
                user.Profile.ProfessionalInterests,
                user.Profile.LearningGoals,
                user.Profile.PreviousProjectsAndContribution,
                user.Profile.TechnologiesToLearn,
                user.Profile.GithubUrl,
                user.Profile.LinkedinUrl,
                user.Profile.PersonalWebsiteUrl,
                user.Profile.PublicEmail,
                user.Profile.AvatarUrl,
                user.Profile.CoverImageUrl,
                user.Profile.UserSkills
                    .OrderBy(link => link.Skill.Name)
                    .ThenBy(link => link.SkillId)
                    .Select(link => new ProfileSkillSummary(link.SkillId, link.Skill.Name))
                    .ToList(),
                _context.ProfileExperiences.Count(experience => experience.UserId == user.UserId),
                _context.UserCertificates.Count(certificate => certificate.UserId == user.UserId),
                user.Profile.AvatarSource, user.Profile.AvatarAssetId, user.Profile.CoverAssetId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<User?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.Users
            .Include(user => user.Profile)
                .ThenInclude(profile => profile!.UserSkills)
            .AsSplitQuery()
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    public Task<bool> IsActiveProfessionalRoleAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        _context.ProfessionalRoles.AsNoTracking()
            .AnyAsync(role => role.ProfessionalRoleId == roleId && role.IsActive, cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default) =>
        await _context.Skills.AsNoTracking()
            .Where(skill => skill.IsActive && skillIds.Contains(skill.SkillId))
            .Select(skill => skill.SkillId)
            .ToArrayAsync(cancellationToken);

    public void SyncUserSkills(UserProfile profile, IReadOnlyCollection<Guid> skillIds)
    {
        var requiredIds = skillIds.ToHashSet();
        var existingIds = profile.UserSkills.Select(link => link.SkillId).ToHashSet();

        foreach (var link in profile.UserSkills.Where(link => !requiredIds.Contains(link.SkillId)).ToArray())
            _context.UserSkills.Remove(link);

        foreach (var skillId in requiredIds.Where(id => !existingIds.Contains(id)))
            _context.UserSkills.Add(new UserSkill { UserId = profile.UserId, SkillId = skillId });
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Guid userId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Serialize concurrent writes to the same profile using the existing
        // Onboarding row-lock convention. Transaction disposal rolls back on failure.
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {userId} FOR UPDATE", cancellationToken);

        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

}
