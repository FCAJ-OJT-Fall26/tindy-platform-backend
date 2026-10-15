using Tindy.Application.DTOs.Catalog;

namespace Tindy.Application.Interfaces.Services;

public interface ICatalogService
{
    Task<IReadOnlyList<SkillCategoryResponse>> GetSkillCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillResponse>> GetSkillsAsync(
        Guid? categoryId,
        string? search,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfessionalRoleResponse>> GetProfessionalRolesAsync(
        string? search,
        CancellationToken cancellationToken = default);
}
