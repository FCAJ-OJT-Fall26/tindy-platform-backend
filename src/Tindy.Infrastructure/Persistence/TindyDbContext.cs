using Tindy.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence;

public class TindyDbContext : DbContext
{
    public TindyDbContext(DbContextOptions<TindyDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<UserProfile> UserProfiles
        => Set<UserProfile>();

    public DbSet<ProfileExperience> ProfileExperiences => Set<ProfileExperience>();

    public DbSet<ProfileExperienceSkill> ProfileExperienceSkills => Set<ProfileExperienceSkill>();

    public DbSet<UserCertificate> UserCertificates => Set<UserCertificate>();

    public DbSet<Role> Roles
        => Set<Role>();

    public DbSet<Permission> Permissions
        => Set<Permission>();

    public DbSet<UserRole> UserRoles
        => Set<UserRole>();

    public DbSet<RolePermission> RolePermissions
        => Set<RolePermission>();

    public DbSet<UserCredential> UserCredentials
        => Set<UserCredential>();

    public DbSet<ExternalAuthAccount> ExternalAuthAccounts
        => Set<ExternalAuthAccount>();

    public DbSet<OAuthLoginTicket> OAuthLoginTickets => Set<OAuthLoginTicket>();
    public DbSet<OAuthLinkTransaction> OAuthLinkTransactions => Set<OAuthLinkTransaction>();


    public DbSet<ProfessionalRole> ProfessionalRoles
        => Set<ProfessionalRole>();

    public DbSet<SkillCategory> SkillCategories
        => Set<SkillCategory>();

    public DbSet<SkillCategorySkill> SkillCategorySkills
        => Set<SkillCategorySkill>();

    public DbSet<Skill> Skills
        => Set<Skill>();

    public DbSet<UserSkill> UserSkills
        => Set<UserSkill>();

    public DbSet<UserOnboardingStep> UserOnboardingSteps
        => Set<UserOnboardingStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TindyDbContext).Assembly);
    }
}