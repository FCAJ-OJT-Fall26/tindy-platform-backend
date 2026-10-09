using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Extensions;

public static class ServicesCollectionExtensions
{
    public static IServiceCollection AddInfrastructure (this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string ' 'DefaultConnection' was not found'.");

        services.AddDbContext<TindyDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        return services;
    }
}