using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tindy.Api.Controllers;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Xunit;

namespace Tindy.Tests.Profile;

public sealed class CertificateControllerTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CertificateId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private const string Body = """
    {"title":" AWS Badge ","issuer":"AWS","credential_id":"ID-123","credential_url":"https://www.credly.com/badges/test","issued_at":"2026-09-01","expires_at":null}
    """;

    private static CertificateResponse Example() => new(CertificateId, "AWS Badge", "AWS", "ID-123",
        "https://www.credly.com/badges/test", "2026-09-01", null,
        new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), null);

    private static T Attach<T>(T controller, string? sub) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        if (sub is not null)
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "test"));
        return controller;
    }

    private static JsonDocument Serialize(object? value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Snake));

    [Fact]
    public async Task GetMyCertificates_Returns200EmptyArray()
    {
        var fake = new StubCertificateService { Result = [] };
        var controller = Attach(new MyCertificatesController(fake), Owner.ToString("D"));
        var response = Assert.IsType<OkObjectResult>(await controller.List(CancellationToken.None));
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(Owner, fake.LastCaller);
        using var json = Serialize(response.Value);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Post_UsesJwtSub_ParsesDateAndReturns201WithoutPrivateFields()
    {
        var fake = new StubCertificateService();
        var controller = Attach(new MyCertificatesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(Body);
        using var cts = new CancellationTokenSource();
        var response = Assert.IsType<ObjectResult>(await controller.Create(body.RootElement, cts.Token));
        Assert.Equal(201, response.StatusCode);
        Assert.Equal(Owner, fake.LastCaller);
        Assert.Equal(cts.Token, fake.LastToken);
        Assert.Equal("AWS Badge", fake.LastRequest?.Title);
        Assert.Equal(new DateOnly(2026, 9, 1), fake.LastRequest?.IssuedAt);
        using var json = Serialize(response.Value);
        var item = json.RootElement.GetProperty("data");
        Assert.Equal(CertificateId.ToString("D"), item.GetProperty("certificate_id").GetString());
        Assert.Equal("2026-09-01", item.GetProperty("issued_at").GetString());
        foreach (var forbidden in new[] { "user_id", "primary_email", "phone_number", "is_verified", "verification_status", "file_url" })
            Assert.False(item.TryGetProperty(forbidden, out _));
    }

    [Fact]
    public async Task Put_UsesRouteId_Returns200()
    {
        var fake = new StubCertificateService();
        var controller = Attach(new MyCertificatesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(Body);
        var response = Assert.IsType<OkObjectResult>(await controller.Update(CertificateId, body.RootElement, CancellationToken.None));
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(CertificateId, fake.LastId);
        Assert.Equal(Owner, fake.LastCaller);
        using var json = Serialize(response.Value);
        Assert.Equal("Certificate updated successfully.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Delete_UsesOwnerAndId_Returns204NoContent()
    {
        var fake = new StubCertificateService();
        var controller = Attach(new MyCertificatesController(fake), Owner.ToString("D"));
        var result = Assert.IsType<NoContentResult>(await controller.Delete(CertificateId, CancellationToken.None));
        Assert.Equal(204, result.StatusCode);
        Assert.Equal(Owner, fake.LastCaller);
        Assert.Equal(CertificateId, fake.LastId);
    }

    [Theory]
    [InlineData("{\"title\":\"X\",\"issuer\":\"Y\",\"is_verified\":true}")]
    [InlineData("{\"title\":\"X\",\"issuer\":\"Y\",\"issued_at\":\"2026-02-30\"}")]
    [InlineData("{\"title\":\"X\",\"issuer\":\"Y\",\"credential_url\":\"ftp://bad\"}")]
    public async Task InvalidBody_RejectsPOSTAndPUTWithoutCallingService(string invalidBody)
    {
        var fake = new StubCertificateService();
        var controller = Attach(new MyCertificatesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(invalidBody);
        await Assert.ThrowsAsync<ProfileValidationException>(() => controller.Create(body.RootElement, CancellationToken.None));
        await Assert.ThrowsAsync<ProfileValidationException>(() => controller.Update(CertificateId, body.RootElement, CancellationToken.None));
        Assert.Equal(0, fake.Mutations);
    }

    [Fact]
    public async Task MemberList_UsesTargetID_AndReturnsOnlySafeProfessionalData()
    {
        var fake = new StubCertificateService { Result = [Example()] };
        var controller = Attach(new MemberCertificatesController(fake), Owner.ToString("D"));
        var response = Assert.IsType<OkObjectResult>(await controller.List(Target, CancellationToken.None));
        Assert.Equal(Target, fake.LastTarget);
        Assert.NotEqual(Owner, fake.LastTarget);
        using var json = Serialize(response.Value);
        var certificate = json.RootElement.GetProperty("data")[0];
        Assert.Equal("AWS Badge", certificate.GetProperty("title").GetString());
        Assert.False(certificate.TryGetProperty("user_id", out _));
        Assert.False(certificate.TryGetProperty("verification_status", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad-sub")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MissingOrInvalidJwtSub_IsRejected(string? sub)
    {
        var fake = new StubCertificateService();
        var myController = Attach(new MyCertificatesController(fake), sub);
        var memberController = Attach(new MemberCertificatesController(fake), sub);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => myController.List(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => memberController.List(Target, CancellationToken.None));
        Assert.Null(fake.LastCaller);
        Assert.Null(fake.LastTarget);
    }

    [Fact]
    public async Task ForeignCertificate_Throws404FromService()
    {
        var fake = new StubCertificateService { RejectForeign = true };
        var controller = Attach(new MyCertificatesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(Body);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Update(CertificateId, body.RootElement, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Delete(CertificateId, CancellationToken.None));
    }

    [Fact]
    public void RoutesAndAuthorization_UseDefaultPolicyAndReadOnlyMember()
    {
        var my = typeof(MyCertificatesController);
        var member = typeof(MemberCertificatesController);
        foreach (var type in new[] { my, member })
        {
            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Null(authorize!.Policy);
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        }
        Assert.Equal("api/me/profile/certificates", my.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("api/users/{user_id:guid}/profile/certificates", member.GetCustomAttribute<RouteAttribute>()!.Template);
        var myMethods = my.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Single(myMethods, method => method.GetCustomAttribute<HttpGetAttribute>() is not null);
        Assert.Single(myMethods, method => method.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.Equal("{certificate_id:guid}", my.GetMethod("Update")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{certificate_id:guid}", my.GetMethod("Delete")!.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        var methods = member.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Single(methods, method => method.GetCustomAttribute<HttpGetAttribute>() is not null);
        Assert.DoesNotContain(methods, method => method.GetCustomAttribute<HttpPostAttribute>() is not null ||
            method.GetCustomAttribute<HttpPutAttribute>() is not null || method.GetCustomAttribute<HttpDeleteAttribute>() is not null);
    }

    private sealed class StubCertificateService : ICertificateService
    {
        public IReadOnlyList<CertificateResponse> Result { get; init; } = [Example()];
        public bool RejectForeign { get; init; }
        public Guid? LastCaller { get; private set; }
        public Guid? LastTarget { get; private set; }
        public Guid? LastId { get; private set; }
        public CertificateUpsertRequest? LastRequest { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public int Mutations { get; private set; }

        public Task<IReadOnlyList<CertificateResponse>> ListMyCertificatesAsync(Guid callerUserId, CancellationToken token = default)
        {
            LastCaller = callerUserId;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<CertificateResponse>> ListMemberCertificatesAsync(Guid targetUserId, CancellationToken token = default)
        {
            LastTarget = targetUserId;
            return Task.FromResult(Result);
        }

        public Task<CertificateResponse> CreateAsync(Guid callerUserId, CertificateUpsertRequest request, CancellationToken token = default)
        {
            LastCaller = callerUserId;
            LastRequest = request;
            LastToken = token;
            Mutations++;
            return Task.FromResult(Example());
        }

        public Task<CertificateResponse> UpdateAsync(Guid callerUserId, Guid certificateId, CertificateUpsertRequest request, CancellationToken token = default)
        {
            LastCaller = callerUserId;
            LastId = certificateId;
            LastRequest = request;
            Mutations++;
            if (RejectForeign) throw new NotFoundException("Certificate was not found.");
            return Task.FromResult(Example());
        }

        public Task DeleteAsync(Guid callerUserId, Guid certificateId, CancellationToken token = default)
        {
            LastCaller = callerUserId;
            LastId = certificateId;
            Mutations++;
            if (RejectForeign) throw new NotFoundException("Certificate was not found.");
            return Task.CompletedTask;
        }
    }
}
