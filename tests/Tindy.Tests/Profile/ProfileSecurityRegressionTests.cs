using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tindy.Api.Controllers;
using Tindy.Api.ExceptionHandlers;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Profile;

/// <summary>
/// STEP 11: no Docker, database, OAuth provider or credentials required.
/// Controller attributes are contract checks, not a substitute for HTTP middleware tests.
/// </summary>
public sealed class ProfileSecurityRegressionTests
{
    public static IEnumerable<object[]> ProtectedControllers =>
    [
        [typeof(MyProfileController)],
        [typeof(MemberProfileController)],
        [typeof(MyExperiencesController)],
        [typeof(MemberExperiencesController)],
        [typeof(MyCertificatesController)],
        [typeof(MemberCertificatesController)]
    ];

    [Theory]
    [MemberData(nameof(ProtectedControllers))]
    public void ProfileEndpoints_UseDefaultOnboardedPolicyWithoutAnonymousOverride(Type controller)
    {
        var attribute = controller.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attribute);
        Assert.Null(attribute!.Policy); // No pre-onboarding AuthenticatedOnly override.
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(typeof(MemberProfileController))]
    [InlineData(typeof(MemberExperiencesController))]
    [InlineData(typeof(MemberCertificatesController))]
    public void MemberControllers_AreReadOnly(Type controller)
    {
        var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.Single(methods, method => method.GetCustomAttribute<HttpGetAttribute>() is not null);
        Assert.DoesNotContain(methods, method => method.GetCustomAttribute<HttpPostAttribute>() is not null ||
            method.GetCustomAttribute<HttpPutAttribute>() is not null ||
            method.GetCustomAttribute<HttpPatchAttribute>() is not null ||
            method.GetCustomAttribute<HttpDeleteAttribute>() is not null);
    }

    [Fact]
    public void MemberDto_AndCollectionsHaveNoAccountOrVerificationSecrets()
    {
        var forbidden = new[]
        {
            "PrimaryEmail", "PhoneNumber", "ResumeUrl", "PasswordHash",
            "UserCredential", "ExternalAuthAccounts", "Status", "DeletedAt",
            "IsOnboardingCompleted", "IsPublic", "AccessToken", "RefreshToken",
            "IsVerified", "VerificationStatus", "FileStorageKey"
        };

        foreach (var dto in new[] { typeof(MemberProfileResponse), typeof(ExperienceResponse), typeof(CertificateResponse) })
        {
            var properties = dto.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var field in forbidden)
                Assert.DoesNotContain(field, properties);
        }
    }

    [Theory]
    [InlineData("{\"username\":\"takeover\"}")]
    [InlineData("{\"primary_email\":\"attacker@example.com\"}")]
    [InlineData("{\"avatar_url\":\"https://attacker.example/a.png\"}")]
    [InlineData("{\"is_onboarding_completed\":true}")]
    [InlineData("{\"is_public\":false}")]
    [InlineData("{\"bio\":\"first\",\"bio\":\"second\"}")]
    public void Patch_DisallowsIdentityPrivilegedAndDuplicateFields(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        Assert.Throws<ProfileValidationException>(() => ProfilePatchParser.Parse(json.RootElement));
    }

    [Theory]
    [InlineData("{\"project_name\":\"A\",\"role_title\":\"B\",\"description\":\"C\",\"skill_ids\":[],\"user_id\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"project_name\":\"A\",\"role_title\":\"B\",\"description\":\"C\",\"skill_ids\":[],\"is_verified\":true}")]
    public void Experience_DoesNotAcceptOwnerOrVerificationFields(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        Assert.Throws<ProfileValidationException>(() => ExperienceRequestParser.Parse(json.RootElement));
    }

    [Theory]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"user_id\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"is_verified\":true}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"file_url\":\"https://example.com/c.pdf\"}")]
    public void Certificate_DoesNotAcceptOwnerVerificationOrUploadFields(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        Assert.Throws<ProfileValidationException>(() => CertificateRequestParser.Parse(json.RootElement));
    }

    [Fact]
    public void Patch_MissingAndNullAreDifferent()
    {
        using var json = JsonDocument.Parse("""{"bio":null}""");
        var request = ProfilePatchParser.Parse(json.RootElement);
        Assert.True(request.Bio.IsSpecified);
        Assert.Null(request.Bio.Value);
        Assert.False(request.LearningGoals.IsSpecified);
        Assert.False(request.SkillIds.IsSpecified);
    }

    [Fact]
    public async Task ValidationProblemDetails_HasErrorsAndErrorCode()
    {
        var exception = new ProfileValidationException(new Dictionary<string, string[]>
        {
            ["skill_ids"] = ["Invalid skill ID."]
        });
        using var result = await HandleAsync(exception);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("application/problem+json", result.ContentType);
        Assert.Equal("VALIDATION_FAILED", result.Json.RootElement.GetProperty("error_code").GetString());
        Assert.Equal("Invalid skill ID.", result.Json.RootElement.GetProperty("errors").GetProperty("skill_ids")[0].GetString());
    }

    [Fact]
    public async Task BusinessAndOnboardingErrors_KeepExpectedHttpStatusAndCodes()
    {
        using var conflict = await HandleAsync(new OnboardingAlreadyCompletedException());
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("ONBOARDING_ALREADY_COMPLETED", conflict.Json.RootElement.GetProperty("error_code").GetString());
        using var business = await HandleAsync(new BusinessRuleException("Invalid catalog reference."));
        Assert.Equal(422, business.StatusCode);
        Assert.Equal("BUSINESS_RULE_VIOLATION", business.Json.RootElement.GetProperty("error_code").GetString());
        using var missing = await HandleAsync(new NotFoundException("Profile was not found."));
        Assert.Equal(404, missing.StatusCode);
        Assert.Equal("NOT_FOUND", missing.Json.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task ServerError_MustNotExposeSecretFromException()
    {
        const string fakeSecret = "mock-provider-access-token-do-not-leak";
        using var response = await HandleAsync(new InvalidOperationException($"Upstream failed: {fakeSecret}"));
        Assert.Equal(500, response.StatusCode);
        Assert.Equal("INTERNAL_SERVER_ERROR", response.Json.RootElement.GetProperty("error_code").GetString());
        Assert.DoesNotContain(fakeSecret, response.RawBody, StringComparison.Ordinal);
    }

    private static async Task<ExceptionResponse> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/me/profile";
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return new ExceptionResponse(context.Response.StatusCode, context.Response.ContentType, body, JsonDocument.Parse(body));
    }

    private sealed record ExceptionResponse(int StatusCode, string? ContentType, string RawBody, JsonDocument Json) : IDisposable
    {
        public void Dispose() => Json.Dispose();
    }
}
