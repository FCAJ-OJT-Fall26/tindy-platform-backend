
using Microsoft.EntityFrameworkCore;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class TindySeedRunner
{
    // ===================================
    // 1. SYNCHRONOUS SEEDING
    // ===================================
    public static void Seed(DbContext context)
    {
        RoleSeeder.Seed(context);
    }

    // ===================================
    // 2. ASYNCHRONOUS SEEDING
    // ===================================
    public static async Task SeedAsync(
        DbContext context,
        CancellationToken cancellationToken = default)
    {
        await RoleSeeder.SeedAsync(
            context,
            cancellationToken);
    }
}
