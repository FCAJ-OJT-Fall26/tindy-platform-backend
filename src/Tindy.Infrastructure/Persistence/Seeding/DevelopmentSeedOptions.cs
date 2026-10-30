using System.Text;
using Microsoft.Extensions.Configuration;

namespace Tindy.Infrastructure.Persistence.Seeding;

public sealed class DevelopmentSeedOptions
{
    public bool Enabled { get; }
    internal string Password { get; }

    private DevelopmentSeedOptions(bool enabled, string password) => (Enabled, Password) = (enabled, password);

    public static DevelopmentSeedOptions FromConfiguration(IConfiguration configuration, string environmentName)
    {
        if (!configuration.GetValue<bool>("DevelopmentSeed:Enabled")) return new(false, "");
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DevelopmentSeed can only be enabled in the Development environment.");

        var password = configuration["DevelopmentSeed:Password"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || Encoding.UTF8.GetByteCount(password) > 72 ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) ||
            !password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)))
            throw new InvalidOperationException("Set DevelopmentSeed:Password to a development-only password of at least 12 characters, at most 72 UTF-8 bytes, with uppercase, lowercase, digit and symbol. Use an environment variable or User Secrets.");

        return new(true, password);
    }
}
