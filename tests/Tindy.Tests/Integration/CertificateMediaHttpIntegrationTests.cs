using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Media;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Media;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class CertificateMediaHttpIntegrationTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private async Task<(Guid Id, HttpClient Client)> Member()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var id = Guid.NewGuid();
        db.Users.Add(new User
        {
            UserId = id, Status = "active", IsOnboardingCompleted = true, CreatedAt = DateTime.UtcNow,
            Profile = new UserProfile { UserId = id, DisplayName = "HTTP Media Member", CreatedAt = DateTime.UtcNow }
        });
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(id, ["Member"]);
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return (id, client);
    }

    [Fact]
    public async Task RealHttpAndPostgres_UploadShareScanReadQuotaAndCertificateDelete()
    {
        var owner = await Member();
        var member = await Member();
        using var ownerClient = owner.Client;
        using var memberClient = member.Client;
        var created = await ownerClient.PostAsJsonAsync("/api/me/profile/certificates", new { title = "AWS", issuer = "Test" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var certificate = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("certificate_id").GetGuid();
        var url = $"/api/me/profile/certificates/{certificate}/attachments";
        async Task<JsonElement> Upload(byte[] bytes, string name, HttpStatusCode expected)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(bytes), "file", name);
            var response = await ownerClient.PostAsync(url, form);
            Assert.Equal(expected, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        }
        var image = await Upload(CertificateFileProcessorTests.Image(), "certificate.png", HttpStatusCode.Created);
        var imageMedia = image.GetProperty("media_asset_id").GetGuid();
        var imageAttachment = image.GetProperty("attachment_id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync($"/api/media/{imageMedia}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/media/{imageMedia}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PatchAsJsonAsync($"{url}/{imageAttachment}/visibility", new { visibility = "members" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PatchAsJsonAsync($"{url}/{imageAttachment}/visibility", new { visibility = "members" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/media/{imageMedia}/content")).StatusCode);

        using (var extra = new MultipartFormDataContent())
        {
            extra.Add(new ByteArrayContent(CertificateFileProcessorTests.Image()), "file", "second.png");
            var conflict = await ownerClient.PostAsync(url, extra);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("MEDIA_ATTACHMENT_LIMIT", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString());
        }
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync($"{url}/{imageAttachment}")).StatusCode);

        var pdf = await Upload("%PDF-1.7\nHTTP scanner adapter test\n%%EOF"u8.ToArray(), "certificate.pdf", HttpStatusCode.Accepted);
        var pdfMedia = pdf.GetProperty("media_asset_id").GetGuid();
        Assert.Equal("processing", pdf.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/media/{pdfMedia}/content")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var lifecycle = new MediaLifecycleService(db, storage, new CleanScannerDouble(), Options.Create(new MediaLifecycleOptions()), NullLogger<MediaLifecycleService>.Instance);
        await lifecycle.ScanLocalAsync(pdfMedia, default);
        var download = await ownerClient.GetAsync($"/api/media/{pdfMedia}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("no-store", download.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/media/{pdfMedia}/content")).StatusCode);
        var quota = (await ownerClient.GetFromJsonAsync<JsonElement>("/api/me/profile/media/quota")).GetProperty("data");
        Assert.Equal(0, quota.GetProperty("reserved_bytes").GetInt64());
        Assert.True(quota.GetProperty("used_bytes").GetInt64() > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync($"/api/me/profile/certificates/{certificate}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/media/{imageMedia}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/media/{pdfMedia}/content")).StatusCode);
        await lifecycle.CleanupAsync(imageMedia, default);
        await lifecycle.CleanupAsync(pdfMedia, default);
        Assert.False(await db.MediaAssets.AnyAsync(a => a.MediaAssetId == imageMedia || a.MediaAssetId == pdfMedia));
    }

    private sealed class CleanScannerDouble : IFileMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken ct) => Task.FromResult(MalwareScanResult.Clean);
    }
}
