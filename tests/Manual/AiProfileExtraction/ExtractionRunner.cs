using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Tindy.Manual.AiProfileExtraction;

internal sealed class ExtractionRunner(HttpClient apiClient, HttpClient pdfClient, TextWriter output)
{
    private const int MaxPdfBytes = 4 * 1024 * 1024;
    private const int MaxJsonBytes = 4 * 1024 * 1024;
    private string? _token;

    public async Task<int> RunAsync(RunOptions options, string? token, CancellationToken cancellationToken = default)
    {
        _token = token;
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
                throw new CheckException("Set TINDY_ACCESS_TOKEN to the Tindy JWT only (without the Bearer prefix).");

            var text = await ReadTextAsync(options.TextFile, cancellationToken);
            if (text is null && options.PdfUrl is null)
                throw new CheckException("Provide a PDF URL or non-empty text file.");

            var skills = await ReadCatalogAsync(options.BaseUrl, "skills", "skill_id", cancellationToken);
            var roles = await ReadCatalogAsync(options.BaseUrl, "professional-roles", "professional_role_id", cancellationToken);
            Log($"Active catalog: {skills.Count} skills, {roles.Count} roles.");

            var pdf = options.PdfUrl is null ? null : await DownloadPdfAsync(options.PdfUrl, cancellationToken);
            using var form = new MultipartFormDataContent();
            if (text is not null)
                form.Add(new StringContent(text, Encoding.UTF8), "text");
            if (pdf is not null)
            {
                var file = new ByteArrayContent(pdf);
                file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                form.Add(file, "file", "source.pdf");
            }

            using var response = await SendApiAsync(options.BaseUrl, "/api/ai/extract-profile", form, cancellationToken);
            var draft = Data(response.RootElement);
            ValidateShape(draft);
            Log("Draft (including warnings; review against your original sources):");
            Log(JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true }));
            var valid = CheckIds(draft, skills, roles);
            Log(valid ? "PASS: response and catalog IDs are valid. Content accuracy needs manual review."
                : "FAIL: one or more catalog IDs are invalid. Catalog may have changed during this run.");
            return valid ? 0 : 1;
        }
        catch (CheckException exception) { Log($"FAIL: {exception.Message}"); }
        catch (JsonException) { Log("FAIL: API returned invalid JSON."); }
        catch (OperationCanceledException)
        {
            Log(cancellationToken.IsCancellationRequested ? "FAIL: cancelled." : "FAIL: request timed out; no retry performed.");
        }
        catch (HttpRequestException) { Log("FAIL: HTTP connection or download failed; no retry performed."); }
        catch (IOException) { Log("FAIL: could not read the text file or response stream."); }
        catch (UnauthorizedAccessException) { Log("FAIL: text file is not readable."); }
        catch (ArgumentException) { Log("FAIL: invalid file path, URI or request parameter."); }
        catch (UriFormatException) { Log("FAIL: PDF redirect contains an invalid URI."); }
        return 1;
    }

    private static async Task<string?> ReadTextAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is null)
            return null;
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var buffer = new char[20_001];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        if (count > 20_000)
            throw new CheckException("Text must not exceed 20,000 characters.");
        var text = new string(buffer, 0, count);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private async Task<Dictionary<Guid, string>> ReadCatalogAsync(Uri baseUrl, string route, string idField,
        CancellationToken cancellationToken)
    {
        using var response = await SendApiAsync(baseUrl, $"/api/catalog/{route}", null, cancellationToken);
        var data = Data(response.RootElement);
        RequireKind(data, JsonValueKind.Array, "Catalog data must be an array.");
        var catalog = new Dictionary<Guid, string>();
        foreach (var item in data.EnumerateArray())
        {
            var id = Required(item, idField, JsonValueKind.String);
            var name = Required(item, "name", JsonValueKind.String).GetString()!;
            if (!id.TryGetGuid(out var guid) || guid == Guid.Empty || string.IsNullOrWhiteSpace(name) || !catalog.TryAdd(guid, name))
                throw new CheckException("Catalog contains an invalid or duplicate ID/name.");
        }
        return catalog;
    }

    private async Task<JsonDocument> SendApiAsync(Uri baseUrl, string route, HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var request = new HttpRequestMessage(content is null ? HttpMethod.Get : HttpMethod.Post, new Uri(baseUrl, route));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = content;
        var timer = Stopwatch.StartNew();
        using var response = await apiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Log($"{request.Method} {route}: HTTP {(int)response.StatusCode}");
        var bytes = await ReadBoundedAsync(response.Content, MaxJsonBytes, timeout.Token);
        Log($"Elapsed: {timer.Elapsed.TotalSeconds:F2}s");
        if (!response.IsSuccessStatusCode)
        {
            // Do not echo arbitrary server bodies: proxies may include credentials or source URLs.
            var hint = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "JWT missing, invalid or expired; obtain a new access token.",
                HttpStatusCode.Forbidden => "Account is not allowed to access this endpoint.",
                HttpStatusCode.TooManyRequests => "Rate limit or provider capacity reached; retry manually later.",
                HttpStatusCode.ServiceUnavailable => "Check backend Bedrock configuration and availability.",
                _ => "Check backend diagnostics using the HTTP status."
            };
            throw new CheckException($"API request failed. {hint} No retry performed.");
        }
        return JsonDocument.Parse(bytes);
    }

    private async Task<byte[]> DownloadPdfAsync(Uri initialUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var url = initialUrl;
        for (var redirects = 0; ; redirects++)
        {
            if (!RunOptions.IsPdfUrl(url))
                throw new CheckException("PDF URL and redirects must use HTTPS without embedded credentials.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await pdfClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirects >= 3 || response.Headers.Location is null)
                    throw new CheckException("PDF download exceeded 3 redirects or has no redirect location.");
                url = new Uri(url, response.Headers.Location);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK)
                throw new CheckException($"PDF download returned HTTP {(int)response.StatusCode}; use a direct download URL.");
            var pdf = await ReadBoundedAsync(response.Content, MaxPdfBytes, timeout.Token);
            if (!pdf.AsSpan().StartsWith("%PDF-"u8))
                throw new CheckException("Download is not a PDF (%PDF- signature missing); use a direct file URL, not a preview page.");
            Log($"PDF downloaded: {pdf.Length} bytes.");
            return pdf;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit)
            throw new CheckException($"Response exceeds the {limit:N0}-byte limit.");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit + 1L - buffer.Length)), cancellationToken)) != 0)
        {
            if (buffer.Length + read > limit)
                throw new CheckException($"Response exceeds the {limit:N0}-byte limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static JsonElement Data(JsonElement root)
    {
        Required(root, "success", JsonValueKind.True);
        Required(root, "message", JsonValueKind.String);
        if (!root.TryGetProperty("data", out var data))
            throw new CheckException("Response is missing data.");
        return data;
    }

    private static void ValidateShape(JsonElement draft)
    {
        var profile = Required(draft, "profile", JsonValueKind.Object);
        Required(profile, "skill_ids", JsonValueKind.Array);
        if (!profile.TryGetProperty("preferred_role_id", out var role) || role.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw new CheckException("profile.preferred_role_id must be a UUID string or null.");
        foreach (var experience in Required(draft, "experiences", JsonValueKind.Array).EnumerateArray())
            Required(experience, "skill_ids", JsonValueKind.Array);
        foreach (var certificate in Required(draft, "certificates", JsonValueKind.Array).EnumerateArray())
            RequireKind(certificate, JsonValueKind.Object, "Certificate must be an object.");
        foreach (var warning in Required(draft, "warnings", JsonValueKind.Array).EnumerateArray())
        {
            Required(warning, "field", JsonValueKind.String);
            Required(warning, "code", JsonValueKind.String);
            Required(warning, "message", JsonValueKind.String);
        }
    }

    private bool CheckIds(JsonElement draft, Dictionary<Guid, string> skills, Dictionary<Guid, string> roles)
    {
        var profile = draft.GetProperty("profile");
        var valid = CheckSkillList(profile.GetProperty("skill_ids"), "profile.skill_ids", skills);
        var role = profile.GetProperty("preferred_role_id");
        if (role.ValueKind == JsonValueKind.Null)
            Log("profile.preferred_role_id: no ID to compare.");
        else
            valid &= CheckId(role, "profile.preferred_role_id", roles, new HashSet<Guid>());
        var index = 0;
        foreach (var experience in draft.GetProperty("experiences").EnumerateArray())
            valid &= CheckSkillList(experience.GetProperty("skill_ids"), $"experiences[{index++}].skill_ids", skills);
        return valid;
    }

    private bool CheckSkillList(JsonElement ids, string path, Dictionary<Guid, string> catalog)
    {
        if (ids.GetArrayLength() == 0)
            Log($"{path}: no ID to compare.");
        var seen = new HashSet<Guid>();
        var valid = true;
        var index = 0;
        foreach (var id in ids.EnumerateArray())
            valid &= CheckId(id, $"{path}[{index++}]", catalog, seen);
        return valid;
    }

    private bool CheckId(JsonElement value, string path, Dictionary<Guid, string> catalog, HashSet<Guid> seen)
    {
        if (value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out var id) || id == Guid.Empty)
        {
            Log($"FAIL {path}: invalid UUID {value.GetRawText()}");
            return false;
        }
        if (!seen.Add(id))
        {
            Log($"FAIL {path}: {id} (duplicate)");
            return false;
        }
        if (!catalog.TryGetValue(id, out var name))
        {
            Log($"FAIL {path}: {id} (absent from active catalog)");
            return false;
        }
        Log($"PASS {path}: {id} -> {JsonSerializer.Serialize(name)}");
        return true;
    }

    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw new CheckException($"Response field '{name}' is missing or has the wrong type.");
        return value;
    }

    private static void RequireKind(JsonElement value, JsonValueKind kind, string message)
    {
        if (value.ValueKind != kind)
            throw new CheckException(message);
    }

    private void Log(string message) => output.WriteLine(string.IsNullOrEmpty(_token) ? message : message.Replace(_token, "[REDACTED]", StringComparison.Ordinal));

    private sealed class CheckException(string message) : Exception(message);
}
