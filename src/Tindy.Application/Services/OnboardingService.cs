using System.Text.RegularExpressions;
using Tindy.Application.DTOs.Onboarding;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class OnboardingService : IOnboardingService
{
    private readonly IOnboardingRepository _repository;

    public OnboardingService(IOnboardingRepository repository) => _repository = repository;

    // ===================================
    // GET /api/me
    // ===================================
    public async Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await GetActiveUserAsync(userId, ct);
        var profile = RequireProfile(user);
        var credential = user.Credential ?? throw new InvalidOperationException("User credential was not found.");

        var roles = user.UserRoles
            .Select(x => x.Role.RoleName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return new CurrentUserResponse(user.UserId, user.Username, profile.DisplayName, profile.University, credential.Email, user.Status, roles, user.IsOnboardingCompleted);
    }

    // ===================================
    // GET /api/me/onboarding
    // ===================================
    public async Task<OnboardingDraftResponse> GetDraftAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await GetActiveUserAsync(userId, ct);
        var profile = RequireProfile(user);

        var data = new Dictionary<string, object>
        {
            ["1"] = new
            {
                user.Username,
                profile.DisplayName,
                profile.University
            },
            ["2"] = new
            {
                Skills = profile.UserSkills
                    .Select(x => x.Skill.Name)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            },
            ["3"] = new { profile.PreferredRole },
            ["4"] = new { profile.ProfessionalInterests, profile.LearningGoals },
            ["5"] = new { profile.AvailableHoursPerWeek },
            ["6"] = new { profile.PreviousProjectsAndContribution, profile.TechnologiesToLearn },
            ["7"] = new { profile.GithubUrl, profile.PersonalWebsiteUrl },
            // AI not implemented: no generated summary is written in STEP 12.
            ["8"] = new { profile.Bio }
        };

        return new OnboardingDraftResponse(8, CompletedSteps(user), NextStep(user), user.IsOnboardingCompleted, data);
    }

    // ===================================
    // STEP 1 - BASIC INFORMATION
    // ===================================
    public Task<OnboardingSaveResponse> SaveStep1Async(Guid userId, OnboardingStep1Request request, CancellationToken ct = default)
    {
        return _repository.ExecuteInTransactionAsync(userId, async token =>
        {
            var user = await GetActiveUserAsync(userId, token);
            var profile = RequireProfile(user);

            var username = request.Username!.Trim();
            var normalized = username.ToLowerInvariant();

            if (await _repository.UsernameExistsAsync(userId, normalized, token))
                throw new UsernameAlreadyExistsException();

            user.Username = username;
            user.UsernameNormalized = normalized;
            user.UpdatedAt = DateTime.UtcNow;
            profile.DisplayName = request.DisplayName!.Trim();
            profile.University = request.University!.Trim();
            profile.UpdatedAt = DateTime.UtcNow;

            MarkStep(user, 1);
            await _repository.SaveChangesAsync(token);
            return Saved(user, 1);
        }, ct);
    }

    // ===================================
    // STEP 2 - SKILLS (REPLACE FULL LIST)
    // ===================================
    public Task<OnboardingSaveResponse> SaveStep2Async(Guid userId, OnboardingStep2Request request, CancellationToken ct = default)
    {
        // Canonicalize once and share the resulting names among all users.
        var names = request.Skills!
            .Select(NormalizeSkillDisplayName)
            .DistinctBy(x => x.ToLowerInvariant())
            .ToArray();

        return _repository.ExecuteInTransactionAsync(userId, async token =>
        {
            var user = await GetActiveUserAsync(userId, token);
            var profile = RequireProfile(user);
            var skills = await _repository.GetOrCreateSkillsAsync(names, token);

            _repository.SyncUserSkills(profile, skills);
            profile.UpdatedAt = DateTime.UtcNow;
            MarkStep(user, 2);
            await _repository.SaveChangesAsync(token);
            return Saved(user, 2);
        }, ct);
    }

    // ===================================
    // STEP 3 - PREFERRED ROLE (NOT RBAC)
    // ===================================
    public Task<OnboardingSaveResponse> SaveStep3Async(Guid userId, OnboardingStep3Request request, CancellationToken ct = default)
    {
        var roleName = OnboardingAllowedRoles.Names.Single(x => string.Equals(x, request.PreferredRole!.Trim(), StringComparison.OrdinalIgnoreCase));

        return SaveScalarStepAsync(userId, 3, (_, profile) => profile.PreferredRole = roleName, ct);
    }

    // ===================================
    // STEP 4 - INTERESTS & GOALS
    // ===================================
    public Task<OnboardingSaveResponse> SaveStep4Async(Guid userId, OnboardingStep4Request request, CancellationToken ct = default)
    {
        return SaveScalarStepAsync(userId, 4, (_, profile) =>
        {
            profile.ProfessionalInterests = request.ProfessionalInterests!.Trim();
            profile.LearningGoals = request.LearningGoals!.Trim();
        }, ct);
    }

    // ===================================
    // STEP 5 - AVAILABILITY
    // ===================================
    public Task<OnboardingSaveResponse> SaveStep5Async(Guid userId, OnboardingStep5Request request, CancellationToken ct = default)
    {
        return SaveScalarStepAsync(userId, 5, (_, profile) => profile.AvailableHoursPerWeek = request.AvailableHoursPerWeek!.Value, ct);
    }

    // ===================================
    // STEP 6 - PROJECT EXPERIENCE
    // ===================================
    public Task<OnboardingSaveResponse> SaveStep6Async(Guid userId, OnboardingStep6Request request, CancellationToken ct = default)
    {
        return SaveScalarStepAsync(userId, 6, (_, profile) =>
        {
            profile.PreviousProjectsAndContribution = request.PreviousProjectsAndContribution!.Trim();
            profile.TechnologiesToLearn = request.TechnologiesToLearn!.Trim();
        }, ct);
    }

    // ===================================
    // STEP 7 - OPTIONAL LINKS (SUBMIT REQUIRED)
    // ===================================
    public Task<OnboardingSaveResponse> SaveStep7Async(Guid userId, OnboardingStep7Request request, CancellationToken ct = default)
    {
        return SaveScalarStepAsync(userId, 7, (_, profile) =>
        {
            profile.GithubUrl = NullIfBlank(request.GithubUrl);
            profile.PersonalWebsiteUrl = NullIfBlank(request.PersonalWebsiteUrl);
        }, ct);
    }

    // ===================================
    // STEP 8 - COMPLETE & CONFIRM
    // ===================================

    public Task<OnboardingCompleteResponse> CompleteAsync(Guid userId, OnboardingCompleteRequest request, CancellationToken ct = default)
    {
        if (request.Confirmed != true)
        {
            throw new BusinessRuleException("You must confirm before completing your profile.");
        }

        return _repository.ExecuteInTransactionAsync(userId, async token =>
        {
            var user = await GetActiveUserAsync(userId, token);
            var profile = RequireProfile(user);

            // ===================================
            // 1. CHECK ALREADY COMPLETED
            // ===================================
            if (user.IsOnboardingCompleted)
            {
                return Completed(user);
            }

            // ===================================
            // 2. VALIDATE STEPS 1-7
            // ===================================
            var missingSteps = Enumerable.Range(1, 7)
                .Where(step => !user.OnboardingSteps.Any(x => x.StepNumber == step) || !IsStepDataValid(step, user, profile))
                .ToArray();

            if (missingSteps.Length > 0)
            {
                throw new OnboardingIncompleteException(missingSteps);
            }

            // ===================================
            // 3. SAVE USER-EDITED BIO (OPTIONAL)
            // ===================================
            if (request.BioWasProvided)
            {
                profile.Bio =
                    string.IsNullOrWhiteSpace(request.Bio)
                        ? null
                        : request.Bio.Trim();

                profile.UpdatedAt = DateTime.UtcNow;
            }

            // ===================================
            // 4. COMPLETE ONBOARDING
            // ===================================
            user.IsOnboardingCompleted = true;
            user.UpdatedAt = DateTime.UtcNow;

            // Save Bio + Onboarding status atomically
            await _repository.SaveChangesAsync(token);

            // ===================================
            // 5. RETURN RESPONSE
            // ===================================
            return Completed(user);

        }, ct);
    }


    private Task<OnboardingSaveResponse> SaveScalarStepAsync(Guid userId, int step, Action<User, UserProfile> mutate, CancellationToken ct)
    {
        return _repository.ExecuteInTransactionAsync(userId, async token =>
        {
            var user = await GetActiveUserAsync(userId, token);
            var profile = RequireProfile(user);
            mutate(user, profile);
            profile.UpdatedAt = DateTime.UtcNow;
            MarkStep(user, step);
            await _repository.SaveChangesAsync(token);
            return Saved(user, step);
        }, ct);
    }

    private async Task<User> GetActiveUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _repository.GetUserAsync(userId, ct);
        if (user is null || user.DeletedAt is not null ||
            !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidCredentialsException();
        }
        return user;
    }

    private static UserProfile RequireProfile(User user) =>
        user.Profile ?? throw new InvalidOperationException("User profile was not found.");

    private static string NormalizeSkillDisplayName(string name) =>
        Regex.Replace(name.Trim(), @"\s+", " ");

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void MarkStep(User user, int step)
    {
        var marker = user.OnboardingSteps.SingleOrDefault(x => x.StepNumber == step);
        if (marker is null)
            user.OnboardingSteps.Add(new UserOnboardingStep
            {
                UserId = user.UserId,
                StepNumber = step,
                CompletedAt = DateTime.UtcNow
            });
        else
            marker.CompletedAt = DateTime.UtcNow;
    }

    private static int[] CompletedSteps(User user)
    {
        var steps = user.OnboardingSteps.Select(x => x.StepNumber).ToList();
        if (user.IsOnboardingCompleted)
            steps.Add(8);
        return steps.Distinct().OrderBy(x => x).ToArray();
    }

    private static int NextStep(User user) =>
        Enumerable.Range(1, 7)
            .FirstOrDefault(x => !user.OnboardingSteps.Any(m => m.StepNumber == x), 8);

    private static OnboardingSaveResponse Saved(User user, int step) =>
        new(step, CompletedSteps(user), NextStep(user), user.IsOnboardingCompleted);

    private static OnboardingCompleteResponse Completed(User user) =>
        new(true, CompletedSteps(user), "/discover");

    private static bool IsStepDataValid(int step, User user, UserProfile profile)
    {
        switch (step)
        {
            case 1:
                return user.Username is { Length: >= 3 and <= 50 } &&
                    Regex.IsMatch(user.Username, @"^[A-Za-z0-9_.]{3,50}$") &&
                    user.UsernameNormalized == user.Username.ToLowerInvariant() &&
                    !string.IsNullOrWhiteSpace(profile.DisplayName) &&
                    profile.DisplayName.Length <= 100 &&
                    !string.IsNullOrWhiteSpace(profile.University) &&
                    profile.University.Length <= 255;
            case 2:
                return profile.UserSkills.Count > 0 &&
                    profile.UserSkills.All(s => !string.IsNullOrWhiteSpace(s.Skill.Name) && s.Skill.Name.Length <= 100);
            case 3:
                return OnboardingAllowedRoles.Names.Contains(profile.PreferredRole ?? string.Empty, StringComparer.Ordinal);
            case 4:
                return !string.IsNullOrWhiteSpace(profile.ProfessionalInterests) &&
                    !string.IsNullOrWhiteSpace(profile.LearningGoals);
            case 5:
                return profile.AvailableHoursPerWeek is >= 1 and <= 40;
            case 6:
                return !string.IsNullOrWhiteSpace(profile.PreviousProjectsAndContribution) &&
                    !string.IsNullOrWhiteSpace(profile.TechnologiesToLearn);
            case 7:
                return OnboardingStep7Request.IsWebUrlOrEmpty(profile.GithubUrl) &&
                    OnboardingStep7Request.IsWebUrlOrEmpty(profile.PersonalWebsiteUrl) &&
                    (profile.GithubUrl?.Length ?? 0) <= 1000 &&
                    (profile.PersonalWebsiteUrl?.Length ?? 0) <= 1000;
            default:
                return false;
        }
    }
}
