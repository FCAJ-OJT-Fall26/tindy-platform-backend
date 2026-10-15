using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface ICatalogRepository
{
    Task<IReadOnlyList<SkillCategory>> GetActiveSkillCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Skill>> GetActiveSkillsAsync(
        Guid? categoryId,
        string? normalizedSearch,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfessionalRole>> GetActiveProfessionalRolesAsync(
        string? normalizedSearch,
        CancellationToken cancellationToken = default);
}
