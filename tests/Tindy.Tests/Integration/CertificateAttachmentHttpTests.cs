using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authorization;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class CertificateAttachmentHttpTests
{
    private static readonly Guid Certificate = Guid.NewGuid();
    private static string Url => $"/api/me/profile/certificates/{Certificate}/attachments";

    private static WebApplicationFactory<Program> Configure(OAuthHttpApiFactory source, AttachmentDouble? service = null) => source.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICertificateAttachmentService>();
            services.AddSingleton<ICertificateAttachmentService>(service ?? new AttachmentDouble());
        }));

    private static HttpClient Client(WebApplicationFactory<Program> factory, Guid userId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(userId, ["Member"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    [Theory]
    [InlineData("cert.pdf", HttpStatusCode.Accepted, "processing")]
    [InlineData("cert.png", HttpStatusCode.Created, "active")]
    public async Task Upload_UsesRealMultipartBinding_SnakeCase_AndAccurateStatus(string name, HttpStatusCode status, string lifecycle)
    {
        using var source = new OAuthHttpApiFactory(false);
        source.Accounts.Status = OnboardingAccessStatus.Completed;
        using var factory = Configure(source);
        using var client = Client(factory, source.UserId);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", name);
        var response = await client.PostAsync(Url, form);
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        var data = body.GetProperty("data");
        Assert.Equal(lifecycle, data.GetProperty("status").GetString());
        Assert.Equal("owner_only", data.GetProperty("visibility").GetString());
        Assert.True(data.TryGetProperty("attachment_id", out _));
        Assert.False(data.TryGetProperty("storage_key", out _));
    }

    [Fact]
    public async Task AuthGates_RejectAnonymous_Incomplete_AndInactiveCallers()
    {
        using var source = new OAuthHttpApiFactory(false);
        using var factory = Configure(source);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url)).StatusCode);
        using var client = Client(factory, source.UserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        source.Accounts.Status = OnboardingAccessStatus.Completed;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        source.Accounts.Status = OnboardingAccessStatus.InactiveOrMissing;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task NoCollectionDelete_AndQuotaHasAllFourFields()
    {
        using var source = new OAuthHttpApiFactory(false);
        source.Accounts.Status = OnboardingAccessStatus.Completed;
        using var factory = Configure(source);
        using var client = Client(factory, source.UserId);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Url)).StatusCode);
        var response = await client.GetFromJsonAsync<JsonElement>("/api/me/profile/media/quota");
        Assert.Equal(4, response.GetProperty("data").EnumerateObject().Count());
        Assert.Equal(200_000_000, response.GetProperty("data").GetProperty("quota_bytes").GetInt64());
    }

    [Fact]
    public async Task UploadAndVisibilityPolicies_Return429WithProblemDetails()
    {
        using var source = new OAuthHttpApiFactory(false);
        source.Accounts.Status = OnboardingAccessStatus.Completed;
        using var factory = Configure(source);
        using var client = Client(factory, source.UserId);
        HttpResponseMessage response = null!;
        for (var i = 0; i < 21; i++)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent([1]), "file", "cert.png");
            response = await client.PostAsync(Url, form);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("MEDIA_RATE_LIMITED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString());
        for (var i = 0; i < 61; i++) response = await client.PatchAsJsonAsync($"{Url}/{Guid.NewGuid()}/visibility", new { visibility = "members" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Startup_WithLocalStorageNeedsNoAWSCredentials(string environment)
    {
        using var source = new OAuthHttpApiFactory(false);
        source.Accounts.Status = OnboardingAccessStatus.Completed;
        using var mediaFactory = Configure(source);
        using var factory = mediaFactory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = Client(factory, source.UserId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/profile/media/quota")).StatusCode);
        Assert.Null(factory.Services.GetService<Amazon.SQS.IAmazonSQS>());
        Assert.IsType<Tindy.Infrastructure.Media.ClamAvMalwareScanner>(factory.Services.GetRequiredService<IFileMalwareScanner>());
    }

    [Fact]
    public async Task ConcurrentHttpUploads_IncludeTrailingSlashRouteVariants()
    {
        using var source = new OAuthHttpApiFactory(false);
        source.Accounts.Status = OnboardingAccessStatus.Completed;
        var service = new AttachmentDouble { BlockUploads = true };
        using var factory = Configure(source, service);
        using var client = Client(factory, source.UserId);
        using var firstForm = new MultipartFormDataContent();
        using var secondForm = new MultipartFormDataContent();
        using var thirdForm = new MultipartFormDataContent();
        foreach (var form in new[] { firstForm, secondForm, thirdForm }) form.Add(new ByteArrayContent([1]), "file", "cert.png");
        var first = client.PostAsync(Url + "/", firstForm);
        var second = client.PostAsync(Url, secondForm);
        try
        {
            await service.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var rejected = await client.PostAsync(Url + "/", thirdForm, timeout.Token);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        }
        finally { service.Release.TrySetResult(); }
        foreach (var response in await Task.WhenAll(first, second)) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed class AttachmentDouble : ICertificateAttachmentService
    {
        private int _uploads;
        public bool BlockUploads { get; init; }
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static CertificateAttachmentResponse Response(string status = "active") =>
            new(Guid.NewGuid(), Guid.NewGuid(), "cert", "image/webp", 3, status, "owner_only", DateTime.UtcNow);
        public async Task<CertificateAttachmentResponse> UploadAsync(Guid ownerId, Guid certificateId, Stream content, string fileName, long length, CancellationToken ct)
        {
            if (BlockUploads)
            {
                if (Interlocked.Increment(ref _uploads) == 2) BothEntered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return Response(fileName.EndsWith(".pdf") ? "processing" : "active");
        }
        public Task<IReadOnlyList<CertificateAttachmentResponse>> ListAsync(Guid callerId, Guid ownerId, Guid certificateId, CancellationToken ct) => Task.FromResult<IReadOnlyList<CertificateAttachmentResponse>>([Response()]);
        public Task<CertificateAttachmentResponse> ChangeVisibilityAsync(Guid ownerId, Guid certificateId, Guid attachmentId, string visibility, CancellationToken ct) => Task.FromResult(Response());
        public Task DeleteAsync(Guid ownerId, Guid certificateId, Guid attachmentId, CancellationToken ct) => Task.CompletedTask;
        public Task<MediaQuotaResponse> GetQuotaAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(new MediaQuotaResponse(200_000_000, 0, 0, 200_000_000));
    }
}
