using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class ExperienceService : IExperienceService
{
    private readonly IExperienceRepository _repository;

    public ExperienceService(IExperienceRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<ExperienceResponse>> ListMyExperiencesAsync(Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireEligibleOwnerAsync(callerUserId, cancellationToken);
        return await _repository.ListOwnedAsync(callerUserId, cancellationToken);
    }

    public async Task<ExperienceResponse> GetMyExperienceAsync(Guid callerUserId, Guid experienceId, CancellationToken cancellationToken = default)
    {
        await RequireEligibleOwnerAsync(callerUserId, cancellationToken);
        return await _repository.GetOwnedAsync(callerUserId, experienceId, cancellationToken)
            ?? throw new NotFoundException("Experience was not found.");
    }

    public Task<ExperienceResponse> CreateAsync(Guid callerUserId, ExperienceUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = ExperienceInputValidator.Normalize(request);
        return _repository.ExecuteInTransactionAsync(callerUserId, async token =>
        {
            await RequireEligibleOwnerAsync(callerUserId, token);
            await EnsureAddedSkillsActiveAsync(normalized.SkillIds, token);
            var experience = new ProfileExperience
            {
                ExperienceId = Guid.NewGuid(),
                UserId = callerUserId,
                ProjectName = normalized.ProjectName,
                RoleTitle = normalized.RoleTitle,
                Description = normalized.Description,
                RepoUrl = normalized.RepoUrl,
                StartDate = normalized.StartDate,
                EndDate = normalized.EndDate,
                IsOngoing = normalized.IsOngoing,
                CreatedAt = DateTime.UtcNow,
                ExperienceSkills = normalized.SkillIds.Select(id => new ProfileExperienceSkill { SkillId = id }).ToList()
            };
            _repository.Add(experience);
            await _repository.SaveChangesAsync(token);
            return await _repository.GetOwnedAsync(callerUserId, experience.ExperienceId, token)
                ?? throw new NotFoundException("Experience was not found.");
        }, cancellationToken);
    }

    public Task<ExperienceResponse> UpdateAsync(Guid callerUserId, Guid experienceId, ExperienceUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = ExperienceInputValidator.Normalize(request);
        return _repository.ExecuteInTransactionAsync(callerUserId, async token =>
        {
            await RequireEligibleOwnerAsync(callerUserId, token);
            var experience = await _repository.FindOwnedForUpdateAsync(callerUserId, experienceId, token)
                ?? throw new NotFoundException("Experience was not found.");

            var linked = experience.ExperienceSkills.Select(item => item.SkillId).ToHashSet();
            var added = normalized.SkillIds.Where(id => !linked.Contains(id)).ToArray();
            await EnsureAddedSkillsActiveAsync(added, token);

            experience.ProjectName = normalized.ProjectName;
            experience.RoleTitle = normalized.RoleTitle;
            experience.Description = normalized.Description;
            experience.RepoUrl = normalized.RepoUrl;
            experience.StartDate = normalized.StartDate;
            experience.EndDate = normalized.EndDate;
            experience.IsOngoing = normalized.IsOngoing;
            experience.UpdatedAt = DateTime.UtcNow;
            _repository.SyncSkills(experience, normalized.SkillIds);
            await _repository.SaveChangesAsync(token);
            return await _repository.GetOwnedAsync(callerUserId, experienceId, token)
                ?? throw new NotFoundException("Experience was not found.");
        }, cancellationToken);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid experienceId, CancellationToken cancellationToken = default)
    {
        await _repository.ExecuteInTransactionAsync(callerUserId, async token =>
        {
            await RequireEligibleOwnerAsync(callerUserId, token);
            var experience = await _repository.FindOwnedForUpdateAsync(callerUserId, experienceId, token)
                ?? throw new NotFoundException("Experience was not found.");
            _repository.Remove(experience);
            await _repository.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    private async Task RequireEligibleOwnerAsync(Guid callerUserId, CancellationToken cancellationToken)
    {
        if (callerUserId == Guid.Empty || !await _repository.IsEligibleOwnerAsync(callerUserId, cancellationToken))
            throw new NotFoundException("Experience was not found.");
    }

    private async Task EnsureAddedSkillsActiveAsync(IReadOnlyCollection<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return;
        var active = await _repository.GetActiveSkillIdsAsync(ids, token);
        if (ids.Except(active).Any())
            throw new BusinessRuleException("One or more selected skills do not exist or are inactive.");
    }
}
