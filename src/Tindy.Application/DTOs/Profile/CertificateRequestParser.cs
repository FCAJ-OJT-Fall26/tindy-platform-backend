using System.Globalization;
using System.Text.Json;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Profile;

// Strict JSON boundary: unknown, duplicate and account/security fields are rejected.
public static class CertificateRequestParser
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "title", "issuer", "credential_id", "credential_url", "issued_at", "expires_at"
    };

    public static CertificateUpsertRequest Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new ProfileValidationException(new Dictionary<string, string[]> { ["body"] = ["The request body must be a JSON object."] });

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
        {
            if (!Allowed.Contains(property.Name))
                errors[property.Name] = ["This field is not supported."];
            else if (!fields.TryAdd(property.Name, property.Value))
                errors[property.Name] = ["Duplicate JSON properties are not allowed."];
        }

        string? Text(string key, bool required, int maxLength)
        {
            if (!fields.TryGetValue(key, out var element))
            {
                if (required) errors[key] = ["This field is required."];
                return null;
            }
            if (element.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                errors[key] = ["The field must be a string or null."];
                return null;
            }
            var value = element.ValueKind == JsonValueKind.Null ? null : element.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value)) value = null;
            if (required && value is null)
                errors[key] = ["This field cannot be empty or null."];
            else if (value?.Length > maxLength)
                errors[key] = [$"The maximum length is {maxLength} characters."];
            return value;
        }

        DateOnly? Date(string key)
        {
            if (!fields.TryGetValue(key, out var element) || element.ValueKind == JsonValueKind.Null) return null;
            var raw = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            if (raw is null || raw.Length != 10 || raw[4] != '-' || raw[7] != '-' ||
                !raw.Where((ch, index) => index != 4 && index != 7).All(ch => ch is >= '0' and <= '9') ||
                !DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                errors[key] = ["The date must be a valid YYYY-MM-DD calendar date."];
                return null;
            }
            return date;
        }

        var title = Text("title", true, 255);
        var issuer = Text("issuer", true, 255);
        var credentialId = Text("credential_id", false, 255);
        var credentialUrl = Text("credential_url", false, 1000);
        var issued = Date("issued_at");
        var expires = Date("expires_at");

        if (credentialUrl is not null && !IsWebUrl(credentialUrl))
            errors["credential_url"] = ["A valid absolute http:// or https:// URL is required."];
        if (issued is not null && expires is not null && expires < issued)
            errors["expires_at"] = ["Expiry must not precede issuance."];
        if (errors.Count > 0) throw new ProfileValidationException(errors);
        return new CertificateUpsertRequest(title!, issuer!, credentialId, credentialUrl, issued, expires);
    }

    internal static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrWhiteSpace(uri.Host);
}

public static class CertificateInputValidator
{
    // Revalidate at Application boundary; Services can be called outside HTTP.
    public static CertificateUpsertRequest Normalize(CertificateUpsertRequest? request)
    {
        if (request is null)
            throw new ProfileValidationException(new Dictionary<string, string[]> { ["body"] = ["A certificate is required."] });

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = Clean(request.Title);
        var issuer = Clean(request.Issuer);
        var credentialId = Clean(request.CredentialId);
        var credentialUrl = Clean(request.CredentialUrl);
        if (title is null || title.Length > 255)
            errors["title"] = ["Title must contain 1 to 255 characters."];
        if (issuer is null || issuer.Length > 255)
            errors["issuer"] = ["Issuer must contain 1 to 255 characters."];
        if (credentialId?.Length > 255)
            errors["credential_id"] = ["Credential ID must not exceed 255 characters."];
        if (credentialUrl is not null && (credentialUrl.Length > 1000 || !CertificateRequestParser.IsWebUrl(credentialUrl)))
            errors["credential_url"] = ["A valid absolute http:// or https:// URL up to 1000 characters is required."];
        if (request.IssuedAt is not null && request.ExpiresAt is not null && request.ExpiresAt < request.IssuedAt)
            errors["expires_at"] = ["Expiry must not precede issuance."];
        if (errors.Count > 0) throw new ProfileValidationException(errors);
        return request with { Title = title!, Issuer = issuer!, CredentialId = credentialId, CredentialUrl = credentialUrl };
    }
}
