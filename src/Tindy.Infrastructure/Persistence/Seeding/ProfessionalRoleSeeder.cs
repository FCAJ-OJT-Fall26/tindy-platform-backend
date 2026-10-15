using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class ProfessionalRoleSeeder
{
    public static void Seed(DbContext context)
    {
        var existing = context.Set<ProfessionalRole>().AsNoTracking()
            .Select(x => x.NameNormalized).ToHashSet(StringComparer.Ordinal);

        var missing = BuildMissing(existing);
        if (missing.Count == 0) return;

        context.Set<ProfessionalRole>().AddRange(missing);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var names = await context.Set<ProfessionalRole>().AsNoTracking()
            .Select(x => x.NameNormalized).ToListAsync(cancellationToken);

        var missing = BuildMissing(names.ToHashSet(StringComparer.Ordinal));
        if (missing.Count == 0) return;

        context.Set<ProfessionalRole>().AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<ProfessionalRole> BuildMissing(HashSet<string> existing)
    {
        var missing = new List<ProfessionalRole>();
        foreach (var item in CatalogSeedData.ProfessionalRoles)
        {
            var normalized = CatalogSeedData.Normalize(item.Name);
            if (!existing.Add(normalized)) continue;

            missing.Add(new ProfessionalRole
            {
                ProfessionalRoleId = item.Id,
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
