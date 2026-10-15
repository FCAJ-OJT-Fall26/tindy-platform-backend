using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Api.Authorization;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Route("api/catalog")]
[Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
public sealed class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public CatalogController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    // GET /api/catalog/skill-categories
    [HttpGet("skill-categories")]
    public async Task<IActionResult> GetSkillCategories(CancellationToken cancellationToken)
    {
        var data = await _catalogService.GetSkillCategoriesAsync(cancellationToken);
        return Ok(new { success = true, message = "Skill categories retrieved successfully.", data });
    }

    // GET /api/catalog/skills?category_id=<uuid>&search=python
    [HttpGet("skills")]
    public async Task<IActionResult> GetSkills(
        [FromQuery(Name = "category_id")] Guid? categoryId,
        [FromQuery(Name = "search"), StringLength(100)] string? search,
        CancellationToken cancellationToken)
    {
        var data = await _catalogService.GetSkillsAsync(categoryId, search, cancellationToken);
        return Ok(new { success = true, message = "Skills retrieved successfully.", data });
    }

    // GET /api/catalog/professional-roles?search=backend
    [HttpGet("professional-roles")]
    public async Task<IActionResult> GetProfessionalRoles(
        [FromQuery(Name = "search"), StringLength(100)] string? search,
        CancellationToken cancellationToken)
    {
        var data = await _catalogService.GetProfessionalRolesAsync(search, cancellationToken);
        return Ok(new { success = true, message = "Professional roles retrieved successfully.", data });
    }
}
