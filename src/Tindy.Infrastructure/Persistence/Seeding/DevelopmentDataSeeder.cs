using Microsoft.EntityFrameworkCore;
using Tindy.Application.Constants;
using Tindy.Application.Entities;
using Tindy.Application.Services;
using Tindy.Infrastructure.Services;

namespace Tindy.Infrastructure.Persistence.Seeding;

/// <summary>Opt-in synthetic development data. Invoke through the EF migration seed lock.</summary>
public static class DevelopmentDataSeeder
{
    public const int UserCount = 20;
    public const int ProjectCount = 50;

    public static void Seed(DbContext context, DevelopmentSeedOptions options) =>
        SeedAsync(context, options).GetAwaiter().GetResult();

    public static async Task SeedAsync(DbContext context, DevelopmentSeedOptions options, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return;
        var userIds = Enumerable.Range(1, UserCount).Select(i => DevelopmentSeedData.Id(1, i)).ToArray();
        var projectIds = Enumerable.Range(1, ProjectCount).Select(i => DevelopmentSeedData.Id(2, i)).ToArray();
        var existingUsers = await context.Set<User>().AsNoTracking().Where(user => userIds.Contains(user.UserId))
            .Select(user => user.UserId).ToListAsync(cancellationToken);
        var existingProjects = await context.Set<Project>().AsNoTracking().Where(project => projectIds.Contains(project.ProjectId))
            .Select(project => project.ProjectId).ToListAsync(cancellationToken);
        if (existingUsers.Count == UserCount && existingProjects.Count == ProjectCount) return;

        // Do not adopt/reset another account that happens to use a reserved demo name/email.
        var usernames = Enumerable.Range(1, UserCount).Select(DevelopmentSeedData.Username).ToArray();
        var emails = Enumerable.Range(1, UserCount).Select(DevelopmentSeedData.Email).ToArray();
        if (await context.Set<User>().AnyAsync(user => !userIds.Contains(user.UserId) &&
                (usernames.Contains(user.UsernameNormalized!) || emails.Contains(user.PrimaryEmailNormalized!)), cancellationToken) ||
            await context.Set<UserCredential>().AnyAsync(credential => emails.Contains(credential.EmailNormalized) && !userIds.Contains(credential.UserId), cancellationToken))
            throw new InvalidOperationException("A reserved demo username/email belongs to another user. Nothing was replaced; resolve the collision before enabling DevelopmentSeed.");

        var roles = await context.Set<ProfessionalRole>().AsNoTracking().Where(role => role.IsActive)
            .ToDictionaryAsync(role => role.NameNormalized, role => role.ProfessionalRoleId, cancellationToken);
        var skills = await context.Set<Skill>().AsNoTracking().Where(skill => skill.IsActive)
            .ToDictionaryAsync(skill => skill.NameNormalized, skill => skill.SkillId, cancellationToken);
        var memberRoleId = await context.Set<Role>().Where(role => role.RoleName == RoleNames.Member)
            .Select(role => role.RoleId).SingleAsync(cancellationToken);
        var existingUserIds = existingUsers.ToHashSet();
        var existingProjectIds = existingProjects.ToHashSet();
        var now = DateTime.UtcNow;
        var hasher = new PasswordHasher();

        // One SaveChanges transaction covers every new user/profile/experience/certificate/project graph.
        // Existing roots are skipped, including edited profiles, changed passwords and archived Projects.
        foreach (var ordinal in Enumerable.Range(1, UserCount))
        {
            if (existingUserIds.Contains(DevelopmentSeedData.Id(1, ordinal))) continue;
            context.Set<User>().Add(CreateUser(ordinal, memberRoleId, roles, skills, hasher.HashPassword(options.Password), now));
        }
        foreach (var ordinal in Enumerable.Range(1, ProjectCount))
        {
            if (existingProjectIds.Contains(DevelopmentSeedData.Id(2, ordinal))) continue;
            context.Set<Project>().Add(CreateProject(ordinal, roles, skills, now));
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private static User CreateUser(int ordinal, Guid memberRoleId, Dictionary<string, Guid> roles,
        Dictionary<string, Guid> skills, string passwordHash, DateTime now)
    {
        var persona = DevelopmentSeedData.Personas[ordinal - 1];
        var id = DevelopmentSeedData.Id(1, ordinal);
        var createdAt = now.AddDays(-45 - ordinal);
        var user = new User
        {
            UserId = id, Username = DevelopmentSeedData.Username(ordinal), UsernameNormalized = DevelopmentSeedData.Username(ordinal),
            PrimaryEmail = DevelopmentSeedData.Email(ordinal), PrimaryEmailNormalized = DevelopmentSeedData.Email(ordinal),
            Status = "active", IsOnboardingCompleted = true, CreatedAt = createdAt,
            Credential = new UserCredential
            {
                UserCredentialId = DevelopmentSeedData.Id(3, ordinal), UserId = id,
                Email = DevelopmentSeedData.Email(ordinal), EmailNormalized = DevelopmentSeedData.Email(ordinal),
                PasswordHash = passwordHash, CreatedAt = createdAt
            },
            Profile = new UserProfile
            {
                UserId = id, DisplayName = persona.Name + " (Demo)", Headline = persona.Role + " · Tindy Demo Lab",
                Bio = $"Hồ sơ demo phục vụ kiểm thử Tindy. Quan tâm đến {string.Join(", ", persona.Skills.Take(3))}; mong muốn cộng tác ở vị trí {persona.Role}.",
                University = new[] { "FPT University", "Đại học Bách khoa", "Đại học Công nghệ Thông tin", "Đại học Cần Thơ" }[(ordinal - 1) % 4],
                Location = new[] { "Hồ Chí Minh, Việt Nam", "Hà Nội, Việt Nam", "Đà Nẵng, Việt Nam", "Cần Thơ, Việt Nam" }[(ordinal - 1) % 4],
                CurrentRole = persona.Role, PreferredRoleId = roles[CatalogSeedData.Normalize(persona.Role)],
                AvailableHoursPerWeek = new[] { 5, 8, 10, 12, 15, 20, 25 }[(ordinal - 1) % 7],
                AvatarSource = "default", CreatedAt = createdAt,
                ProfessionalInterests = "Ứng dụng cộng đồng, mã nguồn mở và sản phẩm cho sinh viên.",
                LearningGoals = "Rèn luyện làm việc nhóm, phát triển sản phẩm và kiểm thử tự động.",
                PreviousProjectsAndContribution = "Đã tham gia hai dự án thực hành tại Tindy Demo Lab; chi tiết trong Experiences.",
                TechnologiesToLearn = "Docker, CI/CD và thiết kế hệ thống.",
                UserSkills = persona.Skills.Select(name => new UserSkill { UserId = id, SkillId = skills[CatalogSeedData.Normalize(name)] }).ToList()
            },
            UserRoles = [new UserRole { UserId = id, RoleId = memberRoleId }],
            OnboardingSteps = Enumerable.Range(1, 7).Select(step => new UserOnboardingStep { UserId = id, StepNumber = step, CompletedAt = createdAt.AddHours(step) }).ToList()
        };

        var month = new DateOnly(now.Year, now.Month, 1);
        for (var index = 0; index < 2; index++)
        {
            var experienceId = DevelopmentSeedData.Id(4, ordinal * 10 + index);
            user.ProfileExperiences.Add(new ProfileExperience
            {
                ExperienceId = experienceId, UserId = id,
                ProjectName = index == 0 ? "Tindy Demo Lab — Community Platform" : "Tindy Demo Lab — Team Delivery Workshop",
                RoleTitle = persona.Role,
                Description = $"Kinh nghiệm mô phỏng cho kiểm thử, không phải lịch sử tuyển dụng thật. Thực hành {string.Join(", ", persona.Skills.Take(3))}, review yêu cầu và bàn giao theo sprint.",
                StartDate = month.AddMonths(index == 0 ? -12 : -5), EndDate = index == 0 ? month.AddMonths(-6) : null,
                IsOngoing = index == 1, CreatedAt = createdAt,
                ExperienceSkills = persona.Skills.Take(3).Select(name => new ProfileExperienceSkill { ExperienceId = experienceId, SkillId = skills[CatalogSeedData.Normalize(name)] }).ToList()
            });
        }
        for (var index = 0; index < (ordinal % 2 == 0 ? 2 : 1); index++)
            user.Certificates.Add(new UserCertificate
            {
                CertificateId = DevelopmentSeedData.Id(5, ordinal * 10 + index), UserId = id,
                Title = index == 0 ? $"{persona.Role} Foundations (Demo)" : "Agile Team Delivery Workshop (Demo)",
                Issuer = "Tindy Demo Academy", Description = "Chứng chỉ giả lập để kiểm thử hồ sơ; không đại diện cho chứng nhận thật. Không có file đính kèm.",
                CreatedAt = createdAt.AddDays(10)
            });
        return user;
    }

    private static Project CreateProject(int ordinal, Dictionary<string, Guid> roles, Dictionary<string, Guid> skills, DateTime now)
    {
        var track = DevelopmentSeedData.Tracks[(ordinal - 1) / 5];
        var title = track.Titles[(ordinal - 1) % 5];
        var id = DevelopmentSeedData.Id(2, ordinal);
        var leaderId = DevelopmentSeedData.Id(1, (ordinal - 1) % 6 + 1);
        var createdAt = now.AddHours(-ordinal * 3);
        var project = new Project
        {
            ProjectId = id, LeaderId = leaderId, Name = title + " [Demo]",
            Tagline = $"Cùng xây dựng {title} từ ý tưởng đến MVP.",
            Description = $"Dự án demo {title} thuộc lĩnh vực {track.Category}, dành cho đội sinh viên thực hành sản phẩm thực tế. " +
                $"Nhóm tìm thành viên sử dụng {string.Join(", ", track.Required.Concat(track.Optional))}. " +
                "Làm việc theo sprint, review code và chia sẻ kết quả hằng tuần. Đây là dữ liệu mẫu trong database để thử Discovery, Apply và Leader Approval.",
            Category = track.Category, DifficultyLevel = new[] { "beginner_friendly", "intermediate", "advanced" }[(ordinal - 1) % 3],
            ExpectedCommitmentHours = new[] { 5, 8, 10, 12, 15, 20 }[(ordinal - 1) % 6],
            Duration = new[] { "1 month", "2 months", "3 months", "6 months" }[(ordinal - 1) % 4],
            InitialTeamSize = 1, MaxTeamCapacity = 7, Status = "recruiting", CreatedAt = createdAt,
            RecruitmentClosesAt = RecruitmentDeadline.CreateCutoff(RecruitmentDeadline.LocalDate(now).AddDays(new[] { 7, 14, 30, 45 }[(ordinal - 1) % 4]), now),
            ColorTheme = new[] { "indigo", "teal", "sky", "amber", "rose" }[(ordinal - 1) % 5],
            Goals = ["Bàn giao MVP có thể sử dụng", "Kiểm thử các luồng chính và review theo sprint", "Viết tài liệu để thành viên mới có thể tiếp tục"],
            Members = [new ProjectMember { ProjectId = id, UserId = leaderId, RoleTitle = "Project Leader", IsLeader = true, Status = "active", JoinedAt = createdAt }]
        };
        var partnerRole = track.Role == "Frontend Developer" ? "Backend Developer" : "Frontend Developer";
        AddPosition(0, track.Role, track.Required, track.Optional);
        AddPosition(1, partnerRole, partnerRole == "Backend Developer" ? ["C#", "ASP.NET Core"] : ["React", "TypeScript"], ["Git", "Docker"]);
        AddPosition(2, "QA Engineer", ["Postman", "Test Planning"], ["Playwright", "SQL"]);
        return project;

        void AddPosition(int index, string role, string[] required, string[] optional)
        {
            var positionId = DevelopmentSeedData.Id(6, ordinal * 10 + index);
            project.Positions.Add(new ProjectPosition
            {
                PositionId = positionId, ProjectId = id, RoleId = roles[CatalogSeedData.Normalize(role)], Title = role,
                Description = $"Tham gia {title}; trao đổi tiến độ và review theo sprint. Vị trí demo để kiểm thử ứng tuyển.",
                RequiredCount = 2, FilledCount = 0, ExperienceLevel = project.DifficultyLevel, Status = "open", CreatedAt = createdAt,
                PositionSkills = required.Select(name => PositionSkill(name, true)).Concat(optional.Select(name => PositionSkill(name, false))).ToList()
            });

            ProjectPositionSkill PositionSkill(string name, bool requiredSkill) => new()
            {
                PositionId = positionId, SkillId = skills[CatalogSeedData.Normalize(name)], IsRequired = requiredSkill
            };
        }
    }
}
