using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class SkillCategorySeeder
{
    public static void Seed(DbContext context)
    {
        var existing = context.Set<SkillCategory>().AsNoTracking()
            .Select(x => x.NameNormalized).ToHashSet(StringComparer.Ordinal);

        var missing = BuildMissing(existing);
        if (missing.Count == 0) return;

        context.Set<SkillCategory>().AddRange(missing);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var names = await context.Set<SkillCategory>().AsNoTracking()
            .Select(x => x.NameNormalized).ToListAsync(cancellationToken);

        var missing = BuildMissing(names.ToHashSet(StringComparer.Ordinal));
        if (missing.Count == 0) return;

        context.Set<SkillCategory>().AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<SkillCategory> BuildMissing(HashSet<string> existing)
    {
        var missing = new List<SkillCategory>();
        foreach (var item in CatalogSeedData.SkillCategories)
        {
            var normalized = CatalogSeedData.Normalize(item.Name);
            if (!existing.Add(normalized)) continue;

            missing.Add(new SkillCategory
            {
                CategoryId = item.Id,
                Name = item.Name,
                NameNormalized = normalized,
                Description = item.Description,
                DisplayOrder = item.DisplayOrder,
                IsActive = true
            });
        }
        return missing;
    }
}
