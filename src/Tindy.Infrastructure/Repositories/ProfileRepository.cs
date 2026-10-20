using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Profile;
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
                _context.UserCertificates.Count(certificate => certificate.UserId == user.UserId)))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
