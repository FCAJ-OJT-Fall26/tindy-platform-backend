
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Tindy.Application.DTOs.Authentication;

public sealed class RegisterRequest : IValidatableObject
{
    [Required]
    public string? DisplayName { get; set; }

    [Required]
    public string? University { get; set; }

    [Required]
    public string? Email { get; set; }

    [Required]
    public string? Password { get; set; }

    public bool AcceptTerms { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        // ===================================
        // 1. FULL NAME
        // ===================================
        if (DisplayName is not null &&
            (string.IsNullOrWhiteSpace(DisplayName) ||
             DisplayName.Trim().Length > 100))
        {
            yield return new ValidationResult(
                "Full name must contain 1–100 characters.",
                new[] { nameof(DisplayName) });
        }

        // ===================================
        // 2. UNIVERSITY
        // ===================================
        if (University is not null &&
            (string.IsNullOrWhiteSpace(University) ||
             University.Trim().Length > 255))
        {
            yield return new ValidationResult(
                "University must contain 1–255 characters.",
                new[] { nameof(University) });
        }

        // ===================================
        // 3. EMAIL
        // ===================================
        if (!string.IsNullOrWhiteSpace(Email))
        {
            var normalizedInput = Email.Trim();

            if (normalizedInput.Length > 255 ||
                !new EmailAddressAttribute()
                    .IsValid(normalizedInput))
            {
                yield return new ValidationResult(
                    "Email is invalid or too long.",
                    new[] { nameof(Email) });
            }
        }

        // ===================================
        // 4. PASSWORD
        // ===================================
        if (!string.IsNullOrEmpty(Password))
        {
            var hasUpper = Password.Any(char.IsUpper);
            var hasLower = Password.Any(char.IsLower);
            var hasDigit = Password.Any(char.IsDigit);

            var hasSpecial = Password.Any(
                c => !char.IsLetterOrDigit(c) &&
                     !char.IsWhiteSpace(c));

            var isValid = Password.Length >= 8
                && Encoding.UTF8.GetByteCount(Password) <= 72
                && hasUpper
                && hasLower
                && hasDigit
                && hasSpecial;

            if (!isValid)
            {
                yield return new ValidationResult(
                    "Password must have at least 8 characters, " +
                    "uppercase, lowercase, digit and special " +
                    "character, with a maximum of 72 UTF-8 bytes.",
                    new[] { nameof(Password) });
            }
        }

        // ===================================
        // 5. TERMS ACCEPTANCE
        // ===================================
        if (!AcceptTerms)
        {
            yield return new ValidationResult(
                "You must accept the terms.",
                new[] { nameof(AcceptTerms) });
        }
    }
}
