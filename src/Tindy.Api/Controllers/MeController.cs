using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Onboarding;
using Tindy.Api.Authorization;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
[Route("api/v1/me")]
public sealed class MeController : ControllerBase
{
    private readonly IOnboardingService _onboardingService;

    public MeController(IOnboardingService onboardingService) =>
        _onboardingService = onboardingService;

    private Guid CurrentUserId
    {
        get
        {
            if (Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty)
                return id;
            throw new InvalidCredentialsException();
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var data = await _onboardingService.GetCurrentUserAsync(CurrentUserId, ct);
        return Ok(new { success = true, message = "Lấy thông tin tài khoản thành công", data });
    }

    [HttpGet("onboarding")]
    public async Task<IActionResult> GetOnboarding(CancellationToken ct)
    {
        var data = await _onboardingService.GetDraftAsync(CurrentUserId, ct);
        return Ok(new { success = true, message = "Lấy tiến độ onboarding thành công", data });
    }

    [HttpPut("onboarding/1")]
    public Task<IActionResult> SaveStep1([FromBody] OnboardingStep1Request req, CancellationToken ct) =>
        SaveAsync(_onboardingService.SaveStep1Async(CurrentUserId, req, ct));

    [HttpPut("onboarding/2")]
    public Task<IActionResult> SaveStep2([FromBody] OnboardingStep2Request req, CancellationToken ct) =>
        SaveAsync(_onboardingService.SaveStep2Async(CurrentUserId, req, ct));

    [HttpPut("onboarding/3")]
    public Task<IActionResult> SaveStep3([FromBody] OnboardingStep3Request req, CancellationToken ct) =>
        SaveAsync(_onboardingService.SaveStep3Async(CurrentUserId, req, ct));

    [HttpPut("onboarding/4")]
    public Task<IActionResult> SaveStep4([FromBody] OnboardingStep4Request req, CancellationToken ct) =>
        SaveAsync(_onboardingService.SaveStep4Async(CurrentUserId, req, ct));

    [HttpPut("onboarding/5")]
    public Task<IActionResult> SaveStep5([FromBody] OnboardingStep5Request req, CancellationToken ct) =>
        SaveAsync(_onboardingService.SaveStep5Async(CurrentUserId, req, ct));

    [HttpPut("onboarding/6")]
    public Task<IActionResult> SaveStep6([FromBody] OnboardingStep6Request req, CancellationToken ct) =>
        SaveAsync(_onboardingService.SaveStep6Async(CurrentUserId, req, ct));

    [HttpPut("onboarding/7")]
    public Task<IActionResult> SaveStep7([FromBody] OnboardingStep7Request req, CancellationToken ct) =>
        SaveAsync(_onboardingService.SaveStep7Async(CurrentUserId, req, ct));

    private async Task<IActionResult> SaveAsync(Task<OnboardingSaveResponse> operation)
    {
        var data = await operation;
        return Ok(new { success = true, message = "Đã lưu bước onboarding", data });
    }

    [HttpPost("onboarding/complete")]
    public async Task<IActionResult> Complete([FromBody] OnboardingCompleteRequest req, CancellationToken ct)
    {
        var data = await _onboardingService.CompleteAsync(CurrentUserId, req, ct);
        return Ok(new { success = true, message = "Hoàn tất hồ sơ thành công", data });
    }
}
