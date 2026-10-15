namespace Tindy.Application.DTOs.Catalog;

public sealed record SkillCategoryResponse(
    Guid CategoryId,
    string Name,
    string? Description,
    int DisplayOrder);
