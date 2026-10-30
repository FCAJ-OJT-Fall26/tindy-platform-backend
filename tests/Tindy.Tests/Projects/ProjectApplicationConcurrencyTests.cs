using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tindy.Application.Entities;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

// All concurrency checks below use separate request scopes/connections to a real
// PostgreSQL database, not EF's in-memory provider or a fake repository.
public sealed class ProjectApplicationConcurrencyTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public ProjectApplicationConcurrencyTests(TindyApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentAccepts_CannotOverfillPositionOrProject(bool positionLimit)
    {
        var test = await ApplicationTestData.Seed(_factory, required: positionLimit ? 1 : 5, capacity: positionLimit ? 5 : 2);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var outsider = RecruitmentHttp.Client(_factory, test.Outsider);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var a = await ProjectApplicationHttpTests.Apply(member, test.Project, test.Position);
        var b = await ProjectApplicationHttpTests.Apply(outsider, test.Project, positionLimit ? test.Position : test.OtherPosition);
        var responses = await Race(
            () => leader.PostAsync(Accept(test.Project, a), null),
            () => leader.PostAsync(Accept(test.Project, b), null));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(2, await db.ProjectMembers.CountAsync(m => m.ProjectId == test.Project && m.Status == "active"));
        Assert.Equal(1, await db.ProjectPositions.Where(p => p.ProjectId == test.Project).SumAsync(p => p.FilledCount));
        Assert.Equal(1, await db.ProjectApplications.CountAsync(x => x.ProjectId == test.Project && x.Status == "accepted"));
        Assert.Equal(1, await db.ProjectApplications.CountAsync(x => x.ProjectId == test.Project && x.Status == "pending"));
    }

    [Fact]
    public async Task EightParallelApprovals_FillExactlyTwoSlots_AndRetriesDoNotIncrementAgain()
    {
        var test = await ApplicationTestData.Seed(_factory, required: 2, capacity: 10);
        var ids = new List<Guid>();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            var members = Enumerable.Range(0, 8).Select(_ => RecruitmentData.User()).ToArray();
            db.Users.AddRange(members); await db.SaveChangesAsync();
            foreach (var user in members)
            {
                using var client = RecruitmentHttp.Client(_factory, user.UserId);
                ids.Add(await ProjectApplicationHttpTests.Apply(client, test.Project, test.Position));
            }
        }
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var results = await Race(ids.Select<Guid, Func<Task<HttpResponseMessage>>>(id => () => leader.PostAsync(Accept(test.Project, id), null)).ToArray());
        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(6, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope2 = _factory.Services.CreateScope(); var check = scope2.ServiceProvider.GetRequiredService<TindyDbContext>();
        var position = await check.ProjectPositions.SingleAsync(p => p.PositionId == test.Position);
        Assert.Equal(2, position.FilledCount); Assert.Equal("filled", position.Status);
        var accepted = await check.ProjectApplications.Where(a => a.ProjectId == test.Project && a.Status == "accepted").ToArrayAsync();
        foreach (var application in accepted)
            await ProjectApplicationHttpTests.Problem(await leader.PostAsync(Accept(test.Project, application.ApplicationId), null), HttpStatusCode.Conflict, "APPLICATION_NOT_PENDING");
        Assert.Equal(3, await check.ProjectMembers.CountAsync(m => m.ProjectId == test.Project && m.Status == "active"));
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task WithdrawVersusLeaderDecision_OnlyOneTerminalTransitionCommits(string decision)
    {
        var test = await ApplicationTestData.Seed(_factory);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var id = await ProjectApplicationHttpTests.Apply(member, test.Project, test.Position);
        var results = await Race(() => member.PostAsync($"/api/me/project-applications/{id}/withdraw", null),
            () => leader.PostAsync($"{ProjectApplicationHttpTests.Url(test.Project)}/{id}/{decision}", null));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var application = await db.ProjectApplications.SingleAsync(a => a.ApplicationId == id);
        Assert.Contains(application.Status, new[] { "withdrawn", decision == "accept" ? "accepted" : "rejected" });
        var accepted = application.Status == "accepted" ? 1 : 0;
        Assert.Equal(accepted, await db.ProjectMembers.CountAsync(m => m.ProjectId == test.Project && !m.IsLeader && m.Status == "active"));
        Assert.Equal(accepted, (await db.ProjectPositions.SingleAsync(p => p.PositionId == test.Position)).FilledCount);
        if (accepted == 0) await ProjectApplicationHttpTests.Apply(member, test.Project, test.Position);
    }

    [Fact]
    public async Task DuplicateApply_IsConflict_AndDatabasePartialIndexAlsoProtectsDirectConcurrentInserts()
    {
        var test = await ApplicationTestData.Seed(_factory);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        var results = await Race(() => member.PostAsJsonAsync(ProjectApplicationHttpTests.Url(test.Project), new { position_id = test.Position }),
            () => member.PostAsJsonAsync(ProjectApplicationHttpTests.Url(test.Project), new { position_id = test.OtherPosition }));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        async Task<string> RawInsert(Guid position)
        {
            using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            var now = DateTime.UtcNow;
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_applications (application_id,project_id,position_id,applicant_id,status,created_at,updated_at) VALUES ({Guid.NewGuid()},{test.Project},{position},{test.Outsider},'pending',{now},{now})");
                return "inserted";
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            { Assert.Equal("ux_project_applications_pending", e.ConstraintName); return "conflict"; }
        }
        var inserts = await Task.WhenAll(RawInsert(test.Position), RawInsert(test.OtherPosition));
        Assert.Equal(new[] { "conflict", "inserted" }, inserts.OrderBy(s => s));
        using var scope2 = _factory.Services.CreateScope(); var check = scope2.ServiceProvider.GetRequiredService<TindyDbContext>();
        await check.ProjectApplications.Where(a => a.ProjectId == test.Project && a.ApplicantId == test.Outsider)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, "rejected"));
        Assert.Equal("inserted", await RawInsert(test.Position));
        Assert.Equal(2, await check.ProjectApplications.CountAsync(a => a.ProjectId == test.Project && a.ApplicantId == test.Outsider));
        var index = await check.Database.SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname='ux_project_applications_pending'").SingleAsync();
        Assert.Contains("WHERE", index); Assert.Contains("pending", index);
    }

    [Fact]
    public async Task CapacityEditVersusApproval_UsesSameProjectLock()
    {
        var test = await ApplicationTestData.Seed(_factory, required: 3, capacity: 3);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var id = await ProjectApplicationHttpTests.Apply(member, test.Project, test.Position);
        var edit = new { name = "Edited Project", description = "A sufficiently long project description.", category = "Web", difficulty_level = "intermediate", expected_commitment_hours = 10, duration = "3 months", max_team_capacity = 1, goals = Array.Empty<string>() };
        var results = await Race(() => leader.PutAsJsonAsync($"/api/projects/{test.Project}", edit), () => leader.PostAsync(Accept(test.Project, id), null));
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest }));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var project = await db.Projects.SingleAsync(p => p.ProjectId == test.Project);
        Assert.True(await db.ProjectMembers.CountAsync(m => m.ProjectId == test.Project && m.Status == "active") <= project.MaxTeamCapacity);
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact]
    public async Task PositionShrinkVersusApproval_CannotReduceBelowConcurrentFilledCount()
    {
        var test = await ApplicationTestData.Seed(_factory, required: 2);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var second = RecruitmentHttp.Client(_factory, test.Outsider);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var a = await ProjectApplicationHttpTests.Apply(member, test.Project, test.Position);
        await RecruitmentHttp.Data(await leader.PostAsync(Accept(test.Project, a), null));
        var b = await ProjectApplicationHttpTests.Apply(second, test.Project, test.Position);
        var results = await Race(() => leader.PutAsJsonAsync($"/api/projects/{test.Project}/positions/{test.Position}", new { required_count = 1 }),
            () => leader.PostAsync(Accept(test.Project, b), null));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var position = await db.ProjectPositions.SingleAsync(p => p.PositionId == test.Position);
        Assert.True(position.FilledCount <= position.RequiredCount);
        Assert.Equal(position.FilledCount, await db.ProjectMembers.CountAsync(m => m.PositionId == test.Position && m.Status == "active"));
    }

    [Fact]
    public async Task DatabaseFailure_DuringApproval_RollsBackApplicationMembershipAndSlot()
    {
        var test = await ApplicationTestData.Seed(_factory);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        var id = await ProjectApplicationHttpTests.Apply(member, test.Project, test.Position);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var suffix = id.ToString("N");
#pragma warning disable EF1002 // DDL identifiers cannot be SQL parameters; only locally generated Guids in N format are interpolated.
        await db.Database.ExecuteSqlRawAsync($"CREATE FUNCTION fail_approval_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test rollback'; END $$; CREATE TRIGGER fail_approval_{suffix} BEFORE UPDATE ON project_applications FOR EACH ROW WHEN (NEW.application_id = '{id}'::uuid) EXECUTE FUNCTION fail_approval_{suffix}();");
        try
        {
            using var operation = _factory.Services.CreateScope();
            await Assert.ThrowsAsync<DbUpdateException>(() => operation.ServiceProvider.GetRequiredService<ProjectApplicationService>().AcceptAsync(test.Leader, test.Project, id));
            Assert.Equal("pending", (await db.ProjectApplications.SingleAsync(a => a.ApplicationId == id)).Status);
            Assert.Equal(0, (await db.ProjectPositions.SingleAsync(p => p.PositionId == test.Position)).FilledCount);
            Assert.False(await db.ProjectMembers.AnyAsync(m => m.ProjectId == test.Project && m.UserId == test.Applicant));
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER fail_approval_{suffix} ON project_applications; DROP FUNCTION fail_approval_{suffix}();"); }
#pragma warning restore EF1002
    }

    private static string Accept(Guid project, Guid application) => $"{ProjectApplicationHttpTests.Url(project)}/{application}/accept";
    private static async Task<HttpResponseMessage[]> Race(params Func<Task<HttpResponseMessage>>[] actions)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions.Select(async action => { await gate.Task; return await action(); }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}
