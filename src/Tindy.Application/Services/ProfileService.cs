using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class ProfileService : IProfileService
{
    private readonly IProfileRepository _profileRepository;

    public ProfileService(IProfileRepository profileRepository) => _profileRepository = profileRepository;

    public async Task<MyProfileResponse> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new InvalidCredentialsException();

        return await _profileRepository.GetMyProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Profile was not found.");
    }

    public async Task<MemberProfileResponse> GetMemberProfileAsync(Guid targetUserId, CancellationToken cancellationToken = default)
    {
        // Target IDs come from the route, not the authenticated caller claim.
        // Do not disclose whether malformed, inactive, deleted, or incomplete accounts exist.
        if (targetUserId == Guid.Empty)
            throw new NotFoundException("Profile was not found.");

        return await _profileRepository.GetMemberProfileAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Profile was not found.");
    }

    public Task<MyProfileResponse> UpdateMyProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new InvalidCredentialsException();
        ArgumentNullException.ThrowIfNull(request);

        // Lock the user row before loading a tracked profile. This uses the same
        // ordering as Onboarding writes to avoid concurrent updates/lost skill changes.
        return _profileRepository.ExecuteInTransactionAsync(userId, async token =>
        {
            var user = await _profileRepository.GetUserForUpdateAsync(userId, token);
            if (user is null || user.Status != "active" || user.DeletedAt is not null || !user.IsOnboardingCompleted || user.Profile is null)
                throw new NotFoundException("Profile was not found.");

            var profile = user.Profile;
            var selectedSkills = request.SkillIds.IsSpecified ? request.SkillIds.Value! : null;

            // Validate new catalog IDs before any mutation. Previously selected inactive
            // roles/skills may remain, but cannot be newly introduced.
            if (request.PreferredRoleId.IsSpecified && profile.PreferredRoleId != request.PreferredRoleId.Value &&
                !await _profileRepository.IsActiveProfessionalRoleAsync(request.PreferredRoleId.Value!.Value, token))
                throw new BusinessRuleException("Selected professional role does not exist or is inactive.");

            if (selectedSkills is not null)
            {
                var existingIds = profile.UserSkills.Select(link => link.SkillId).ToHashSet();
                var newlyAddedIds = selectedSkills.Where(id => !existingIds.Contains(id)).ToArray();
                if (newlyAddedIds.Length > 0)
                {
                    var activeIds = await _profileRepository.GetActiveSkillIdsAsync(newlyAddedIds, token);
                    if (newlyAddedIds.Except(activeIds).Any())
                        throw new BusinessRuleException("One or more selected skills do not exist or are inactive.");
                }
            }

            // Only supplied fields may be changed; null clears optional fields.
            if (request.DisplayName.IsSpecified) profile.DisplayName = request.DisplayName.Value!;
            if (request.University.IsSpecified) profile.University = request.University.Value!;
            if (request.PhoneNumber.IsSpecified) profile.PhoneNumber = request.PhoneNumber.Value;
            if (request.Headline.IsSpecified) profile.Headline = request.Headline.Value;
            if (request.Bio.IsSpecified) profile.Bio = request.Bio.Value;
            if (request.Location.IsSpecified) profile.Location = request.Location.Value;
            if (request.CurrentRole.IsSpecified) profile.CurrentRole = request.CurrentRole.Value;
            if (request.PreferredRoleId.IsSpecified) profile.PreferredRoleId = request.PreferredRoleId.Value;
            if (request.AvailableHoursPerWeek.IsSpecified) profile.AvailableHoursPerWeek = request.AvailableHoursPerWeek.Value;
            if (request.ProfessionalInterests.IsSpecified) profile.ProfessionalInterests = request.ProfessionalInterests.Value;
            if (request.LearningGoals.IsSpecified) profile.LearningGoals = request.LearningGoals.Value;
            if (request.PreviousProjectsAndContribution.IsSpecified) profile.PreviousProjectsAndContribution = request.PreviousProjectsAndContribution.Value;
            if (request.TechnologiesToLearn.IsSpecified) profile.TechnologiesToLearn = request.TechnologiesToLearn.Value;
            if (request.GithubUrl.IsSpecified) profile.GithubUrl = request.GithubUrl.Value;
            if (request.LinkedinUrl.IsSpecified) profile.LinkedinUrl = request.LinkedinUrl.Value;
            if (request.PersonalWebsiteUrl.IsSpecified) profile.PersonalWebsiteUrl = request.PersonalWebsiteUrl.Value;
            if (request.ResumeUrl.IsSpecified) profile.ResumeUrl = request.ResumeUrl.Value;
            if (request.PublicEmail.IsSpecified) profile.PublicEmail = request.PublicEmail.Value;

            if (selectedSkills is not null)
                _profileRepository.SyncUserSkills(profile, selectedSkills);

            // Protect post-onboarding invariants without revalidating optional historical
            // text/links that were not included in this PATCH.
            var updatedSkillCount = selectedSkills?.Count ?? profile.UserSkills.Count;
            if (string.IsNullOrWhiteSpace(profile.DisplayName) || string.IsNullOrWhiteSpace(profile.University) ||
                profile.PreferredRoleId is null || profile.PreferredRoleId == Guid.Empty ||
                profile.AvailableHoursPerWeek is null or < 1 or > 40 || updatedSkillCount is < 1 or > 30)
                throw new BusinessRuleException("The completed profile must retain its required onboarding information.");

            profile.UpdatedAt = DateTime.UtcNow;
            await _profileRepository.SaveChangesAsync(token);

            // Read within the SAME transaction, after SaveChanges, to return DB state.
            return await _profileRepository.GetMyProfileAsync(userId, token)
                ?? throw new NotFoundException("Profile was not found.");
        }, cancellationToken);
    }
}
