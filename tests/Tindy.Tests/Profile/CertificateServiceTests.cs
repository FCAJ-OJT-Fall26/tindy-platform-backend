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
        "  AWS Certified Cloud Practitioner  ", " AWS ", "  CERT-123  ",
        " https://www.credly.com/badges/example ", new DateOnly(2026, 9, 1), new DateOnly(2029, 9, 1));

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
        using var body = JsonDocument.Parse("""{"title":"  Badge  ","issuer":"  Organization ","credential_id":"  ","credential_url":null,"issued_at":"2024-02-29","expires_at":null}""");
        var result = CertificateRequestParser.Parse(body.RootElement);
        Assert.Equal("Badge", result.Title);
        Assert.Equal("Organization", result.Issuer);
        Assert.Null(result.CredentialId);
        Assert.Null(result.CredentialUrl);
        Assert.Equal(new DateOnly(2024, 2, 29), result.IssuedAt);
        Assert.Null(result.ExpiresAt);

        using var minimal = JsonDocument.Parse("""{"title":"T","issuer":"I"}""");
        var noOptionals = CertificateRequestParser.Parse(minimal.RootElement);
        Assert.Null(noOptionals.IssuedAt);
        Assert.Null(noOptionals.ExpiresAt);
    }

    [Fact]
    public void DirectApplicationCommands_AreRevalidated()
    {
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(
            Request() with { CredentialUrl = "ftp://example.org" }));
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(
            Request() with { Title = " " }));
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(
            Request() with { ExpiresAt = new DateOnly(2025, 1, 1) }));
        Assert.Throws<ProfileValidationException>(() => CertificateInputValidator.Normalize(null));
    }

    [Fact]
    public async Task Create_NormalizesPersistsWithUTCAndDateOnlyResponse()
    {
        var repo = new FakeRepository();
        var result = await new CertificateService(repo).CreateAsync(Owner, Request());
        Assert.Equal("AWS Certified Cloud Practitioner", result.Title);
        Assert.Equal("AWS", result.Issuer);
        Assert.Equal("CERT-123", result.CredentialId);
        Assert.Equal("https://www.credly.com/badges/example", result.CredentialUrl);
        Assert.Equal("2026-09-01", result.IssuedAt);
        Assert.Equal("2029-09-01", result.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
        Assert.Null(result.UpdatedAt);
        Assert.Equal(1, repo.SaveCalls);
        Assert.Equal(1, repo.Transactions);
        Assert.Equal(Owner, Assert.Single(repo.Items.Values).UserId);
    }

    [Fact]
    public async Task Create_AllowsNullDatesAndCredentialData()
    {
        var result = await new CertificateService(new FakeRepository()).CreateAsync(Owner,
            Request() with { CredentialId = null, CredentialUrl = null, IssuedAt = null, ExpiresAt = null });
        Assert.Null(result.CredentialId);
        Assert.Null(result.CredentialUrl);
        Assert.Null(result.IssuedAt);
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public async Task Update_IsFullReplacement_OwnerOnly()
    {
        var repo = new FakeRepository();
        var cert = repo.Seed(Owner);
        var service = new CertificateService(repo);
        var result = await service.UpdateAsync(Owner, cert.CertificateId, Request() with
        {
            Title = "Renewed", CredentialId = null, CredentialUrl = null, IssuedAt = null, ExpiresAt = null
        });
        Assert.Equal("Renewed", result.Title);
        Assert.Null(result.CredentialUrl);
        Assert.Null(result.IssuedAt);
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
    public void EfMetadata_HasExpectedFKColumnsIndexAndDateTypes()
    {
        using var context = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var entity = context.Model.FindEntityType(typeof(UserCertificate));
        Assert.NotNull(entity);
        Assert.Equal("user_certificates", entity!.GetTableName());
        Assert.Equal("date", entity.FindProperty(nameof(UserCertificate.IssuedAt))!.GetColumnType());
        Assert.Equal("date", entity.FindProperty(nameof(UserCertificate.ExpiresAt))!.GetColumnType());
        Assert.Contains(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(User) &&
            fk.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(entity.GetIndexes(), idx => idx.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(UserCertificate.UserId)]));
    }

    private static CertificateResponse ToResponse(UserCertificate value) => new(
        value.CertificateId, value.Title, value.Issuer, value.CredentialId, value.CredentialUrl,
        value.IssuedAt?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        value.ExpiresAt?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
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
                CreatedAt = DateTime.UtcNow, IssuedAt = new DateOnly(2024, 1, 1)
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
            Issuer = source.Issuer, CredentialId = source.CredentialId, CredentialUrl = source.CredentialUrl,
            IssuedAt = source.IssuedAt, ExpiresAt = source.ExpiresAt,
            CreatedAt = source.CreatedAt, UpdatedAt = source.UpdatedAt
        };
    }
}
