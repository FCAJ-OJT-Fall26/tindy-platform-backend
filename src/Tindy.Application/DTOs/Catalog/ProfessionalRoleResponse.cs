namespace Tindy.Application.DTOs.Catalog;

public sealed record ProfessionalRoleResponse(
    Guid ProfessionalRoleId,
    string Name,
    string? Description,
    int DisplayOrder);
