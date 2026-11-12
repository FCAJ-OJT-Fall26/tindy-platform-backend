using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Tindy.Infrastructure.Extensions;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;

namespace Tindy.Api.Commands;

internal static class DemoSeedCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["seed-demo", "--fresh"])
        {
            Console.WriteLine("Usage: dotnet Tindy.Api.dll seed-demo --fresh");
            Console.WriteLine("Run after migrations on a fresh database. Configure DefaultConnection and DemoSeed:Password.");
            return args is ["seed-demo", "--help"] ? 0 : 2;
        }

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddTindyDatabase(builder.Configuration);
            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TindyDbContext>();

            var seeded = await TindySeedRunner.SeedFreshDemoAsync(context, builder.Configuration);
            Console.WriteLine(seeded
                ? "Demo seed completed."
                : "Demo dataset already exists. No changes were made.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"Demo seed stopped: {exception.Message}");
            return 1;
        }
        catch (DbException exception)
        {
            Console.Error.WriteLine($"Demo seed stopped on a PostgreSQL error ({exception.SqlState ?? exception.GetType().Name}).");
            return 1;
        }
        catch (DbUpdateException)
        {
            Console.Error.WriteLine("Demo seed could not save the dataset. The seed transaction was rolled back.");
            return 1;
        }
    }
}
