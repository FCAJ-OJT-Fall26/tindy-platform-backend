
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
    public AuthService(
        IAuthRepository authRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _authRepository = authRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    // ===================================
    // 2. LOCAL REGISTER
    // ===================================
    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
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
            await _authRepository.EmailExistsAsync(
                emailNormalized,
                cancellationToken);

        if (emailExists)
        {
            throw new EmailAlreadyExistsException();
        }

        // ===================================
        // 4. FIND DEFAULT MEMBER ROLE
        // ===================================
        var memberRole =
            await _authRepository.GetRoleByNameAsync(
                RoleNames.Member,
                cancellationToken);

        if (memberRole is null)
        {
            throw new InvalidOperationException(
                "Default member role was not found. " +
                "Run database seeding before registration.");
        }

        // ===================================
        // 5. HASH PASSWORD
        // ===================================
        var passwordHash = _passwordHasher.HashPassword(
            request.Password!);

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
        await _authRepository.AddUserAsync(
            user,
            cancellationToken);

        await _authRepository.SaveChangesAsync(
            cancellationToken);

        // ===================================
        // 9. ISSUE JWT ACCESS TOKEN
        // ===================================
        var roles = new[] { RoleNames.Member };

        var token = _jwtTokenService.GenerateAccessToken(
            userId,
            roles);

        // ===================================
        // 10. BUILD REGISTER RESPONSE
        // ===================================
        return new RegisterResponse(
            AccessToken: token.AccessToken,
            TokenType: token.TokenType,
            ExpiresIn: token.ExpiresIn,

            User: new RegisterUserResponse(
                UserId: userId,
                DisplayName: displayName,
                University: university,
                Email: email,
                Username: null,
                Status: "active",
                Roles: roles,
                IsOnboardingCompleted: false
            ),

            RedirectTo: "/onboarding"
        );
    }
}
