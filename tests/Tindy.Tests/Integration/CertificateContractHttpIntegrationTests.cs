using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class CertificateContractHttpIntegrationTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private const string Url = "/api/me/profile/certificates";

    private async Task<(Guid Id, HttpClient Client)> Member()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var id = Guid.NewGuid();
        db.Users.Add(new User { UserId = id, Status = "active", IsOnboardingCompleted = true, CreatedAt = DateTime.UtcNow,
            Profile = new UserProfile { UserId = id, DisplayName = "Certificate test", CreatedAt = DateTime.UtcNow } });
        await db.SaveChangesAsync();
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(id, ["Member"]).AccessToken);
        return (id, client);
    }

    [Fact]
    public async Task CreateAndFullReplacementPut_ExposeOnlyNewContract_AndClearOmittedOptionals()
    {
        var (_, client) = await Member();
        using var owner = client;
        var created = await owner.PostAsJsonAsync(Url, new { title = " AWS ", issuer = " Amazon ",
            description = " Foundational knowledge. ", credential_url = " https://example.com/badge " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("Foundational knowledge.", data.GetProperty("description").GetString());
        Assert.Equal("AWS", data.GetProperty("title").GetString());
        Assert.Equal(new[] { "certificate_id", "created_at", "credential_url", "description", "issuer", "title", "updated_at" },
            data.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var id = data.GetProperty("certificate_id").GetGuid();
        var updated = await owner.PutAsJsonAsync($"{Url}/{id}", new { title = "AWS", issuer = "Amazon", description = " Updated. " });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var replacement = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("Updated.", replacement.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, replacement.GetProperty("credential_url").ValueKind);
        Assert.Equal(data.GetProperty("created_at").GetDateTime(), replacement.GetProperty("created_at").GetDateTime());
        Assert.Equal(JsonValueKind.String, replacement.GetProperty("updated_at").ValueKind);
        var cleared = await owner.PutAsJsonAsync($"{Url}/{id}", new { title = "AWS", issuer = "Amazon" });
        var emptyOptionals = (await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(JsonValueKind.Null, emptyOptionals.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, emptyOptionals.GetProperty("credential_url").ValueKind);
    }

    [Fact]
    public async Task Create_AllowsExplicitNullDescriptionAndUrl()
    {
        var (_, client) = await Member();
        using var owner = client;
        var created = await owner.PostAsJsonAsync(Url, new { title = "Badge", issuer = "Issuer",
            description = (string?)null, credential_url = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("credential_url").ValueKind);
    }

    [Theory]
    [InlineData("credential_id", "null")]
    [InlineData("issued_at", "\"2026-10-23\"")]
    [InlineData("expires_at", "null")]
    [InlineData("file", "null")]
    [InlineData("description", "123")]
    public async Task CreateAndUpdate_RejectUnsupportedFieldsAndInvalidTypes(string field, string value)
    {
        var (_, client) = await Member();
        using var owner = client;
        var json = $$"""{"title":"T","issuer":"I","{{field}}":{{value}}}""";
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method, method == HttpMethod.Post ? Url : $"{Url}/{Guid.NewGuid()}")
                { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            var response = await owner.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VALIDATION_FAILED", error.GetProperty("error_code").GetString());
            Assert.True(error.GetProperty("errors").TryGetProperty(field, out _));
        }
    }

    [Fact]
    public async Task Create_RejectsDuplicateDescriptionAndTooLongDescription()
    {
        var (_, client) = await Member();
        using var owner = client;
        using var duplicate = new StringContent("""{"title":"T","issuer":"I","description":"a","description":"b"}""",
            Encoding.UTF8, "application/json");
        var response = await owner.PostAsync(Url, duplicate);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Duplicate JSON properties are not allowed.", error.GetProperty("errors").GetProperty("description")[0].GetString());
        response = await owner.PostAsJsonAsync(Url, new { title = "T", issuer = "I", description = new string('x', 2001) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OwnerAndMemberLists_OrderByCreationDescendingThenIdAscending()
    {
        var (id, ownerClient) = await Member();
        var (_, memberClient) = await Member();
        using var owner = ownerClient;
        using var member = memberClient;
        var older = Guid.NewGuid();
        var first = Guid.Parse("00000000-0000-4000-8000-000000000001");
        var second = Guid.Parse("00000000-0000-4000-8000-000000000002");
        var now = new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            foreach (var cert in new[] { second, older, first }) db.UserCertificates.Add(new UserCertificate
                { CertificateId = cert, UserId = id, Title = "Badge", Issuer = "Issuer", Description = "Saved description",
                    CreatedAt = cert == older ? now.AddDays(-1) : now });
            await db.SaveChangesAsync();
        }
        foreach (var result in new[] { await owner.GetFromJsonAsync<JsonElement>(Url),
            await member.GetFromJsonAsync<JsonElement>($"/api/users/{id}/profile/certificates") })
        {
            var rows = result.GetProperty("data").EnumerateArray().ToArray();
            Assert.Equal(new[] { first, second, older }, rows.Select(r => r.GetProperty("certificate_id").GetGuid()).ToArray());
            Assert.All(rows, row => Assert.Equal("Saved description", row.GetProperty("description").GetString()));
        }
    }
}
