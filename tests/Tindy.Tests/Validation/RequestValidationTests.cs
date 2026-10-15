using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Tindy.Application.DTOs.Authentication;
using Tindy.Application.DTOs.Onboarding;
using Xunit;

namespace Tindy.Tests.Validation;

public sealed class RequestValidationTests
{
    [Fact]
    public void RegisterRequest_ValidInput_Passes()
    {
        var request = ValidRegister();
        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData("short", "password", false)]
    [InlineData("PASSWORD123!", "password", false)]
    [InlineData("Password123!", "password", true)]
    public void RegisterRequest_PasswordRules_AreEnforced(string password, string field, bool expectedValid = false)
    {
        var request = ValidRegister();
        request.Password = password;

        var errors = Validate(request);
        if (expectedValid)
            Assert.Empty(errors);
        else
            Assert.Contains(errors, x => x.MemberNames.Contains(field, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegisterRequest_Rejects_MissingTerms_InvalidEmail_AndOverlongPassword()
    {
        var request = ValidRegister();
        request.AcceptTerms = false;
        request.Email = "not-an-email";
        request.Password = new string('a', 73) + "A1!";

        var errors = Validate(request);
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(RegisterRequest.AcceptTerms)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(RegisterRequest.Email)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(RegisterRequest.Password)));
    }

    [Fact]
    public void LoginRequest_RejectsBlankIdentifier()
    {
        var errors = Validate(new LoginRequest { Identifier = "  ", Password = "Password123!" });
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(LoginRequest.Identifier)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    public void Availability_Rejects_OutOfRange(int hours)
    {
        var errors = Validate(new OnboardingStep5Request { AvailableHoursPerWeek = hours });
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Skills_Rejects_EmptyList()
    {
        Assert.NotEmpty(Validate(new OnboardingStep2Request { SkillIds = [] }));
    }

    [Fact]
    public void Skills_Rejects_EmptyGuid_And_OverThirtySelections()
    {
        Assert.NotEmpty(Validate(new OnboardingStep2Request { SkillIds = [Guid.Empty] }));
        Assert.NotEmpty(Validate(new OnboardingStep2Request
        {
            SkillIds = Enumerable.Range(0, 31).Select(_ => Guid.NewGuid()).ToList()
        }));
        var validId = Guid.NewGuid();
        Assert.Empty(Validate(new OnboardingStep2Request { SkillIds = [validId, validId] }));
    }

    [Fact]
    public void PreferredRole_RequiresNonEmptyGuid()
    {
        Assert.NotEmpty(Validate(new OnboardingStep3Request()));
        Assert.NotEmpty(Validate(new OnboardingStep3Request { PreferredRoleId = Guid.Empty }));
        Assert.Empty(Validate(new OnboardingStep3Request { PreferredRoleId = Guid.NewGuid() }));
    }

    [Fact]
    public void Step7_AllowsBothLinksNull()
    {
        Assert.Empty(Validate(new OnboardingStep7Request { GithubUrl = null, PersonalWebsiteUrl = null }));
    }

    [Fact]
    public void Complete_BioPresence_IsDifferentFrom_Omission()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var omitted = JsonSerializer.Deserialize<OnboardingCompleteRequest>("""{"confirmed":true}""", options)!;
        var cleared = JsonSerializer.Deserialize<OnboardingCompleteRequest>("""{"confirmed":true,"bio":null}""", options)!;
        var edited = JsonSerializer.Deserialize<OnboardingCompleteRequest>("""{"confirmed":true,"bio":"Updated"}""", options)!;

        Assert.False(omitted.BioWasProvided);
        Assert.True(cleared.BioWasProvided);
        Assert.True(edited.BioWasProvided);
        Assert.Equal("Updated", edited.Bio);
        Assert.Empty(Validate(edited));
    }

    [Fact]
    public void Complete_Rejects_BioLongerThan2000()
    {
        var request = new OnboardingCompleteRequest { Confirmed = true, Bio = new string('x', 2001) };
        Assert.NotEmpty(Validate(request));
    }

    private static RegisterRequest ValidRegister() => new()
    {
        DisplayName = "Nguyen Van A",
        University = "FPT University",
        Email = "test@example.com",
        Password = "Password123!",
        AcceptTerms = true
    };

    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
        return errors;
    }
}
