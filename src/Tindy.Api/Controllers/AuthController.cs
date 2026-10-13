
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Authentication;
using Tindy.Application.Interfaces.Services;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Api.Authorization;

namespace Tindy.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // ===================================
    // POST /api/auth/register
    // ===================================
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            success = true,
            message = "Account registered successfully.",
            data = result
        });
    }


    // ===================================
    // POST /api/auth/login
    // ===================================
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Logged in successfully.",
            data = result
        });
    }

    // ===================================
    // POST /api/auth/logout
    // ===================================
    [HttpPost("logout")]
    [Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Logout()
    {
        return Ok(new
        {
            success = true,
            message = "Logged out successfully.",
            data = (object?)null
        });
    }

}
