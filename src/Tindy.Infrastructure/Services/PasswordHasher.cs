
using System.Text;
using Tindy.Application.Interfaces.Services;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Tindy.Infrastructure.Services;

public sealed class PasswordHasher : IPasswordHasher
{
    // ===================================
    // 1. BCRYPT CONFIGURATION
    // ===================================

    private const int WorkFactor = 12;

    // BCrypt standard password size limit
    private const int MaxPasswordBytes = 72;

    // ===================================
    // 2. HASH PASSWORD
    // ===================================

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException(
                "Password cannot be empty.",
                nameof(password));
        }

        if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
        {
            throw new ArgumentException(
                "Password exceeds the 72-byte BCrypt limit.",
                nameof(password));
        }

        return BCryptNet.HashPassword(
            password,
            workFactor: WorkFactor);
    }

    // ===================================
    // 3. VERIFY PASSWORD
    // ===================================

    public bool VerifyPassword(
        string password,
        string passwordHash)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
        {
            return false;
        }

        try
        {
            return BCryptNet.Verify(
                password,
                passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
