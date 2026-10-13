using Tindy.Application.DTOs.Onboarding;

namespace Tindy.Application.Interfaces.Services;

public interface IOnboardingService
{
    Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task<OnboardingDraftResponse> GetDraftAsync(Guid userId, CancellationToken ct = default);
    Task<OnboardingSaveResponse> SaveStep1Async(Guid userId, OnboardingStep1Request request, CancellationToken ct = default);
    Task<OnboardingSaveResponse> SaveStep2Async(Guid userId, OnboardingStep2Request request, CancellationToken ct = default);
    Task<OnboardingSaveResponse> SaveStep3Async(Guid userId, OnboardingStep3Request request, CancellationToken ct = default);
    Task<OnboardingSaveResponse> SaveStep4Async(Guid userId, OnboardingStep4Request request, CancellationToken ct = default);
    Task<OnboardingSaveResponse> SaveStep5Async(Guid userId, OnboardingStep5Request request, CancellationToken ct = default);
    Task<OnboardingSaveResponse> SaveStep6Async(Guid userId, OnboardingStep6Request request, CancellationToken ct = default);
    Task<OnboardingSaveResponse> SaveStep7Async(Guid userId, OnboardingStep7Request request, CancellationToken ct = default);
    Task<OnboardingCompleteResponse> CompleteAsync(Guid userId, OnboardingCompleteRequest request, CancellationToken ct = default);
}
