using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Profile;

// No Docker: parser, business logic, ownership, rollback simulation and EF metadata.
public sealed class CertificateServiceTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static CertificateUpsertRequest Request() => new(
        "  AWS Certified Cloud Practitioner  ", " AWS ", "  Foundational AWS knowledge.  ",
        " https://www.credly.com/badges/example ");

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"title\":\"T\",\"issuer\":null}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"user_id\":\"bad\"}")]
    [InlineData("{\"title\":\"T\",\"title\":\"U\",\"issuer\":\"I\"}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"is_verified\":true}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"credential_url\":\"javascript:alert(1)\"}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"issued_at\":\"2026-02-30\"}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"issued_at\":\"2026-2-01\"}")]
    [InlineData("{\"title\":\"T\",\"issuer\":\"I\",\"issued_at\":\"2026-10-02\",\"expires_at\":\"2026-10-01\"}")]
    [InlineData("{\"title\":3,\"issuer\":\"I\"}")]
    public void Parser_RejectsInvalidPayloads(string json)
    {
        using var body = JsonDocument.Parse(json);
        Assert.Throws<ProfileValidationException>(() => CertificateRequestParser.Parse(body.RootElement));
    }

    [Fact]
    public void Parser_ValidNullableDataAndTrim()
    {
        using var body = JsonDocument.Parse("""{"title":"  Badge  ","issuer":"  Organization ","description":"  ","credential_url":null}""");
        var result = CertificateRequestParser.Parse(body.RootElement);
        Assert.Equal("Badge", result.Title);
        Assert.Equal("Organization", result.Issuer);
        Assert.Null(result.Description);
        Assert.Null(result.CredentialUrl);

        using var minimal = JsonDocument.Parse("""{"title":"T","issuer":"I"}""");
        var noOptionals = CertificateRequestParser.Parse(minimal.RootElement);
        Assert.Null(noOptionals.Description);
        Assert.Null(noOptionals.CredentialUrl);
    }

    [Theory]
    [InlineData("credential_id", "\"legacy-id\"")]
    [InlineData("issued_at", "\"2026-10-23\"")]
    [InlineData("expires_at", "null")]
    [InlineData("file", "null")]
    [InlineData("Title", "\"wrong case\"")]
    public void Parser_RejectsUnsupportedFieldsEvenWhenValuesAreValid(string field, string value)
    {
        using var body = JsonDocument.Parse($$"""{"title":"T","issuer":"I","{{field}}":{{value}}}""");
        var error = Assert.Throws<ProfileValidationException>(() => CertificateRequestParser.Parse(body.RootElement));
        Assert.Equal("This field is not supported.", Assert.Single(error.Errors[field]));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("issuer")]
    [InlineData("description")]
    [InlineData("credential_url")]
    public void Parser_RejectsDuplicateProperties(string field)
    {
        var value = field == "credential_url" ? "https://example.com" : "Value";
        using var body = JsonDocument.Parse($$"""{"title":"T","issuer":"I","{{field}}":"{{value}}","{{field}}":"{{value}}"}""");
        var error = Assert.Throws<ProfileValidationException>(() => CertificateRequestParser.Parse(body.RootElement));
        Assert.Equal("Duplicate JSON properties are not allowed.", Assert.Single(error.Errors[field]));
    }

    [Theory]
    [InlineData("title", 255)]
    [InlineData("issuer", 255)]
    [InlineData("description", 2000)]
    [InlineData("credential_url", 1000)]
    public void ParserAndApplication_EnforceTextLengthBoundaries(string field, int limit)
    {
        string Value(int size) => field == "credential_url" ? "https://example.com/" + new string('x', size - 20) : new string('x', size);
        CertificateUpsertRequest RequestWith(int size) => field switch
        {
            "title" => Request() with { Title = Value(size) },
            "issuer" => Request() with { Issuer = Value(size) },
            "description" => Request() with { Description = Value(size) },
            _ => Request() with { CredentialUrl = Value(size) }
        };
        JsonDocument Body(int size) => JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
            { ["title"] = "T", ["issuer"] = "I", [field] = Value(size) }));
        using var valid = Body(limit);
        CertificateRequestParser.Parse(valid.RootElement);
        CertificateInputValidator.Normalize(RequestWith(limit));
        using var invalid = Body(limit + 1);
        Assert.Throws<ProfileValidationException>(() => CertificateRequestParser.Parse(invalid.RootElement));
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(RequestWith(limit + 1)));
    }

    [Fact]
    public async Task Update_OmittedOptionalFieldsClearExistingValues()
    {
        var repo = new FakeRepository();
        var existing = repo.Seed(Owner);
        existing.CredentialUrl = "https://example.com";
        using var body = JsonDocument.Parse("""{"title":"Replacement","issuer":"Org"}""");
        var updated = await new CertificateService(repo).UpdateAsync(Owner, existing.CertificateId,
            CertificateRequestParser.Parse(body.RootElement));
        Assert.Null(updated.Description);
        Assert.Null(updated.CredentialUrl);
        Assert.Equal(existing.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public void DirectApplicationCommands_AreRevalidated()
    {
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(
            Request() with { CredentialUrl = "ftp://example.org" }));
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(
            Request() with { Title = " " }));
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(
            Request() with { Description = new string('x', 2001) }));
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(null));
    }

    [Fact]
    public async Task Create_NormalizesPersistsWithUTCAndDescription()
    {
        var repo = new FakeRepository();
        var result = await new CertificateService(repo).CreateAsync(Owner, Request());
        Assert.Equal("AWS Certified Cloud Practitioner", result.Title);
        Assert.Equal("AWS", result.Issuer);
        Assert.Equal("Foundational AWS knowledge.", result.Description);
        Assert.Equal("https://www.credly.com/badges/example", result.CredentialUrl);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
        Assert.Null(result.UpdatedAt);
        Assert.Equal(1, repo.SaveCalls);
        Assert.Equal(1, repo.Transactions);
        Assert.Equal(Owner, Assert.Single(repo.Items.Values).UserId);
    }

    [Fact]
    public async Task Create_AllowsNullDescriptionAndCredentialUrl()
    {
        var result = await new CertificateService(new FakeRepository()).CreateAsync(Owner,
            Request() with { Description = null, CredentialUrl = null });
        Assert.Null(result.Description);
        Assert.Null(result.CredentialUrl);
    }

    [Fact]
    public async Task Update_IsFullReplacement_OwnerOnly()
    {
        var repo = new FakeRepository();
        var cert = repo.Seed(Owner);
        var service = new CertificateService(repo);
        var result = await service.UpdateAsync(Owner, cert.CertificateId, Request() with
        {
            Title = "Renewed", Description = " Updated description ", CredentialUrl = null
        });
        Assert.Equal("Renewed", result.Title);
        Assert.Null(result.CredentialUrl);
        Assert.Equal("Updated description", result.Description);
        Assert.NotNull(result.UpdatedAt);
        Assert.Equal(cert.CertificateId, result.CertificateId);
        Assert.Equal(1, repo.SaveCalls);
        Assert.Equal(1, repo.Transactions);
    }

    [Fact]
    public async Task ForeignCertificate_Is404OnUpdateDelete_AndNeverMutatesOtherUsers()
    {
        var repo = new FakeRepository();
        var cert = repo.Seed(Stranger);
        var service = new CertificateService(repo);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(Owner, cert.CertificateId, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Owner, cert.CertificateId));
        Assert.Equal("Existing", Assert.Single(repo.Items.Values).Title);
        Assert.Equal(0, repo.SaveCalls);
    }

    [Fact]
    public async Task Delete_OwnerSucceeds_SecondDelete404()
    {
        var repo = new FakeRepository();
        var cert = repo.Seed(Owner);
        var service = new CertificateService(repo);
        await service.DeleteAsync(Owner, cert.CertificateId);
        Assert.Empty(repo.Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Owner, cert.CertificateId));
    }

    [Fact]
    public async Task InvalidOwnerAndTargetRejected_ButEligibleEmptyListReturned()
    {
        var repo = new FakeRepository();
        var service = new CertificateService(repo);
        Assert.Empty(await service.ListMyCertificatesAsync(Owner));
        Assert.Empty(await service.ListMemberCertificatesAsync(Stranger));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ListMemberCertificatesAsync(Guid.Empty));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(Guid.Empty, Request()));
        repo.Eligible = false;
        await Assert.ThrowsAsync<NotFoundException>(() => service.ListMyCertificatesAsync(Owner));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ListMemberCertificatesAsync(Stranger));
    }

    [Fact]
    public async Task MemberReadAndOwnerList_ReturnOnlyTargetCertificates()
    {
        var repo = new FakeRepository();
        repo.Seed(Owner);
        repo.Seed(Stranger);
        var service = new CertificateService(repo);
        Assert.Single(await service.ListMyCertificatesAsync(Owner));
        Assert.Single(await service.ListMemberCertificatesAsync(Stranger));
    }

    [Fact]
    public async Task SaveFailure_RollsBackUpdatedAndCreatedEntities()
    {
        var repo = new FakeRepository { FailOnSave = true };
        var original = repo.Seed(Owner);
        var service = new CertificateService(repo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(Owner, original.CertificateId,
            Request() with { Title = "Changed" }));
        Assert.Equal("Existing", Assert.Single(repo.Items.Values).Title);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Owner, Request()));
        Assert.Single(repo.Items);
    }

    [Fact]
    public void EfMetadata_HasExpectedFKColumnsIndexAndDescription()
    {
        using var context = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var entity = context.Model.FindEntityType(typeof(UserCertificate));
        Assert.NotNull(entity);
        Assert.Equal("user_certificates", entity!.GetTableName());
        Assert.Equal(2000, entity.FindProperty(nameof(UserCertificate.Description))!.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(UserCertificate.Description))!.IsNullable);
        Assert.Equal(new[] { "CertificateId", "CreatedAt", "CredentialUrl", "Description", "Issuer", "Title", "UpdatedAt", "UserId" },
            entity.GetProperties().Select(p => p.Name).Order().ToArray());
        Assert.Contains(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(User) &&
            fk.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(entity.GetIndexes(), idx => idx.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(UserCertificate.UserId)]));
    }

    private static CertificateResponse ToResponse(UserCertificate value) => new(
        value.CertificateId, value.Title, value.Issuer, value.Description, value.CredentialUrl,
        value.CreatedAt, value.UpdatedAt);

    private sealed class FakeRepository : ICertificateRepository
    {
        public Dictionary<Guid, UserCertificate> Items { get; } = new();
        public bool Eligible { get; set; } = true;
        public bool FailOnSave { get; set; }
        public int SaveCalls { get; private set; }
        public int Transactions { get; private set; }

        public UserCertificate Seed(Guid userId)
        {
            var item = new UserCertificate
            {
                UserId = userId, CertificateId = Guid.NewGuid(), Title = "Existing", Issuer = "Org",
                CreatedAt = DateTime.UtcNow, Description = "Existing description"
            };
            Items.Add(item.CertificateId, item);
            return item;
        }

        public Task<bool> IsEligibleUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Eligible && userId != Guid.Empty);

        public Task<IReadOnlyList<CertificateResponse>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CertificateResponse>>(Items.Values.Where(item => item.UserId == userId)
                .Select(ToResponse).ToArray());

        public Task<CertificateResponse?> GetOwnedAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.TryGetValue(certificateId, out var value) && value.UserId == userId ? ToResponse(value) : null);

        public Task<UserCertificate?> FindOwnedForUpdateAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.TryGetValue(certificateId, out var value) && value.UserId == userId ? value : null);

        public void Add(UserCertificate certificate) => Items.Add(certificate.CertificateId, certificate);
        public void Remove(UserCertificate certificate) => Items.Remove(certificate.CertificateId);
        public Task DetachMediaAsync(Guid ownerUserId, Guid certificateId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (FailOnSave) throw new InvalidOperationException("Simulated failure.");
            return Task.CompletedTask;
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Guid ownerUserId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            Transactions++;
            var backup = Items.ToDictionary(kvp => kvp.Key, kvp => Copy(kvp.Value));
            try { return await operation(cancellationToken); }
            catch
            {
                Items.Clear();
                foreach (var (key, value) in backup) Items.Add(key, value);
                throw;
            }
        }

        private static UserCertificate Copy(UserCertificate source) => new()
        {
            CertificateId = source.CertificateId, UserId = source.UserId, Title = source.Title,
            Issuer = source.Issuer, Description = source.Description, CredentialUrl = source.CredentialUrl,
            CreatedAt = source.CreatedAt, UpdatedAt = source.UpdatedAt
        };
    }
}
