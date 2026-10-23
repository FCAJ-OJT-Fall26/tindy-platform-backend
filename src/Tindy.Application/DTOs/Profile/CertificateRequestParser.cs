using System.Text.Json;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Profile;

// Strict JSON boundary: unknown, duplicate and account/security fields are rejected.
public static class CertificateRequestParser
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "title", "issuer", "description", "credential_url"
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

        var title = Text("title", true, 255);
        var issuer = Text("issuer", true, 255);
        var description = Text("description", false, 2000);
        var credentialUrl = Text("credential_url", false, 1000);

        if (credentialUrl is not null && !IsWebUrl(credentialUrl))
            errors["credential_url"] = ["A valid absolute http:// or https:// URL is required."];
        if (errors.Count > 0) throw new ProfileValidationException(errors);
        return new CertificateUpsertRequest(title!, issuer!, description, credentialUrl);
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
        var description = Clean(request.Description);
        var credentialUrl = Clean(request.CredentialUrl);
        if (title is null || title.Length > 255)
            errors["title"] = ["Title must contain 1 to 255 characters."];
        if (issuer is null || issuer.Length > 255)
            errors["issuer"] = ["Issuer must contain 1 to 255 characters."];
        if (description?.Length > 2000)
            errors["description"] = ["Description must not exceed 2000 characters."];
        if (credentialUrl is not null && (credentialUrl.Length > 1000 || !CertificateRequestParser.IsWebUrl(credentialUrl)))
            errors["credential_url"] = ["A valid absolute http:// or https:// URL up to 1000 characters is required."];
        if (errors.Count > 0) throw new ProfileValidationException(errors);
        return request with { Title = title!, Issuer = issuer!, Description = description, CredentialUrl = credentialUrl };
    }
}
