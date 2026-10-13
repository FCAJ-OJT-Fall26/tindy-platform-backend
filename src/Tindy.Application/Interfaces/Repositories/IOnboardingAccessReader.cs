using Tindy.Application.Models.Authorization;

namespace Tindy.Application.Interfaces.Repositories;

public interface IOnboardingAccessReader
{
    Task<OnboardingAccessStatus> GetStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
