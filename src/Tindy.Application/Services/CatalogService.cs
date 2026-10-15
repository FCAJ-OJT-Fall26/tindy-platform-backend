using System.Text.RegularExpressions;
using Tindy.Application.DTOs.Catalog;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class CatalogService : ICatalogService
{
    private readonly ICatalogRepository _catalogRepository;

    public CatalogService(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<IReadOnlyList<SkillCategoryResponse>> GetSkillCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await _catalogRepository.GetActiveSkillCategoriesAsync(cancellationToken);
        return categories.Select(x => new SkillCategoryResponse(
            x.CategoryId, x.Name, x.Description, x.DisplayOrder)).ToArray();
    }

    public async Task<IReadOnlyList<SkillResponse>> GetSkillsAsync(
        Guid? categoryId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var skills = await _catalogRepository.GetActiveSkillsAsync(
            categoryId, NormalizeSearch(search), cancellationToken);

        return skills.Select(x => new SkillResponse(x.SkillId, x.Name)).ToArray();
    }

    public async Task<IReadOnlyList<ProfessionalRoleResponse>> GetProfessionalRolesAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var roles = await _catalogRepository.GetActiveProfessionalRolesAsync(
            NormalizeSearch(search), cancellationToken);

        return roles.Select(x => new ProfessionalRoleResponse(
            x.ProfessionalRoleId, x.Name, x.Description, x.DisplayOrder)).ToArray();
    }

    // Same normalization convention used by the catalog seed data.
    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;

        return Regex.Replace(search.Trim(), @"\s+", " ").ToLowerInvariant();
    }
}
