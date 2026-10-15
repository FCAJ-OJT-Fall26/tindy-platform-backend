using Microsoft.EntityFrameworkCore;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class TindySeedRunner
{
    public static void Seed(DbContext context)
    {
        RoleSeeder.Seed(context);
        ProfessionalRoleSeeder.Seed(context);
        SkillCategorySeeder.Seed(context);
        SkillSeeder.Seed(context);
        SkillCategorySkillSeeder.Seed(context);
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        await RoleSeeder.SeedAsync(context, cancellationToken);
        await ProfessionalRoleSeeder.SeedAsync(context, cancellationToken);
        await SkillCategorySeeder.SeedAsync(context, cancellationToken);
        await SkillSeeder.SeedAsync(context, cancellationToken);
        await SkillCategorySkillSeeder.SeedAsync(context, cancellationToken);
    }
}
