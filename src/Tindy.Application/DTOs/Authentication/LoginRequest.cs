
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Tindy.Application.DTOs.Authentication;

public sealed class LoginRequest : IValidatableObject
{
    [Required]
    public string? Identifier { get; set; }

    [Required]
    public string? Password { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // ===================================
        // 1. IDENTIFIER VALIDATION
        // ===================================
        if (Identifier is not null &&
            (string.IsNullOrWhiteSpace(Identifier) ||
             Identifier.Trim().Length > 255))
        {
            yield return new ValidationResult("Identifier must contain 1–255 characters.", new[] { nameof(Identifier) });
        }

        // ===================================
        // 2. PASSWORD VALIDATION
        // ===================================
        if (Password is not null &&
            (string.IsNullOrWhiteSpace(Password) ||
             Encoding.UTF8.GetByteCount(Password) > 72))
        {
            yield return new ValidationResult("Password is invalid.", new[] { nameof(Password) });
        }
    }
}
