using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Tindy.Infrastructure.Extensions;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Api.Commands;

internal static class MigrateDatabaseCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["migrate-db"])
        {
            Console.WriteLine("Usage: dotnet Tindy.Api.dll migrate-db");
            Console.WriteLine("Configure ConnectionStrings:DefaultConnection for the migration task.");
            return args is ["migrate-db", "--help"] ? 0 : 2;
        }

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddTindyDatabase(builder.Configuration);
            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TindyDbContext>().Database;

            Console.WriteLine("Applying PostgreSQL migrations.");
            await database.MigrateAsync();
            var appliedMigrations = (await database.GetAppliedMigrationsAsync()).ToArray();
            var latestMigration = appliedMigrations.LastOrDefault() ?? "none";
            Console.WriteLine($"Database migration complete. Applied history: {appliedMigrations.Length}. Latest migration: {latestMigration}.");
            return 0;
        }
        catch (DbException exception)
        {
            Console.Error.WriteLine($"Database migration failed with a PostgreSQL error ({exception.SqlState ?? exception.GetType().Name}).");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Database migration failed ({exception.GetType().Name}). Check database configuration and migration state.");
            return 1;
        }
    }
}
