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

    internal static CreateProjectRequest CreateValidRequest() => new(
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
    public async Task CreateProject_Throws_WhenMaxCapacityLessThanOne()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var req = CreateValidRequest() with { MaxTeamCapacity = 0 };

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
        Assert.Equal(1, created.InitialTeamSize);
        var leader = Assert.Single(created.Members);
        Assert.True(leader.IsLeader);
        Assert.Equal(LeaderId, leader.UserId);
        Assert.Equal(created.ProjectId, leader.ProjectId);
        Assert.Equal("active", leader.Status);
        Assert.Equal(1, (await service.GetProjectByIdAsync(created.ProjectId)).CurrentTeamSize);
    }

    [Theory]
    [InlineData("  Develop REST APIs using .NET  ", "Develop REST APIs using .NET")]
    [InlineData(null, null)]
    [InlineData("   ", "")]
    public async Task CreateProject_PreservesOptionalPositionDescription(string? description, string? expected)
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var request = CreateValidRequest();
        var position = request.Positions[0] with { Description = description };

        var created = await service.CreateProjectAsync(LeaderId, request with { Positions = new[] { position } });

        Assert.Equal(expected, Assert.Single(Assert.Single(repo.Projects).Positions).Description);
        var detail = await service.GetProjectByIdAsync(created.ProjectId);
        Assert.Equal(expected, Assert.Single(detail.Positions).Description);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task CreateProject_RejectsInitialTeamSizeOtherThanOne(int initialTeamSize)
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId,
            CreateValidRequest() with { InitialTeamSize = initialTeamSize }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
        Assert.Contains("Initial team size must be 1", ex.Message);
        Assert.Empty(repo.Projects);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData(101)]
    public async Task CreateProject_RejectsInvalidPositionTitle(object title)
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var request = CreateValidRequest();
        var position = request.Positions[0] with { Title = title is int length ? new string('A', length) : (string)title };

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId,
            request with { Positions = new[] { position } }));

        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
        Assert.Empty(repo.Projects);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateProject_RejectsInvalidPositionRequiredCount(int requiredCount)
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var request = CreateValidRequest();

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId,
            request with { Positions = new[] { request.Positions[0] with { RequiredCount = requiredCount } } }));

        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
        Assert.Empty(repo.Projects);
    }

    [Fact]
    public async Task CreateProject_RejectsDuplicateSkillIds_BeforePersisting()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var request = CreateValidRequest();
        var position = request.Positions[0] with
        {
            Skills = new[] { new CreateProjectPositionSkillRequest(SkillId1, true), new CreateProjectPositionSkillRequest(SkillId1, false) }
        };

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(LeaderId,
            request with { Positions = new[] { position } }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
        Assert.Contains("unique", ex.Message);
        Assert.Empty(repo.Projects);
    }

    [Fact]
    public async Task CurrentTeamSize_UsesActiveMembers_InsteadOfHistoricalInitialTeamSize()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());
        var project = Assert.Single(repo.Projects);
        project.InitialTeamSize = 3;
        project.Members.Add(new ProjectMember { UserId = StrangerId, Status = "left" });

        Assert.Equal(1, (await service.GetProjectByIdAsync(created.ProjectId)).CurrentTeamSize);
        Assert.Equal(1, Assert.Single((await service.GetProjectsAsync(new())).Items).CurrentTeamSize);
        Assert.Equal(1, Assert.Single((await service.GetMyProjectsAsync(LeaderId)).LeadingProjects).CurrentTeamSize);
        Assert.Equal(3, project.InitialTeamSize);
    }

    [Fact]
    public void ProjectLeaderDto_HasOnlyPublicFields()
    {
        var properties = typeof(ProjectLeaderSummaryDto).GetProperties().Select(p => p.Name).OrderBy(name => name);
        Assert.Equal(new[] { "AvatarUrl", "DisplayName", "Initials", "UserId" }, properties);
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
        Assert.All(project.Positions, position => Assert.Equal("closed", position.Status));
        Assert.Single(repo.Projects);
    }

    [Fact]
    public async Task DeleteProject_RejectsStranger_WithoutArchivingOrClosingPositions()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.DeleteProjectAsync(StrangerId, created.ProjectId));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("PROJECT_ACCESS_DENIED", ex.ErrorCode);
        var project = Assert.Single(repo.Projects);
        Assert.Equal("recruiting", project.Status);
        Assert.Null(project.ArchivedAt);
        Assert.All(project.Positions, position => Assert.Equal("open", position.Status));
    }

    [Fact]
    public async Task ArchivedProject_CannotBeReadUpdatedOrArchivedAgain()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());
        await service.DeleteProjectAsync(LeaderId, created.ProjectId);
        var project = Assert.Single(repo.Projects);
        var archivedAt = project.ArchivedAt;
        var request = new UpdateProjectRequest("Changed Name", null, new string('D', 20), "Web", "intermediate", 10, "3 months", 5, null, []);

        foreach (var operation in new Func<Task>[]
        {
            () => service.GetProjectByIdAsync(created.ProjectId),
            () => service.UpdateProjectAsync(LeaderId, created.ProjectId, request),
            () => service.DeleteProjectAsync(LeaderId, created.ProjectId)
        })
        {
            var ex = await Assert.ThrowsAsync<ProjectException>(operation);
            Assert.Equal(404, ex.StatusCode);
            Assert.Equal("PROJECT_NOT_FOUND", ex.ErrorCode);
        }

        Assert.Equal("AI Support System", project.Name);
        Assert.Equal(archivedAt, project.ArchivedAt);
    }

    [Fact]
    public async Task UpdateProject_RespectsHistoricalInitialCapacityConstraint()
    {
        var repo = new FakeProjectRepository();
        var service = new ProjectService(repo);
        var created = await service.CreateProjectAsync(LeaderId, CreateValidRequest());
        var project = Assert.Single(repo.Projects);
        project.InitialTeamSize = 3;
        var request = new UpdateProjectRequest("Changed Name", null, new string('D', 20), "Web", "intermediate", 10, "3 months", 2, null, []);

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.UpdateProjectAsync(LeaderId, created.ProjectId, request));

        Assert.Equal("PROJECT_CAPACITY_EXCEEDED", ex.ErrorCode);
        Assert.Equal(3, project.InitialTeamSize);
        Assert.Equal(5, project.MaxTeamCapacity);
    }

    internal sealed class FakeProjectRepository : IProjectRepository
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
                    PrimaryEmail = "private-leader@example.com",
                    Profile = new UserProfile
                    {
                        UserId = p.LeaderId,
                        DisplayName = "Leader Name",
                        AvatarUrl = "https://example.com/avatar.png"
                    }
                };
            }
            if (p != null)
            {
                foreach (var position in p.Positions)
                {
                    position.Role = new ProfessionalRole { ProfessionalRoleId = position.RoleId, Name = "Backend Developer", IsActive = true };
                    foreach (var link in position.PositionSkills)
                    {
                        link.Skill = new Skill { SkillId = link.SkillId, Name = "Skill " + link.SkillId.ToString()[..4], IsActive = true };
                    }
                }
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

        public Task<IReadOnlyList<ProjectPosition>> GetPositionsByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            var project = Projects.FirstOrDefault(p => p.ProjectId == projectId);
            var positions = project?.Positions?.ToList() ?? new List<ProjectPosition>();
            return Task.FromResult<IReadOnlyList<ProjectPosition>>(positions);
        }

        public Task<ProjectPosition?> GetPositionWithDetailsAsync(Guid projectId, Guid positionId, CancellationToken cancellationToken = default)
        {
            var project = Projects.FirstOrDefault(p => p.ProjectId == projectId);
            var pos = project?.Positions?.FirstOrDefault(p => p.PositionId == positionId);
            return Task.FromResult(pos);
        }

        public Task<ProjectPosition> AddPositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
        {
            var project = Projects.FirstOrDefault(p => p.ProjectId == position.ProjectId);
            project?.Positions.Add(position);
            return Task.FromResult(position);
        }

        public Task UpdatePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeletePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
        {
            var project = Projects.FirstOrDefault(p => p.ProjectId == position.ProjectId);
            project?.Positions.Remove(position);
            return Task.CompletedTask;
        }

        public Task<Skill?> GetSkillByIdAsync(Guid skillId, CancellationToken cancellationToken = default)
        {
            if (ValidSkillIds.Contains(skillId))
            {
                return Task.FromResult<Skill?>(new Skill { SkillId = skillId, Name = "Skill " + skillId.ToString()[..4], IsActive = true });
            }
            return Task.FromResult<Skill?>(null);
        }

        public Task<ProjectPositionSkill?> GetPositionSkillAsync(Guid positionId, Guid skillId, CancellationToken cancellationToken = default)
        {
            foreach (var prj in Projects)
            {
                var pos = prj.Positions?.FirstOrDefault(p => p.PositionId == positionId);
                var link = pos?.PositionSkills?.FirstOrDefault(s => s.SkillId == skillId);
                if (link != null) return Task.FromResult<ProjectPositionSkill?>(link);
            }
            return Task.FromResult<ProjectPositionSkill?>(null);
        }

        public Task AddPositionSkillAsync(ProjectPositionSkill positionSkill, CancellationToken cancellationToken = default)
        {
            foreach (var prj in Projects)
            {
                var pos = prj.Positions?.FirstOrDefault(p => p.PositionId == positionSkill.PositionId);
                if (pos != null)
                {
                    pos.PositionSkills.Add(positionSkill);
                    break;
                }
            }
            return Task.CompletedTask;
        }

        public Task DeletePositionSkillAsync(ProjectPositionSkill positionSkill, CancellationToken cancellationToken = default)
        {
            foreach (var prj in Projects)
            {
                var pos = prj.Positions?.FirstOrDefault(p => p.PositionId == positionSkill.PositionId);
                if (pos != null)
                {
                    pos.PositionSkills.Remove(positionSkill);
                    break;
                }
            }
            return Task.CompletedTask;
        }
    }
}
