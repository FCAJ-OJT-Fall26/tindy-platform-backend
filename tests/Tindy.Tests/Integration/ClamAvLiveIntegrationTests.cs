using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Media;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

// Explicit opt-in: no fake scanner, no application database, no real malware.
public sealed class ClamAvLiveIntegrationTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [ClamAvLiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "ClamAvLive")]
    public async Task RealScanner_ControlsPdfLifecycleAndHttpDownload(bool eicar)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var owner = Guid.NewGuid();
        db.Users.Add(new User
        {
            UserId = owner, Status = "active", IsOnboardingCompleted = true, CreatedAt = DateTime.UtcNow,
            Profile = new UserProfile { UserId = owner, DisplayName = "ClamAV Live Test", CreatedAt = DateTime.UtcNow }
        });
        await db.SaveChangesAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(owner, ["Member"]).AccessToken);
        var created = await client.PostAsJsonAsync("/api/me/profile/certificates", new { title = "ClamAV live", issuer = "Test only" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var certificate = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("certificate_id").GetGuid();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Pdf(eicar)), "file", "scanner-test.pdf");
        var uploaded = await client.PostAsync($"/api/me/profile/certificates/{certificate}/attachments", form);
        Assert.Equal(HttpStatusCode.Accepted, uploaded.StatusCode);
        var media = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("media_asset_id").GetGuid();
        var readUrl = $"/api/media/{media}/content";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(readUrl)).StatusCode);

        // Resolve the production implementation and exercise the real TCP scanner.
        var lifecycle = scope.ServiceProvider.GetRequiredService<MediaLifecycleService>();
        await lifecycle.ScanLocalAsync(media, default);
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(a => a.MediaAssetId == media);
        var unavailable = Environment.GetEnvironmentVariable("TINDY_CLAMAV_EXPECT_UNAVAILABLE") == "1";
        Assert.Equal(unavailable ? "processing" : eicar ? "rejected" : "active", asset.Status);
        Assert.Equal(unavailable ? "failed" : eicar ? "threat_detected" : "clean", asset.ScanStatus);
        Assert.Equal(unavailable || eicar ? HttpStatusCode.NotFound : HttpStatusCode.OK, (await client.GetAsync(readUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/profile/certificates/{certificate}")).StatusCode);
        await lifecycle.CleanupAsync(media, default);
    }

    [ClamAvLiveFact]
    [Trait("Category", "ClamAvLive")]
    public async Task RealScanner_AvailabilityMatchesRequestedPhase()
    {
        var scanner = new ClamAvMalwareScanner(Options.Create(new ClamAvOptions { TimeoutSeconds = 10 }));
        using var input = new MemoryStream(Pdf(false));
        var result = await scanner.ScanAsync(input, default);
        Assert.Equal(Environment.GetEnvironmentVariable("TINDY_CLAMAV_EXPECT_UNAVAILABLE") == "1"
            ? MalwareScanResult.Unavailable : MalwareScanResult.Clean, result);
    }

    private static byte[] Pdf(bool eicar)
    {
        var page = "BT /F1 12 Tf 40 100 Td (Tindy scanner integration test) Tj ET\n";
        // Split the harmless standard antivirus test string in source; assemble only in test memory.
        var test = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$" + "EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R" + (eicar ? " /Names << /EmbeddedFiles << /Names [(eicar.txt) 7 0 R] >> >>" : "") + " >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {page.Length} >>\nstream\n{page}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        if (eicar)
        {
            objects.Add($"<< /Type /EmbeddedFile /Length {test.Length} >>\nstream\n{test}\nendstream");
            objects.Add("<< /Type /Filespec /F (eicar.txt) /EF << /F 6 0 R >> >>");
        }
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{offsets.Count - 1} 0 obj\n{obj}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}

public sealed class ClamAvLiveTheoryAttribute : TheoryAttribute
{
    public ClamAvLiveTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("TINDY_CLAMAV_LIVE") != "1") Skip = "Set TINDY_CLAMAV_LIVE=1 after starting the real ClamAV container.";
    }
}

public sealed class ClamAvLiveFactAttribute : FactAttribute
{
    public ClamAvLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TINDY_CLAMAV_LIVE") != "1") Skip = "Set TINDY_CLAMAV_LIVE=1 after starting the real ClamAV container.";
    }
}
