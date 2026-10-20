namespace Tindy.Application.Models.Authentication;

/// <summary>Only persist a conservative HTTPS avatar URL supplied by a trusted OAuth provider.</summary>
public static class OAuthAvatarUrl
{
    public static string? Normalize(string? value)
    {
        var url = value?.Trim();
        if (string.IsNullOrWhiteSpace(url) || url.Length > 1000 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        return url;
    }
}
