
using Tindy.Application.Constants;
using Tindy.Application.DTOs.Authentication;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class AuthService : IAuthService
{
    private readonly IAuthRepository _authRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    // ===================================
    // 1. CONSTRUCTOR
    // ===================================
    public AuthService(IAuthRepository authRepository, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _authRepository = authRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    // ===================================
    // 2. LOCAL REGISTER
    // ===================================
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Request validation is enforced by [ApiController].
        // AuthService receives a validated request.
        var displayName = request.DisplayName!.Trim();
        var university = request.University!.Trim();
        var email = request.Email!.Trim();

        var emailNormalized = email.ToLowerInvariant();

        // ===================================
        // 3. CHECK DUPLICATE EMAIL
        // ===================================
        var emailExists =
            await _authRepository.EmailExistsAsync(emailNormalized, cancellationToken);

        if (emailExists)
        {
            throw new EmailAlreadyExistsException();
        }

        // ===================================
        // 4. FIND DEFAULT MEMBER ROLE
        // ===================================
        var memberRole =
            await _authRepository.GetRoleByNameAsync(RoleNames.Member, cancellationToken);

        if (memberRole is null)
        {
            throw new InvalidOperationException("Default member role was not found. " + "Run database seeding before registration.");
        }

        // ===================================
        // 5. HASH PASSWORD
        // ===================================
        var passwordHash = _passwordHasher.HashPassword(request.Password!);

        // ===================================
        // 6. PREPARE ENTITY VALUES
        // ===================================
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // ===================================
        // 7. CREATE USER AGGREGATE
        // ===================================
        var user = new User
        {
            UserId = userId,

            Username = null,
            UsernameNormalized = null,

            // Local email is the initial private contact email.
            // Local registration does not verify ownership yet.
            PrimaryEmail = email,
            PrimaryEmailNormalized = emailNormalized,
            PrimaryEmailVerifiedAt = null,

            Status = "active",
            IsOnboardingCompleted = false,

            CreatedAt = now,

            // Local Account Credentials
            Credential = new UserCredential
            {
                UserCredentialId = Guid.NewGuid(),
                UserId = userId,

                Email = email,
                EmailNormalized = emailNormalized,

                PasswordHash = passwordHash,

                EmailVerifiedAt = null,

                CreatedAt = now
            },

            // Initial User Profile
            Profile = new UserProfile
            {
                UserId = userId,

                DisplayName = displayName,
                University = university,

                // AI Summary - Not implemented
                Bio = null,

                // Filled during Onboarding Step 5
                AvailableHoursPerWeek = null,

                IsPublic = true,

                CreatedAt = now
            },

            // Default System Role
            UserRoles = new List<UserRole>
            {
                new UserRole
                {
                    UserId = userId,
                    RoleId = memberRole.RoleId
                }
            }
        };

        // ===================================
        // 8. SAVE USER AGGREGATE
        // ===================================
        await _authRepository.AddUserAsync(user, cancellationToken);

        await _authRepository.SaveChangesAsync(cancellationToken);

        // ===================================
        // 9. ISSUE JWT ACCESS TOKEN
        // ===================================
        var roles = new[] { RoleNames.Member };

        var token = _jwtTokenService.GenerateAccessToken(userId, roles);

        // ===================================
        // 10. BUILD REGISTER RESPONSE
        // ===================================
        return new RegisterResponse(AccessToken: token.AccessToken, TokenType: token.TokenType, ExpiresIn: token.ExpiresIn, User: new RegisterUserResponse(UserId: userId, DisplayName: displayName, University: university, Email: email, Username: null, Status: "active", Roles: roles, IsOnboardingCompleted: false), RedirectTo: "/onboarding");
    }


    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ===================================
        // 1. NORMALIZE IDENTIFIER
        // ===================================
        var identifier = request.Identifier!.Trim();

        var normalizedIdentifier =
            identifier.ToLowerInvariant();

        // ===================================
        // 2. FIND USER BY EMAIL OR USERNAME
        // ===================================
        User? user;

        if (normalizedIdentifier.Contains('@'))
        {
            // Login with email
            user = await _authRepository.GetByEmailAsync(normalizedIdentifier, cancellationToken);
        }
        else
        {
            // Login with username
            user = await _authRepository.GetByUsernameAsync(normalizedIdentifier, cancellationToken);
        }

        // ===================================
        // 3. VERIFY PASSWORD
        // ===================================
        if (user?.Credential is null)
        {
            throw new InvalidCredentialsException();
        }

        var isPasswordValid = _passwordHasher.VerifyPassword(request.Password!, user.Credential.PasswordHash);

        if (!isPasswordValid)
        {
            throw new InvalidCredentialsException();
        }

        // ===================================
        // 4. CHECK ACCOUNT STATUS
        // ===================================
        var isActive = string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase);

        var isDeleted = user.DeletedAt.HasValue;

        if (!isActive || isDeleted)
        {
            throw new InvalidCredentialsException();
        }

        // ===================================
        // 5. LOAD SYSTEM ROLES
        // ===================================
        var roles = user.UserRoles
            .Select(x => x.Role.RoleName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (roles.Length == 0)
        {
            throw new InvalidOperationException("Authenticated user has no assigned system role.");
        }

        // ===================================
        // 6. CHECK INITIAL PROFILE
        // ===================================
        if (user.Profile is null)
        {
            throw new InvalidOperationException("Authenticated user has no profile.");
        }

        // ===================================
        // 7. GENERATE JWT ACCESS TOKEN
        // ===================================
        var token = _jwtTokenService.GenerateAccessToken(user.UserId, roles);

        // ===================================
        // 8. CHECK ONBOARDING STATUS
        // ===================================
        var isOnboardingCompleted =
            user.IsOnboardingCompleted;

        var redirectTo = isOnboardingCompleted
            ? "/discover"
            : "/onboarding";

        // ===================================
        // 9. BUILD LOGIN RESPONSE
        // ===================================
        return new LoginResponse(AccessToken: token.AccessToken, TokenType: token.TokenType, ExpiresIn: token.ExpiresIn, User: new LoginUserResponse(UserId: user.UserId, DisplayName: user.Profile.DisplayName, University: user.Profile.University ?? string.Empty, Email: user.Credential.Email, Username: user.Username, Status: user.Status, Roles: roles, IsOnboardingCompleted: isOnboardingCompleted), RedirectTo: redirectTo);
    }

}
