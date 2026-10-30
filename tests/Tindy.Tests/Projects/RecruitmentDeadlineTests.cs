using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class RecruitmentDeadlineTests
{
    [Theory]
    [InlineData("2026-10-30T16:59:59Z", "2026-11-13")]
    [InlineData("2026-10-30T17:00:00Z", "2026-11-14")]
    public void Default_UsesVietnamCalendarDate_NotHostOrUtcDate(string now, string expectedDate)
    {
        var cutoff = RecruitmentDeadline.CreateCutoff(null, Utc(now));
        Assert.Equal(DateOnly.Parse(expectedDate), RecruitmentDeadline.ClosingDate(cutoff));
        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
        Assert.Equal(17, cutoff.Hour);
    }

    [Theory]
    [InlineData("2026-11-12", "2026-11-12T17:00:00Z")]
    [InlineData("2026-12-31", "2026-12-31T17:00:00Z")]
    [InlineData("2028-02-29", "2028-02-29T17:00:00Z")]
    public void CustomDate_ClosesAtFollowingVietnamMidnight(string date, string expected)
    {
        var cutoff = RecruitmentDeadline.CreateCutoff(DateOnly.Parse(date), Utc("2026-10-30T05:00:00Z"));
        Assert.Equal(Utc(expected), cutoff);
        Assert.Equal(DateOnly.Parse(date), RecruitmentDeadline.ClosingDate(cutoff));
    }

    [Theory]
    [InlineData("2026-10-30")]
    [InlineData("2026-10-31")]
    [InlineData("9999-12-31")]
    public void InvalidDate_IsRejectedAtVietnamTodayBoundary(string date)
    {
        var exception = Assert.Throws<ProjectException>(() =>
            RecruitmentDeadline.CreateCutoff(DateOnly.Parse(date), Utc("2026-10-30T17:00:00Z")));
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("VALIDATION_FAILED", exception.ErrorCode);
    }

    [Theory]
    [InlineData("2026-11-12T16:59:59.9999999Z", true)]
    [InlineData("2026-11-12T17:00:00Z", false)]
    [InlineData("2026-11-12T17:00:00.0000001Z", false)]
    public void Cutoff_IsExclusive_WithoutChangingLifecycle(string now, bool expectedOpen)
    {
        var project = new Project { RecruitmentClosesAt = Utc("2026-11-12T17:00:00Z") };
        Assert.Equal(expectedOpen, RecruitmentDeadline.IsOpen(project, Utc(now)));
        Assert.Equal(0, RecruitmentDeadline.DaysLeft(project.RecruitmentClosesAt, Utc(now)));
        Assert.Equal("recruiting", project.Status);
    }

    [Theory]
    [InlineData("2026-11-11T16:59:59Z", 1)]
    [InlineData("2026-11-11T17:00:00Z", 0)]
    [InlineData("2026-11-12T17:00:00Z", 0)]
    public void DaysLeft_CountsCalendarDaysToClosingDate(string now, int expected) =>
        Assert.Equal(expected, RecruitmentDeadline.DaysLeft(Utc("2026-11-12T17:00:00Z"), Utc(now)));

    [Theory]
    [InlineData("2026-11-12T05:30:00Z", "2026-11-12")]
    [InlineData("2026-11-12T17:00:00Z", "2026-11-12")]
    [InlineData("2026-11-12T18:00:00Z", "2026-11-13")]
    public void LegacyMiddayCutoff_UsesItsLocalDate(string cutoff, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), RecruitmentDeadline.ClosingDate(Utc(cutoff)));

    [Theory]
    [InlineData("draft")]
    [InlineData("in_progress")]
    [InlineData("completed")]
    [InlineData("archived")]
    public void NonRecruitingProject_IsNotOpen(string status)
    {
        var project = new Project { Status = status, RecruitmentClosesAt = Utc("2027-01-01T17:00:00Z") };
        Assert.False(RecruitmentDeadline.IsOpen(project, Utc("2026-10-30T05:00:00Z")));
        project.Status = "recruiting";
        project.ArchivedAt = Utc("2026-10-29T05:00:00Z");
        Assert.False(RecruitmentDeadline.IsOpen(project, Utc("2026-10-30T05:00:00Z")));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    public async Task CreateAndEdit_AcceptPresetDates(int days)
    {
        var clock = new ProjectTestClock(Utc("2026-10-30T17:01:00Z"));
        var repo = new ProjectServiceTests.FakeProjectRepository();
        var service = new ProjectService(repo, clock);
        var date = new DateOnly(2026, 10, 31).AddDays(days);
        var created = await service.CreateProjectAsync(Guid.NewGuid(), ProjectServiceTests.CreateValidRequest() with { RecruitmentClosingDate = date });
        Assert.Equal(date, created.RecruitmentClosingDate);
        Assert.Equal(days, created.ClosingDaysLeft);
        Assert.True(created.IsRecruitmentOpen);

        var project = Assert.Single(repo.Projects);
        var edited = await service.UpdateProjectAsync(project.LeaderId, project.ProjectId,
            EditRequest(date.AddDays(1)));
        Assert.Equal(date.AddDays(1), edited.RecruitmentClosingDate);
        Assert.Equal(created.RecruitmentClosesAt.AddDays(1), edited.RecruitmentClosesAt);
    }

    [Fact]
    public async Task OmittedCreateDate_DefaultsTo14Days_AndOmittedEditPreservesLegacyInstant()
    {
        var repo = new ProjectServiceTests.FakeProjectRepository();
        var service = new ProjectService(repo, new ProjectTestClock(Utc("2026-10-30T17:01:00Z")));
        var created = await service.CreateProjectAsync(Guid.NewGuid(), ProjectServiceTests.CreateValidRequest());
        Assert.Equal(new DateOnly(2026, 11, 14), created.RecruitmentClosingDate);
        Assert.Equal(Utc("2026-11-14T17:00:00Z"), created.RecruitmentClosesAt);

        var project = Assert.Single(repo.Projects);
        project.RecruitmentClosesAt = Utc("2026-11-12T05:30:00Z");
        var edited = await service.UpdateProjectAsync(project.LeaderId, project.ProjectId, EditRequest());
        Assert.Equal(Utc("2026-11-12T05:30:00Z"), edited.RecruitmentClosesAt);
        Assert.Equal(new DateOnly(2026, 11, 12), edited.RecruitmentClosingDate);
    }

    [Fact]
    public async Task ExpiredProject_RemainsReadableAndEditable_AndExtensionReopensRecruitment()
    {
        var repo = new ProjectServiceTests.FakeProjectRepository();
        var clock = new ProjectTestClock(Utc("2026-10-30T05:00:00Z"));
        var service = new ProjectService(repo, clock);
        var created = await service.CreateProjectAsync(Guid.NewGuid(), ProjectServiceTests.CreateValidRequest());
        clock.UtcNow = new DateTimeOffset(created.RecruitmentClosesAt);
        var detail = await service.GetProjectByIdAsync(created.ProjectId);
        var card = Assert.Single((await service.GetProjectsAsync(new())).Items);
        Assert.False(detail.IsRecruitmentOpen);
        Assert.False(card.IsRecruitmentOpen);
        Assert.Equal("recruiting", detail.Status);
        Assert.Equal(0, detail.ClosingDaysLeft);

        var project = Assert.Single(repo.Projects);
        var extended = await service.UpdateProjectAsync(project.LeaderId, project.ProjectId,
            EditRequest(RecruitmentDeadline.LocalDate(clock.UtcNow.UtcDateTime).AddDays(7)));
        Assert.True(extended.IsRecruitmentOpen);
        Assert.Equal(7, extended.ClosingDaysLeft);
    }

    [Fact]
    public async Task InvalidCreateAndEdit_DoNotPersistOrMutateProject()
    {
        var repo = new ProjectServiceTests.FakeProjectRepository();
        var service = new ProjectService(repo, new ProjectTestClock(Utc("2026-10-30T17:00:00Z")));
        await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(Guid.NewGuid(),
            ProjectServiceTests.CreateValidRequest() with { RecruitmentClosingDate = new DateOnly(2026, 10, 31) }));
        Assert.Empty(repo.Projects);

        var created = await service.CreateProjectAsync(Guid.NewGuid(), ProjectServiceTests.CreateValidRequest());
        var project = Assert.Single(repo.Projects);
        await Assert.ThrowsAsync<ProjectException>(() => service.UpdateProjectAsync(project.LeaderId, project.ProjectId,
            EditRequest(new DateOnly(2026, 10, 31))));
        Assert.Equal(created.RecruitmentClosesAt, project.RecruitmentClosesAt);
        Assert.Equal(created.Name, project.Name);
    }

    internal static UpdateProjectRequest EditRequest(DateOnly? date = null) => new(
        "Edited Project", null, "A sufficiently long project description.", "Web Application", "intermediate", 10,
        "3 months", 5, "indigo", [], date);

    internal static DateTime Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
}

internal sealed class ProjectTestClock(DateTime utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(utcNow);
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
