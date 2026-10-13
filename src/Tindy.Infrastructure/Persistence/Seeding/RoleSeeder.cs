

using Microsoft.EntityFrameworkCore;
using Tindy.Application.Constants;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Seeding;

public static class RoleSeeder
{
    // ===================================
    // 1. SYNCHRONOUS SEED
    // ===================================
    public static void Seed(DbContext context)
    {
        var roleExists = context.Set<Role>()
            .Any(x => x.RoleName == RoleNames.Member);

        if (roleExists)
        {
            return;
        }

        var memberRole = new Role
        {
            RoleId = Guid.NewGuid(),
            RoleName = RoleNames.Member
        };

        context.Set<Role>().Add(memberRole);

        context.SaveChanges();
    }

    // ===================================
    // 2. ASYNCHRONOUS SEED
    // ===================================
    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var roleExists = await context.Set<Role>()
            .AnyAsync(x => x.RoleName == RoleNames.Member, cancellationToken);

        if (roleExists)
        {
            return;
        }

        var memberRole = new Role
        {
            RoleId = Guid.NewGuid(),
            RoleName = RoleNames.Member
        };

        context.Set<Role>().Add(memberRole);

        await context.SaveChangesAsync(cancellationToken);
    }
}
