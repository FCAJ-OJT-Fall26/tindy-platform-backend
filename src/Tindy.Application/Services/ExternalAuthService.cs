using System.Net.Mail;
using Tindy.Application.Constants;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Services;

public sealed class ExternalAuthService : IExternalAuthService
{
    private readonly IExternalAuthRepository _repository;

    public ExternalAuthService(IExternalAuthRepository repository)
    {
        _repository = repository;
    }

    public async Task<ExternalSignInResult> ResolveSignInAsync(
        AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var data = ValidateIdentity(identity);

        // Use the provider's stable user ID, never its mutable username/email.
        var existing = await _repository.GetByProviderIdentityAsync(
            data.Provider, data.ProviderUserId, cancellationToken);

        if (existing is not null)
        {
            return ResultFor(existing.User, isNewUser: false);
        }

        // Never merge two accounts just because the provider returns the same email.
        // Reserved email conflicts must be resolved by explicit account linking in STEP 05.
        if (data.NormalizedEmail is not null &&
            await _repository.EmailReservedAsync(data.NormalizedEmail, cancellationToken))
        {
            throw new ConflictException("An existing Tindy account must be explicitly linked before using this provider.");
        }

        var memberRole = await _repository.GetRoleByNameAsync(RoleNames.Member, cancellationToken)
            ?? throw new InvalidOperationException("Default member role is missing. Run database seeding.");

        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var user = new User
        {
            UserId = userId,
            Username = null,
            UsernameNormalized = null,
            PrimaryEmail = data.VerifiedEmail,
            PrimaryEmailNormalized = data.NormalizedEmail,
            PrimaryEmailVerifiedAt = data.VerifiedEmail is null ? null : now,
            Status = "active",
            IsOnboardingCompleted = false,
            CreatedAt = now,
            // OAuth-only: DO NOT create a UserCredential or a synthetic password.
            Profile = new UserProfile
            {
                UserId = userId,
                DisplayName = data.DisplayName,
                University = null,
                Bio = null,
                AvatarUrl = data.AvatarUrl,
                CreatedAt = now
            },
            UserRoles = new List<UserRole>
            {
                new() { UserId = userId, RoleId = memberRole.RoleId }
            },
            ExternalAuthAccounts = new List<ExternalAuthAccount>
            {
                new()
                {
                    ExternalAuthAccountId = Guid.NewGuid(),
                    UserId = userId,
                    Provider = data.Provider,
                    ProviderUserId = data.ProviderUserId,
                    ProviderUsername = data.ProviderUsername,
                    ProviderEmail = data.ProviderEmail,
                    CreatedAt = now
                }
            }
        };

        await _repository.AddUserAsync(user, cancellationToken);
        // In EF Core, saving the aggregate in one SaveChanges call is transactional.
        // A racing duplicate is rejected by the database unique indexes.
        await _repository.SaveChangesAsync(cancellationToken);

        // Role was loaded AsNoTracking: do not attach it to the Added User graph.
        // Returning the seeded role name avoids a duplicate INSERT into roles.
        return new ExternalSignInResult(userId, true, data.DisplayName,
            data.VerifiedEmail, new[] { RoleNames.Member }, false);
    }

    public async Task<bool> LinkAsync(
        Guid currentUserId,
        AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new ArgumentException("A valid current Tindy user is required.", nameof(currentUserId));
        }

        var data = ValidateIdentity(identity);
        var user = await _repository.GetUserByIdAsync(currentUserId, cancellationToken)
            ?? throw new InvalidCredentialsException();
        EnsureActiveUser(user);
        _ = ResultFor(user, isNewUser: false); // Require profile and system role.

        var otherOwner = await _repository.GetByProviderIdentityAsync(
            data.Provider, data.ProviderUserId, cancellationToken);

        if (otherOwner is not null)
        {
            if (otherOwner.UserId != currentUserId)
            {
                throw new ConflictException("This external identity is linked to another Tindy account.");
            }

            return false; // Repeated link of the exact same identity is idempotent.
        }

        var sameProvider = await _repository.GetByUserAndProviderAsync(
            currentUserId, data.Provider, cancellationToken);

        if (sameProvider is not null)
        {
            throw new ConflictException("A different identity from this provider is already linked.");
        }

        // Both operations must commit together. The repository conditionally fills
        // avatar_url in SQL so concurrent provider links cannot overwrite an existing image.
        await _repository.AddExternalAccountAndFillAvatarAsync(new ExternalAuthAccount
        {
            ExternalAuthAccountId = Guid.NewGuid(),
            UserId = currentUserId,
            Provider = data.Provider,
            ProviderUserId = data.ProviderUserId,
            ProviderUsername = data.ProviderUsername,
            ProviderEmail = data.ProviderEmail,
            CreatedAt = DateTime.UtcNow
        }, data.AvatarUrl, cancellationToken);
        return true;
    }

    private static ExternalSignInResult ResultFor(User user, bool isNewUser)
    {
        EnsureActiveUser(user);
        var profile = user.Profile
            ?? throw new InvalidOperationException("Authenticated user has no profile.");

        var roles = user.UserRoles
            .Select(x => x.Role?.RoleName)
            .OfType<string>()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (roles.Length == 0)
        {
            throw new InvalidOperationException("Authenticated user has no assigned system role.");
        }

        return new ExternalSignInResult(user.UserId, isNewUser,
            profile.DisplayName, user.PrimaryEmail, roles, user.IsOnboardingCompleted);
    }

    private static void EnsureActiveUser(User user)
    {
        if (user.DeletedAt.HasValue ||
            !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidCredentialsException();
        }
    }

    private static ValidatedIdentity ValidateIdentity(AuthenticatedExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var provider = ExternalAuthProviders.Normalize(identity.Provider);
        var providerUserId = identity.ProviderUserId?.Trim();

        if (string.IsNullOrWhiteSpace(providerUserId) || providerUserId.Length > 255)
        {
            throw new ArgumentException("Invalid provider user ID.", nameof(identity));
        }

        // Email is metadata, not the identity key. Store only plausible provider email.
        var email = TrimOrNull(identity.Email);
        if (email is not null &&
            (email.Length > 255 || !MailAddress.TryCreate(email, out var parsed) ||
             !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)))
        {
            email = null;
        }

        // Do not claim ownership of unverified or absent provider email.
        var verifiedEmail = identity.EmailVerified ? email : null;
        var normalizedEmail = verifiedEmail?.ToLowerInvariant();
        var username = Crop(TrimOrNull(identity.ProviderUsername), 255);
        var displayName = Crop(TrimOrNull(identity.DisplayName), 100)
            ?? Crop(username, 100)
            ?? "Tindy Member";

        return new ValidatedIdentity(provider, providerUserId,
            displayName, username, email, verifiedEmail, normalizedEmail,
            OAuthAvatarUrl.Normalize(identity.AvatarUrl));
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? Crop(string? value, int length) =>
        value is { Length: > 0 } ? value[..Math.Min(value.Length, length)] : null;

    private sealed record ValidatedIdentity(
        string Provider, string ProviderUserId, string DisplayName,
        string? ProviderUsername, string? ProviderEmail,
        string? VerifiedEmail, string? NormalizedEmail, string? AvatarUrl);
}
