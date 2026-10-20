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

/// <summary>STEP 07: service, parser and EF model metadata tests without Docker/PostgreSQL.</summary>
public sealed class ExperienceServiceTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ActiveSkill = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid InactiveSkill = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid SecondSkill = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static ExperienceUpsertRequest Request(params Guid[] skills) => new(
        "  RAG Platform ", " Backend Developer ", " Implemented a RAG API ", null,
        new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 1), false, skills);

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"project_name\":\"hi\",\"role_title\":\"Dev\",\"description\":\"okay\",\"is_ongoing\":false}")]
    [InlineData("{\"project_name\":\"hi\",\"role_title\":\"Dev\",\"description\":\"okay\",\"skill_ids\":[],\"is_ongoing\":false,\"user_id\":\"bad\"}")]
    [InlineData("{\"project_name\":\"hi\",\"role_title\":\"Dev\",\"description\":\"okay\",\"skill_ids\":[],\"is_ongoing\":false,\"technologies\":[]}")]
    [InlineData("{\"project_name\":\"hi\",\"project_name\":\"again\",\"role_title\":\"Dev\",\"description\":\"okay\",\"skill_ids\":[],\"is_ongoing\":false}")]
    [InlineData("{\"project_name\":\"hi\",\"role_title\":\"Dev\",\"description\":\"okay\",\"skill_ids\":[],\"is_ongoing\":false,\"start_date\":\"2026-13\"}")]
    [InlineData("{\"project_name\":\"hi\",\"role_title\":\"Dev\",\"description\":\"okay\",\"skill_ids\":[],\"is_ongoing\":false,\"start_date\":\"2026-1\"}")]
    [InlineData("{\"project_name\":\"hi\",\"role_title\":\"Dev\",\"description\":\"okay\",\"skill_ids\":[],\"is_ongoing\":true,\"end_date\":\"2026-02\"}")]
    [InlineData("{\"project_name\":\"hi\",\"role_title\":\"Dev\",\"description\":\"okay\",\"skill_ids\":[],\"is_ongoing\":false,\"start_date\":\"2026-03\",\"end_date\":\"2026-02\"}")]
    public void Parser_RejectsInvalidPayloads(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        Assert.Throws<ProfileValidationException>(() => ExperienceRequestParser.Parse(parsed.RootElement));
    }

    [Fact]
    public void Parser_ParsesValidMonthAndEmptySkills_AndRejectsDuplicateSkills()
    {
        using var parsed = JsonDocument.Parse("""{"project_name":" Project ","role_title":" Developer ","description":" Desc ","skill_ids":[],"is_ongoing":false,"start_date":"2025-09","end_date":null}""");
        var request = ExperienceRequestParser.Parse(parsed.RootElement);
        Assert.Equal(new DateOnly(2025, 9, 1), request.StartDate);
        Assert.Null(request.EndDate);
        Assert.Empty(request.SkillIds);
        Assert.Equal("Project", request.ProjectName);

        using var duplicates = JsonDocument.Parse($$"""{"project_name":"Project","role_title":"Dev","description":"Desc","skill_ids":["{{ActiveSkill}}","{{ActiveSkill}}"],"is_ongoing":false}""");
        Assert.Throws<ProfileValidationException>(() => ExperienceRequestParser.Parse(duplicates.RootElement));
    }

    [Fact]
    public async Task Create_ValidatesCatalogAndPersistsAtomicExperienceLinks()
    {
        var repo = new FakeRepository();
        var result = await new ExperienceService(repo).CreateAsync(Owner, Request(ActiveSkill));
        Assert.Equal("RAG Platform", result.ProjectName);
        Assert.Equal("Backend Developer", result.RoleTitle);
        Assert.Equal("2025-09", result.StartDate);
        Assert.Equal("2026-01", result.EndDate);
        Assert.Equal(ActiveSkill, Assert.Single(result.Technologies).SkillId);
        Assert.Equal(1, repo.SaveCalls);
        Assert.Equal(1, repo.Transactions);
        Assert.Equal(0, repo.ProfileSkillsMutations);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task Create_AllowsZeroSkills_AndNoDates()
    {
        var repo = new FakeRepository();
        var result = await new ExperienceService(repo).CreateAsync(Owner,
            Request() with { StartDate = null, EndDate = null, IsOngoing = true });
        Assert.Empty(result.Technologies);
        Assert.Null(result.StartDate);
        Assert.Null(result.EndDate);
        Assert.True(result.IsOngoing);
    }

    [Fact]
    public async Task Create_RejectsInactiveOrNonexistentSkillWithoutMutation()
    {
        var repo = new FakeRepository();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new ExperienceService(repo).CreateAsync(Owner, Request(InactiveSkill)));
        Assert.Empty(repo.Items);
        Assert.Equal(0, repo.SaveCalls);
    }

    [Fact]
    public async Task Update_PreservesPreviouslyLinkedInactiveSkill_OnlyChecksAddedSkills()
    {
        var repo = new FakeRepository();
        var original = repo.Seed(Owner, InactiveSkill);
        var result = await new ExperienceService(repo).UpdateAsync(Owner, original.ExperienceId,
            Request(InactiveSkill, ActiveSkill));
        Assert.Equal(2, result.Technologies.Count);
        Assert.Contains(result.Technologies, skill => skill.SkillId == InactiveSkill);
        Assert.Contains(result.Technologies, skill => skill.SkillId == ActiveSkill);
        Assert.Equal(1, repo.ActiveSkillChecks);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task Update_InactiveNewSkillRejected_PreservesPriorEntityAndSkills()
    {
        var repo = new FakeRepository();
        var old = repo.Seed(Owner, ActiveSkill);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new ExperienceService(repo).UpdateAsync(Owner, old.ExperienceId,
                Request(InactiveSkill) with { ProjectName = "New Name" }));
        Assert.Equal("Existing Project", repo.Items[old.ExperienceId].ProjectName);
        Assert.Equal(ActiveSkill, Assert.Single(repo.Items[old.ExperienceId].ExperienceSkills).SkillId);
        Assert.Equal(0, repo.SaveCalls);
    }

    [Fact]
    public async Task Update_RollbackOnSaveFailure_RestoresScalarAndLinks()
    {
        var repo = new FakeRepository { FailOnSave = true };
        var old = repo.Seed(Owner, ActiveSkill);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExperienceService(repo).UpdateAsync(Owner, old.ExperienceId,
                Request(SecondSkill) with { ProjectName = "Edited" }));
        Assert.Equal("Existing Project", repo.Items[old.ExperienceId].ProjectName);
        Assert.Equal(ActiveSkill, Assert.Single(repo.Items[old.ExperienceId].ExperienceSkills).SkillId);
    }

    [Fact]
    public async Task Service_RejectsInvalidTypedRequests_EvenWhenParserIsBypassed()
    {
        var repo = new FakeRepository();
        var service = new ExperienceService(repo);
        await Assert.ThrowsAsync<ProfileValidationException>(() => service.CreateAsync(Owner,
            Request(ActiveSkill, ActiveSkill)));
        await Assert.ThrowsAsync<ProfileValidationException>(() => service.CreateAsync(Owner,
            Request() with { StartDate = new DateOnly(2025, 9, 12) }));
        await Assert.ThrowsAsync<ProfileValidationException>(() => service.CreateAsync(Owner,
            Request() with { EndDate = new DateOnly(2025, 8, 1) }));
        await Assert.ThrowsAsync<ProfileValidationException>(() => service.CreateAsync(Owner,
            Request() with { IsOngoing = true }));
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task Update_EmptySkillsIsFullReplacement_AndDoesNotTouchProfileSkills()
    {
        var repo = new FakeRepository();
        var old = repo.Seed(Owner, ActiveSkill);
        var result = await new ExperienceService(repo).UpdateAsync(Owner, old.ExperienceId,
            Request() with { RepoUrl = " https://example.com/repository ", EndDate = null, IsOngoing = true });
        Assert.Empty(result.Technologies);
        Assert.Equal("https://example.com/repository", result.RepoUrl);
        Assert.True(result.IsOngoing);
        Assert.Null(result.EndDate);
        Assert.Equal(0, repo.ProfileSkillsMutations);
    }

    [Fact]
    public async Task OtherUserCannotReadUpdateOrDelete_OwnerCanDelete()
    {
        var repo = new FakeRepository();
        var experience = repo.Seed(Owner, ActiveSkill);
        var service = new ExperienceService(repo);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetMyExperienceAsync(Stranger, experience.ExperienceId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(Stranger, experience.ExperienceId, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Stranger, experience.ExperienceId));
        Assert.Single(repo.Items);
        await service.DeleteAsync(Owner, experience.ExperienceId);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task IneligibleOrEmptyCallerAndMissingExperienceRejected()
    {
        var repo = new FakeRepository { OwnerEligible = false };
        await Assert.ThrowsAsync<NotFoundException>(() => new ExperienceService(repo).ListMyExperiencesAsync(Owner));
        repo.OwnerEligible = true;
        await Assert.ThrowsAsync<NotFoundException>(() => new ExperienceService(repo).CreateAsync(Guid.Empty, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => new ExperienceService(repo).DeleteAsync(Owner, Guid.NewGuid()));
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task List_ReturnsOwnedOnlyWithMonthStrings()
    {
        var repo = new FakeRepository();
        repo.Seed(Owner, ActiveSkill);
        repo.Seed(Stranger, SecondSkill);
        var result = await new ExperienceService(repo).ListMyExperiencesAsync(Owner);
        Assert.Single(result);
        Assert.Equal("2024-10", result[0].StartDate);
    }

    [Fact]
    public async Task MemberExperiences_OnlyReturnsTargetItems_AndEmptyTargetReturnsEmptyArray()
    {
        var repo = new FakeRepository();
        repo.Seed(Owner, ActiveSkill);
        repo.Seed(Stranger, SecondSkill);
        var service = new ExperienceService(repo);

        var owned = await service.ListMemberExperiencesAsync(Owner);
        Assert.Single(owned);
        Assert.Equal("Existing Project", owned[0].ProjectName);

        var emptyTarget = Guid.NewGuid();
        var emptyList = await service.ListMemberExperiencesAsync(emptyTarget);
        Assert.Empty(emptyList);
    }

    [Fact]
    public async Task MemberExperiences_IneligibleAndEmptyTargetReturnIdentical404()
    {
        var repo = new FakeRepository { OwnerEligible = false };
        var service = new ExperienceService(repo);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => service.ListMemberExperiencesAsync(Owner));
        var empty = await Assert.ThrowsAsync<NotFoundException>(() => service.ListMemberExperiencesAsync(Guid.Empty));
        Assert.Equal("Profile was not found.", missing.Message);
        Assert.Equal(missing.Message, empty.Message);
    }

    [Fact]
    public void EfModel_ExperienceSkillsCascadeOnExperience_RestrictOnCatalogSkill()
    {
        var options = new DbContextOptionsBuilder<TindyDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres;Password=not-used")
            .Options;
        using var db = new TindyDbContext(options);
        var entity = db.Model.FindEntityType(typeof(ProfileExperienceSkill));
        Assert.NotNull(entity);
        var foreignKeys = entity!.GetForeignKeys().ToList();
        Assert.Contains(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(ProfileExperience) && fk.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(Skill) && fk.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Equal(2, entity.FindPrimaryKey()!.Properties.Count);
        // Model metadata only; actual database constraints are validated separately when PostgreSQL is available.
    }

    private sealed class FakeRepository : IExperienceRepository
    {
        public Dictionary<Guid, ProfileExperience> Items { get; private set; } = new();
        public bool OwnerEligible { get; set; } = true;
        public bool FailOnSave { get; set; }
        public int SaveCalls { get; private set; }
        public int Transactions { get; private set; }
        public int ActiveSkillChecks { get; private set; }
        public int ProfileSkillsMutations { get; private set; }

        public ProfileExperience Seed(Guid owner, params Guid[] ids)
        {
            var entity = new ProfileExperience
            {
                ExperienceId = Guid.NewGuid(), UserId = owner, ProjectName = "Existing Project", RoleTitle = "Engineer",
                Description = "Legacy", StartDate = new DateOnly(2024, 10, 1), CreatedAt = DateTime.UtcNow,
                ExperienceSkills = ids.Select(id => new ProfileExperienceSkill { SkillId = id }).ToList()
            };
            Items.Add(entity.ExperienceId, entity);
            return entity;
        }

        public Task<bool> IsEligibleOwnerAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OwnerEligible && userId != Guid.Empty);

        public Task<IReadOnlyList<ExperienceResponse>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExperienceResponse>>(Items.Values.Where(e => e.UserId == userId)
                .OrderByDescending(e => e.StartDate.HasValue).ThenByDescending(e => e.StartDate)
                .Select(Response).ToArray());

        public Task<ExperienceResponse?> GetOwnedAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExperienceResponse?>(Items.TryGetValue(experienceId, out var e) && e.UserId == userId ? Response(e) : null);

        public Task<ProfileExperience?> FindOwnedForUpdateAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProfileExperience?>(Items.TryGetValue(experienceId, out var e) && e.UserId == userId ? e : null);

        public Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default)
        {
            ActiveSkillChecks++;
            return Task.FromResult<IReadOnlyCollection<Guid>>(skillIds.Where(id => id == ActiveSkill || id == SecondSkill).ToArray());
        }

        public void Add(ProfileExperience experience) => Items.Add(experience.ExperienceId, experience);

        public void SyncSkills(ProfileExperience experience, IReadOnlyCollection<Guid> skillIds)
        {
            experience.ExperienceSkills = skillIds.Select(id => new ProfileExperienceSkill { ExperienceId = experience.ExperienceId, SkillId = id }).ToList();
        }

        public void Remove(ProfileExperience experience) => Items.Remove(experience.ExperienceId);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailOnSave) throw new InvalidOperationException("Simulated transaction failure.");
            SaveCalls++;
            return Task.CompletedTask;
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Guid ownerUserId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            Transactions++;
            var before = Items.ToDictionary(x => x.Key, x => Clone(x.Value));
            try { return await operation(cancellationToken); }
            catch { Items = before; throw; }
        }

        private static ProfileExperience Clone(ProfileExperience src) => new()
        {
            ExperienceId = src.ExperienceId, UserId = src.UserId, ProjectName = src.ProjectName,
            RoleTitle = src.RoleTitle, Description = src.Description, RepoUrl = src.RepoUrl,
            StartDate = src.StartDate, EndDate = src.EndDate, IsOngoing = src.IsOngoing,
            CreatedAt = src.CreatedAt, UpdatedAt = src.UpdatedAt,
            ExperienceSkills = src.ExperienceSkills.Select(x => new ProfileExperienceSkill { ExperienceId = x.ExperienceId, SkillId = x.SkillId }).ToList()
        };

        private static ExperienceResponse Response(ProfileExperience e) => new(
            e.ExperienceId, e.ProjectName, e.RoleTitle, e.Description,
            e.ExperienceSkills.OrderBy(x => x.SkillId).Select(x => new ExperienceTechnologyResponse(x.SkillId, "Catalog Skill")).ToArray(),
            e.RepoUrl, e.StartDate?.ToString("yyyy-MM"), e.EndDate?.ToString("yyyy-MM"),
            e.IsOngoing, e.CreatedAt, e.UpdatedAt);
    }
}
