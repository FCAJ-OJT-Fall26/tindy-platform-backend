
using Tindy.Application.DTOs.Authentication;

namespace Tindy.Application.Interfaces.Services;

public interface IAuthService
{
    // ===================================
    // LOCAL REGISTER
    // ===================================
    Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    // ===================================
    // LOCAL LOGIN
    // ===================================
    Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}
