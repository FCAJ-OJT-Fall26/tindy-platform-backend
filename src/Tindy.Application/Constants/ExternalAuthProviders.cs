namespace Tindy.Application.Constants;

public static class ExternalAuthProviders
{
    public const string Google = "Google";
    public const string GitHub = "GitHub";

    public static string Normalize(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return provider.Trim() switch
        {
            var name when name.Equals(Google, StringComparison.OrdinalIgnoreCase) => Google,
            var name when name.Equals(GitHub, StringComparison.OrdinalIgnoreCase) => GitHub,
            _ => throw new ArgumentException("Unsupported external authentication provider.", nameof(provider))
        };
    }
}
