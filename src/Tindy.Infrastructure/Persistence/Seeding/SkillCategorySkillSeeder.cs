using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class SkillCategorySkillSeeder
{
    public static void Seed(DbContext context)
    {
        var categoryIds = context.Set<SkillCategory>().AsNoTracking()
            .ToDictionary(x => x.NameNormalized, x => x.CategoryId);
        var skillIds = context.Set<Skill>().AsNoTracking()
            .ToDictionary(x => x.NameNormalized, x => x.SkillId);
        var existing = context.Set<SkillCategorySkill>().AsNoTracking()
            .Select(x => new { x.CategoryId, x.SkillId }).ToList()
            .Select(x => (x.CategoryId, x.SkillId)).ToHashSet();

        var missing = BuildMissing(categoryIds, skillIds, existing);
        if (missing.Count == 0) return;

        context.Set<SkillCategorySkill>().AddRange(missing);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var categories = await context.Set<SkillCategory>().AsNoTracking()
            .Select(x => new { x.NameNormalized, x.CategoryId })
            .ToListAsync(cancellationToken);
        var skills = await context.Set<Skill>().AsNoTracking()
            .Select(x => new { x.NameNormalized, x.SkillId })
            .ToListAsync(cancellationToken);
        var links = await context.Set<SkillCategorySkill>().AsNoTracking()
            .Select(x => new { x.CategoryId, x.SkillId })
            .ToListAsync(cancellationToken);

        var categoryIds = categories.ToDictionary(x => x.NameNormalized, x => x.CategoryId);
        var skillIds = skills.ToDictionary(x => x.NameNormalized, x => x.SkillId);
        var existing = links.Select(x => (x.CategoryId, x.SkillId)).ToHashSet();

        var missing = BuildMissing(categoryIds, skillIds, existing);
        if (missing.Count == 0) return;

        context.Set<SkillCategorySkill>().AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<SkillCategorySkill> BuildMissing(
        IReadOnlyDictionary<string, Guid> categoryIds,
        IReadOnlyDictionary<string, Guid> skillIds,
        HashSet<(Guid CategoryId, Guid SkillId)> existing)
    {
        var missing = new List<SkillCategorySkill>();
        foreach (var (categoryName, skillNames) in CatalogSeedData.CategorySkills)
        {
            var categoryNameNormalized = CatalogSeedData.Normalize(categoryName);
            if (!categoryIds.TryGetValue(categoryNameNormalized, out var categoryId))
                throw new InvalidOperationException($"Missing seeded category: {categoryName}");

            foreach (var skillName in skillNames)
            {
                var skillNameNormalized = CatalogSeedData.Normalize(skillName);
                if (!skillIds.TryGetValue(skillNameNormalized, out var skillId))
                    throw new InvalidOperationException($"Missing seeded skill: {skillName}");

                if (!existing.Add((categoryId, skillId))) continue;
                missing.Add(new SkillCategorySkill { CategoryId = categoryId, SkillId = skillId });
            }
        }
        return missing;
    }
}
