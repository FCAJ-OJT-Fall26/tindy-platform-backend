using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class ProjectServiceTests
{
    private static readonly Guid LeaderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid StrangerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid RoleId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid SkillId1 = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid SkillId2 = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static CreateProjectRequest CreateValidRequest() => new(
        Name: "AI Support System",
        Tagline: "Smart answers",
        Description: "This is a detailed description of the AI support system with RAG platform.",
        Category: "AI & Machine Learning",
        DifficultyLevel: "intermediate",
        ExpectedCommitmentHours: 10,
        Duration: "3 months",
        InitialTeamSize: 1,
        MaxTeamCapacity: 5,
        ColorTheme: "indigo",
        RecruitmentClosingDays: 14,
        Goals: new[] { "Goal 1", "Goal 2" },
        Positions: new[]
        {
            new CreateProjectPositionRequest(
                RoleId: RoleId,
                Title: "Backend Developer",
                RequiredCount: 2,
                ExperienceLevel: "intermediate",
                Skills: new[]
                {
                    new CreateProjectPositionSkillRequest(SkillId1, true),
                    new CreateProjectPositionSkillRequest(SkillId2, false)
                })
        });

    [Fact]
    public async Task CreateProject_Throws_WhenNameTooShort()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var req = CreateValidRequest() with { Name = "AI" };

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId, req));
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProject_Throws_WhenDescriptionTooShort()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var req = CreateValidRequest() with { Description = "Short desc" };

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId, req));
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProject_Throws_WhenHoursOutOfRange()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var req = CreateValidRequest() with { ExpectedCommitmentHours = 50 };

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId, req));
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProject_Throws_WhenMaxCapacityLessThanInitial()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var req = CreateValidRequest() with { InitialTeamSize = 3, MaxTeamCapacity = 2 };

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId, req));
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProject_Throws_WhenNoPositions()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var req = CreateValidRequest() with { Positions = Array.Empty<CreateProjectPositionRequest>() };

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId, req));
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProject_Throws_WhenRoleNotFound()
    {
        var repo = new FakeProjectRepository { ValidRoleId = Guid.NewGuid() };
        var service = new ProjectService(repo);
        var req = CreateValidRequest();

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId, req));
        Assert.Equal("ROLE_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProject_Throws_WhenSkillNotFound()
    {
        var repo = new FakeProjectRepository { ValidSkillIds = new List<Guid> { SkillId1 } }; // Missing SkillId2
        var service = new ProjectService(repo);
        var req = CreateValidRequest();

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId, req));
        Assert.Equal("SKILL_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProject_Succeeds_WhenValid()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var req = CreateValidRequest();

        var result = await service.CreateProjectAsync(LeaderId, req);

        Assert.NotNull(result);
        Assert.Equal("AI Support System", result.Name);
        Assert.Equal("recruiting", result.Status);
        Assert.Equal(1, result.CurrentTeamSize);
        Assert.Equal(1, result.PositionsCount);
        Assert.Single(repo.Projects);
        var created = repo.Projects[0];
        Assert.Single(created.Members);
        Assert.True(created.Members.First().IsLeader);
    }

    [Fact]
    public async Task GetProjectById_Throws_WhenNotFound()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.GetProjectByIdAsync(Guid.NewGuid()));
        Assert.Equal("PROJECT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateProject_Throws_WhenStrangerCalls()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());

        var updateReq = new UpdateProjectRequest(
            Name: "Updated Name",
            Tagline: null,
            Description: "Updated long enough description for the project test.",
            Category: "AI & Machine Learning",
            DifficultyLevel: "advanced",
            ExpectedCommitmentHours: 12,
            Duration: "4 months",
            MaxTeamCapacity: 6,
            ColorTheme: "blue",
            Goals: new[] { "New Goal" });

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.UpdateProjectAsync(StrangerId, created.ProjectId, updateReq));
        Assert.Equal("PROJECT_ACCESS_DENIED", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateProject_Throws_WhenCapacitySmallerThanTeam()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());

        var updateReq = new UpdateProjectRequest(
            Name: "Updated Name",
            Tagline: null,
            Description: "Updated long enough description for the project test.",
            Category: "AI & Machine Learning",
            DifficultyLevel: "advanced",
            ExpectedCommitmentHours: 12,
            Duration: "4 months",
            MaxTeamCapacity: 0, // Less than current team size of 1
            ColorTheme: "blue",
            Goals: new[] { "New Goal" });

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.UpdateProjectAsync(LeaderId, created.ProjectId, updateReq));
        Assert.Equal("PROJECT_CAPACITY_EXCEEDED", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateProject_Succeeds_WhenValid()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());

        var updateReq = new UpdateProjectRequest(
            Name: "Updated Name",
            Tagline: "Updated Tag",
            Description: "Updated long enough description for the project test.",
            Category: "Web Application",
            DifficultyLevel: "advanced",
            ExpectedCommitmentHours: 15,
            Duration: "6 months",
            MaxTeamCapacity: 8,
            ColorTheme: "rose",
            Goals: new[] { "Goal Alpha" });

        var updated = await service.UpdateProjectAsync(LeaderId, created.ProjectId, updateReq);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Web Application", updated.Category);
        Assert.Equal(8, updated.MaxTeamCapacity);
    }

    [Fact]
    public async Task DeleteProject_ArchivesProject_WhenLeaderCalls()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());

        await service.DeleteProjectAsync(LeaderId, created.ProjectId);

        var project = repo.Projects.First(p => p.ProjectId == created.ProjectId);
        Assert.Equal("archived", project.Status);
        Assert.NotNull(project.ArchivedAt);
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        public List<Project> Projects { get; } = new();
        public Guid ValidRoleId { get; set; } = RoleId;
        public List<Guid> ValidSkillIds { get; set; } = new() { SkillId1, SkillId2 };

        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Projects.FirstOrDefault(p => p.ProjectId == projectId));
        }

        public Task<Project?> GetByIdWithDetailsAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            var p = Projects.FirstOrDefault(x => x.ProjectId == projectId);
            if (p != null && p.Leader == null)
            {
                p.Leader = new User
                {
                    UserId = p.LeaderId,
                    Username = "leader_user",
                    Profile = new UserProfile
                    {
                        UserId = p.LeaderId,
                        DisplayName = "Leader Name",
                        AvatarUrl = "https://example.com/avatar.png"
                    }
                };
            }
            return Task.FromResult(p);
        }

        public Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
        {
            Projects.Add(project);
            return Task.FromResult(project);
        }

        public Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<Project> Items, int TotalCount)> GetPagedProjectsAsync(GetProjectsQuery query, CancellationToken cancellationToken = default)
        {
            var active = Projects.Where(p => p.Status != "archived").ToList();
            return Task.FromResult< (IReadOnlyList<Project> Items, int TotalCount)>((active, active.Count));
        }

        public Task<IReadOnlyList<Project>> GetLeadingProjectsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var leading = Projects.Where(p => p.LeaderId == userId && p.Status != "archived").ToList();
            return Task.FromResult<IReadOnlyList<Project>>(leading);
        }

        public Task<IReadOnlyList<ProjectMember>> GetJoinedMembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var list = new List<ProjectMember>();
            return Task.FromResult<IReadOnlyList<ProjectMember>>(list);
        }

        public Task<bool> RoleExistsAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(roleId == ValidRoleId);
        }

        public Task<IReadOnlyList<Guid>> ValidateSkillIdsAsync(IEnumerable<Guid> skillIds, CancellationToken cancellationToken = default)
        {
            var valid = skillIds.Where(id => ValidSkillIds.Contains(id)).ToList();
            return Task.FromResult<IReadOnlyList<Guid>>(valid);
        }
    }
}
