using Microsoft.EntityFrameworkCore;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authorization;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class OnboardingAccessReader : IOnboardingAccessReader
{
    private readonly TindyDbContext _context;

    public OnboardingAccessReader(TindyDbContext context)
    {
        _context = context;
    }

    public async Task<OnboardingAccessStatus> GetStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Read the live DB state for each authorized request.
        // Do not read an onboarding flag from JWT claims or client storage.
        var user = await _context.Users
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Status,
                x.DeletedAt,
                x.IsOnboardingCompleted
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null ||
            user.DeletedAt is not null ||
            !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return OnboardingAccessStatus.InactiveOrMissing;
        }

        return user.IsOnboardingCompleted
            ? OnboardingAccessStatus.Completed
            : OnboardingAccessStatus.Incomplete;
    }
}
