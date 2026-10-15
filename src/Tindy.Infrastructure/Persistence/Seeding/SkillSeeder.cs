using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class SkillSeeder
{
    public static void Seed(DbContext context)
    {
        var existing = context.Set<Skill>().AsNoTracking()
            .Select(x => x.NameNormalized).ToHashSet(StringComparer.Ordinal);

        var missing = BuildMissing(existing);
        if (missing.Count == 0) return;

        context.Set<Skill>().AddRange(missing);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var names = await context.Set<Skill>().AsNoTracking()
            .Select(x => x.NameNormalized).ToListAsync(cancellationToken);

        var missing = BuildMissing(names.ToHashSet(StringComparer.Ordinal));
        if (missing.Count == 0) return;

        context.Set<Skill>().AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<Skill> BuildMissing(HashSet<string> existing)
    {
        var missing = new List<Skill>();
        foreach (var item in CatalogSeedData.Skills)
        {
            var normalized = CatalogSeedData.Normalize(item.Name);
            if (!existing.Add(normalized)) continue;

            missing.Add(new Skill
            {
                SkillId = item.Id,
                Name = item.Name,
                NameNormalized = normalized,
                IsActive = true
            });
        }
        return missing;
    }
}
