using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class CatalogRepository : ICatalogRepository
{
    private readonly TindyDbContext _context;

    public CatalogRepository(TindyDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SkillCategory>> GetActiveSkillCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.SkillCategories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Skill>> GetActiveSkillsAsync(
        Guid? categoryId,
        string? normalizedSearch,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Skills
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (categoryId.HasValue)
        {
            // EXISTS instead of JOIN: even a skill that belongs to multiple
            // categories is returned only once. Inactive categories are hidden.
            query = query.Where(skill => skill.SkillCategorySkills.Any(link =>
                link.CategoryId == categoryId.Value && link.SkillCategory.IsActive));
        }

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            // Matching the persisted name_normalized avoids case-sensitive search.
            // Contains treats % and _ as literal text (not user-provided SQL wildcards).
            query = query.Where(x => x.NameNormalized.Contains(normalizedSearch));
        }

        return await query
            .OrderBy(x => x.NameNormalized)
            .ThenBy(x => x.SkillId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProfessionalRole>> GetActiveProfessionalRolesAsync(
        string? normalizedSearch,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ProfessionalRoles
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            query = query.Where(x => x.NameNormalized.Contains(normalizedSearch));
        }

        return await query
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }
}
