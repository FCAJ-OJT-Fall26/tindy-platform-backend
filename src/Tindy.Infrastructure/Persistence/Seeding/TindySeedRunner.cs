using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class TindySeedRunner
{
    public static async Task<bool> SeedFreshDemoAsync(TindyDbContext context, IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var options = DevelopmentSeedOptions.ForFreshDemo(configuration);
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (!context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal).SetEquals(appliedMigrations))
            throw new InvalidOperationException("The database schema must match this release before running the demo seed.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Keep users and projects stable between checking freshness and committing the seed.
        await context.Database.ExecuteSqlRawAsync(
            "LOCK TABLE users, projects IN SHARE ROW EXCLUSIVE MODE", cancellationToken);

        if (await context.Users.AnyAsync(cancellationToken) || await context.Projects.AnyAsync(cancellationToken))
        {
            if (await DevelopmentDataSeeder.HasCompleteDatasetAsync(context, cancellationToken))
                return false;

            throw new InvalidOperationException("Demo seed requires a fresh database without users or projects.");
        }

        await SeedAsync(context, cancellationToken, options);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public static void Seed(DbContext context, DevelopmentSeedOptions? development = null)
    {
        RoleSeeder.Seed(context);
        ProfessionalRoleSeeder.Seed(context);
        SkillCategorySeeder.Seed(context);
        SkillSeeder.Seed(context);
        SkillCategorySkillSeeder.Seed(context);
        if (development?.Enabled == true) DevelopmentDataSeeder.Seed(context, development);
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken = default, DevelopmentSeedOptions? development = null)
    {
        await RoleSeeder.SeedAsync(context, cancellationToken);
        await ProfessionalRoleSeeder.SeedAsync(context, cancellationToken);
        await SkillCategorySeeder.SeedAsync(context, cancellationToken);
        await SkillSeeder.SeedAsync(context, cancellationToken);
        await SkillCategorySkillSeeder.SeedAsync(context, cancellationToken);
        if (development?.Enabled == true) await DevelopmentDataSeeder.SeedAsync(context, development, cancellationToken);
    }
}
