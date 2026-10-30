using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class ProjectPositionServiceTests
{
    private static readonly Guid LeaderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid StrangerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid RoleId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid SkillId1 = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid SkillId2 = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static (Project Project, ProjectPosition Position) CreateTestProject()
    {
        var projectId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        var position = new ProjectPosition
        {
            PositionId = positionId,
            ProjectId = projectId,
            RoleId = RoleId,
            Title = "Backend Developer",
            Description = "Initial position description",
            RequiredCount = 2,
            FilledCount = 0,
            ExperienceLevel = "intermediate",
            Status = "open",
            CreatedAt = DateTime.UtcNow,
            PositionSkills = new List<ProjectPositionSkill>()
        };

        var project = new Project
        {
            ProjectId = projectId,
            LeaderId = LeaderId,
            Name = "AI Support System",
            Description = "Detailed description of the AI support system with RAG platform.",
            Category = "AI & Machine Learning",
            Status = "recruiting",
            CreatedAt = DateTime.UtcNow,
            Positions = new List<ProjectPosition> { position },
            Members = new List<ProjectMember>
            {
                new ProjectMember { ProjectId = projectId, UserId = LeaderId, IsLeader = true, Status = "active" }
            }
        };

        position.Project = project;
        return (project, position);
    }

    [Fact]
    public async Task GetPositions_ReturnsPositions_WhenProjectExists()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var positions = await service.GetPositionsAsync(project.ProjectId);

        Assert.Single(positions);
        Assert.Equal(position.PositionId, positions[0].PositionId);
        Assert.Equal("Backend Developer", positions[0].Title);
    }

    [Fact]
    public async Task GetPositions_Throws_WhenProjectNotFound()
    {
        var repo = new FakeRepo();
        var service = new ProjectPositionService(repo);

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.GetPositionsAsync(Guid.NewGuid()));
        Assert.Equal("PROJECT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task CreatePosition_Succeeds_WhenLeader()
    {
        var (project, _) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new CreatePositionRequest(
            RoleId: RoleId,
            Title: "Frontend Developer",
            RequiredCount: 1,
            ExperienceLevel: "intermediate",
            Description: "React UI development");

        var result = await service.CreatePositionAsync(LeaderId, project.ProjectId, req);

        Assert.Equal("Frontend Developer", result.Title);
        Assert.Equal("React UI development", result.Description);
        Assert.Equal(1, result.RequiredCount);
        Assert.Equal("open", result.Status);
        Assert.Equal(2, project.Positions.Count);
    }

    [Fact]
    public async Task CreatePosition_Throws_WhenNonLeader()
    {
        var (project, _) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new CreatePositionRequest(RoleId, "Frontend Developer");

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreatePositionAsync(StrangerId, project.ProjectId, req));
        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task CreatePosition_Throws_WhenRoleNotFound()
    {
        var (project, _) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new CreatePositionRequest(Guid.NewGuid(), "Frontend Developer");

        var ex = await Assert.ThrowsAsync<ProjectException>(() => service.CreatePositionAsync(LeaderId, project.ProjectId, req));
        Assert.Equal("ROLE_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdatePosition_Succeeds_WhenLeader()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new UpdatePositionRequest(
            Title: "Lead Backend Developer",
            RequiredCount: 3,
            ExperienceLevel: "advanced",
            Status: "filled",
            Description: "Updated desc");

        var updated = await service.UpdatePositionAsync(LeaderId, project.ProjectId, position.PositionId, req);

        Assert.Equal("Lead Backend Developer", updated.Title);
        Assert.Equal(3, updated.RequiredCount);
        Assert.Equal("advanced", updated.ExperienceLevel);
        Assert.Equal("filled", updated.Status);
    }

    [Fact]
    public async Task UpdatePosition_Throws_WhenNonLeader()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new UpdatePositionRequest("New Title", null, null, null, null);

        var ex = await Assert.ThrowsAsync<ProjectException>(() =>
            service.UpdatePositionAsync(StrangerId, project.ProjectId, position.PositionId, req));
        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdatePosition_Throws_WhenInvalidStatus()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new UpdatePositionRequest(null, null, null, "invalid_status", null);

        var ex = await Assert.ThrowsAsync<ProjectException>(() =>
            service.UpdatePositionAsync(LeaderId, project.ProjectId, position.PositionId, req));
        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
    }

    [Fact]
    public async Task DeletePosition_Succeeds_WhenLeader()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        await service.DeletePositionAsync(LeaderId, project.ProjectId, position.PositionId);

        Assert.Empty(project.Positions);
    }

    [Fact]
    public async Task DeletePosition_Throws_WhenNonLeader()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var ex = await Assert.ThrowsAsync<ProjectException>(() =>
            service.DeletePositionAsync(StrangerId, project.ProjectId, position.PositionId));
        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task AddPositionSkill_Succeeds_AndLinksSkill()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new AddPositionSkillRequest(SkillId1, IsRequired: true);

        var res = await service.AddPositionSkillAsync(LeaderId, project.ProjectId, position.PositionId, req);

        Assert.Equal(position.PositionId, res.PositionId);
        Assert.Equal(SkillId1, res.SkillId);
        Assert.True(res.IsRequired);
        Assert.Single(position.PositionSkills);
    }

    [Fact]
    public async Task AddPositionSkill_Throws_WhenSkillNotFound()
    {
        var (project, position) = CreateTestProject();
        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        var req = new AddPositionSkillRequest(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ProjectException>(() =>
            service.AddPositionSkillAsync(LeaderId, project.ProjectId, position.PositionId, req));
        Assert.Equal("SKILL_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task RemovePositionSkill_Succeeds_WhenLeader()
    {
        var (project, position) = CreateTestProject();
        position.PositionSkills.Add(new ProjectPositionSkill
        {
            PositionId = position.PositionId,
            SkillId = SkillId1,
            IsRequired = true
        });

        var repo = new FakeRepo(project);
        var service = new ProjectPositionService(repo);

        await service.RemovePositionSkillAsync(LeaderId, project.ProjectId, position.PositionId, SkillId1);

        Assert.Empty(position.PositionSkills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PositionMutations_RejectStrangersAndArchivedProjects(bool archived)
    {
        var (project, position) = CreateTestProject();
        position.PositionSkills.Add(new ProjectPositionSkill { PositionId = position.PositionId, SkillId = SkillId1, IsRequired = true });
        if (archived) project.Status = "archived";
        var service = new ProjectPositionService(new FakeRepo(project));
        var callerId = archived ? LeaderId : StrangerId;

        foreach (var operation in new Func<Task>[]
        {
            () => service.CreatePositionAsync(callerId, project.ProjectId, new CreatePositionRequest(RoleId, "New Position")),
            () => service.UpdatePositionAsync(callerId, project.ProjectId, position.PositionId, new UpdatePositionRequest("Changed Title", null, null, null, null)),
            () => service.DeletePositionAsync(callerId, project.ProjectId, position.PositionId),
            () => service.AddPositionSkillAsync(callerId, project.ProjectId, position.PositionId, new AddPositionSkillRequest(SkillId2)),
            () => service.RemovePositionSkillAsync(callerId, project.ProjectId, position.PositionId, SkillId1)
        })
        {
            var ex = await Assert.ThrowsAsync<ProjectException>(operation);
            Assert.Equal(archived ? 404 : 403, ex.StatusCode);
            Assert.Equal(archived ? "PROJECT_NOT_FOUND" : "FORBIDDEN", ex.ErrorCode);
        }

        Assert.Same(position, Assert.Single(project.Positions));
        Assert.Equal("Backend Developer", position.Title);
        Assert.Equal(SkillId1, Assert.Single(position.PositionSkills).SkillId);
    }

    [Fact]
    public async Task AddPositionSkill_UpdatesExistingLink_WithoutDuplicatingIt()
    {
        var (project, position) = CreateTestProject();
        var service = new ProjectPositionService(new FakeRepo(project));
        await service.AddPositionSkillAsync(LeaderId, project.ProjectId, position.PositionId, new AddPositionSkillRequest(SkillId1));

        var result = await service.AddPositionSkillAsync(LeaderId, project.ProjectId, position.PositionId,
            new AddPositionSkillRequest(SkillId1, false, "advanced"));

        var link = Assert.Single(position.PositionSkills);
        Assert.False(result.IsRequired);
        Assert.False(link.IsRequired);
        Assert.Equal("advanced", link.MinimumExperienceLevel);
    }

    [Theory]
    [InlineData("create", 1)]
    [InlineData("create", 101)]
    [InlineData("update", 1)]
    [InlineData("update", 101)]
    public async Task PositionTitle_RespectsLengthConstraints(string operation, int length)
    {
        var (project, position) = CreateTestProject();
        var service = new ProjectPositionService(new FakeRepo(project));
        var title = new string('A', length);

        var ex = await Assert.ThrowsAsync<ProjectException>(() => operation == "create"
            ? service.CreatePositionAsync(LeaderId, project.ProjectId, new CreatePositionRequest(RoleId, title))
            : service.UpdatePositionAsync(LeaderId, project.ProjectId, position.PositionId, new UpdatePositionRequest(title, null, null, null, null)));

        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PositionRequiredCount_MustBePositive(int requiredCount)
    {
        var (project, position) = CreateTestProject();
        var service = new ProjectPositionService(new FakeRepo(project));

        var createError = await Assert.ThrowsAsync<ProjectException>(() => service.CreatePositionAsync(LeaderId,
            project.ProjectId, new CreatePositionRequest(RoleId, "New Position", requiredCount)));
        var updateError = await Assert.ThrowsAsync<ProjectException>(() => service.UpdatePositionAsync(LeaderId,
            project.ProjectId, position.PositionId, new UpdatePositionRequest(null, requiredCount, null, null, null)));

        Assert.Equal("VALIDATION_FAILED", createError.ErrorCode);
        Assert.Equal("VALIDATION_FAILED", updateError.ErrorCode);
    }

    [Fact]
    public async Task PositionFromAnotherProject_CannotBeChangedThroughThisProject()
    {
        var (project, _) = CreateTestProject();
        var service = new ProjectPositionService(new FakeRepo(project));
        var unrelatedPositionId = Guid.NewGuid();

        foreach (var operation in new Func<Task>[]
        {
            () => service.UpdatePositionAsync(LeaderId, project.ProjectId, unrelatedPositionId, new UpdatePositionRequest("Changed", null, null, null, null)),
            () => service.DeletePositionAsync(LeaderId, project.ProjectId, unrelatedPositionId),
            () => service.AddPositionSkillAsync(LeaderId, project.ProjectId, unrelatedPositionId, new AddPositionSkillRequest(SkillId1)),
            () => service.RemovePositionSkillAsync(LeaderId, project.ProjectId, unrelatedPositionId, SkillId1)
        })
        {
            var ex = await Assert.ThrowsAsync<ProjectException>(operation);
            Assert.Equal(404, ex.StatusCode);
            Assert.Equal("POSITION_NOT_FOUND", ex.ErrorCode);
        }
    }

    private sealed class FakeRepo : IProjectRepository
    {
        public List<Project> Projects { get; } = new();
        public Guid ValidRoleId { get; set; } = RoleId;
        public List<Guid> ValidSkillIds { get; set; } = new() { SkillId1, SkillId2 };

        public FakeRepo(Project? initialProject = null)
        {
            if (initialProject != null) Projects.Add(initialProject);
        }

        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(Projects.FirstOrDefault(p => p.ProjectId == projectId));

        public Task<Project?> GetByIdWithDetailsAsync(Guid projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(Projects.FirstOrDefault(p => p.ProjectId == projectId));

        public Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
        {
            Projects.Add(project);
            return Task.FromResult(project);
        }

        public Task UpdateAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<(IReadOnlyList<Project> Items, int TotalCount)> GetPagedProjectsAsync(GetProjectsQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<Project> Items, int TotalCount)>((Projects, Projects.Count));

        public Task<IReadOnlyList<Project>> GetLeadingProjectsAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Project>>(Projects.Where(p => p.LeaderId == userId).ToList());

        public Task<IReadOnlyList<ProjectMember>> GetJoinedMembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectMember>>(new List<ProjectMember>());

        public Task<bool> RoleExistsAsync(Guid roleId, CancellationToken cancellationToken = default)
            => Task.FromResult(roleId == ValidRoleId);

        public Task<IReadOnlyList<Guid>> ValidateSkillIdsAsync(IEnumerable<Guid> skillIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>>(skillIds.Where(id => ValidSkillIds.Contains(id)).ToList());

        public Task<IReadOnlyList<ProjectPosition>> GetPositionsByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            var p = Projects.FirstOrDefault(x => x.ProjectId == projectId);
            var list = p?.Positions?.ToList() ?? new List<ProjectPosition>();
            return Task.FromResult<IReadOnlyList<ProjectPosition>>(list);
        }

        public Task<ProjectPosition?> GetPositionWithDetailsAsync(Guid projectId, Guid positionId, CancellationToken cancellationToken = default)
        {
            var p = Projects.FirstOrDefault(x => x.ProjectId == projectId);
            var pos = p?.Positions?.FirstOrDefault(x => x.PositionId == positionId);
            return Task.FromResult(pos);
        }

        public Task<ProjectPosition> AddPositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
        {
            var p = Projects.FirstOrDefault(x => x.ProjectId == position.ProjectId);
            p?.Positions.Add(position);
            return Task.FromResult(position);
        }

        public Task UpdatePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeletePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
        {
            var p = Projects.FirstOrDefault(x => x.ProjectId == position.ProjectId);
            p?.Positions.Remove(position);
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
            foreach (var p in Projects)
            {
                var pos = p.Positions?.FirstOrDefault(x => x.PositionId == positionId);
                var link = pos?.PositionSkills?.FirstOrDefault(x => x.SkillId == skillId);
                if (link != null) return Task.FromResult<ProjectPositionSkill?>(link);
            }
            return Task.FromResult<ProjectPositionSkill?>(null);
        }

        public Task AddPositionSkillAsync(ProjectPositionSkill positionSkill, CancellationToken cancellationToken = default)
        {
            foreach (var p in Projects)
            {
                var pos = p.Positions?.FirstOrDefault(x => x.PositionId == positionSkill.PositionId);
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
            foreach (var p in Projects)
            {
                var pos = p.Positions?.FirstOrDefault(x => x.PositionId == positionSkill.PositionId);
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
