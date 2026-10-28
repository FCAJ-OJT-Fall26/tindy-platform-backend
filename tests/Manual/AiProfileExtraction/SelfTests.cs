using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Tindy.Manual.AiProfileExtraction;

internal static class SelfTests
{
    private const string SkillId = "11111111-1111-4111-8111-111111111111";
    private const string RoleId = "22222222-2222-4222-8222-222222222222";
    private const string UnknownId = "33333333-3333-4333-8333-333333333333";
    private const string Token = "offline.jwt.fixture";
    private const string SourceText = "Tôi đã xây dựng API bằng C# trong dự án nhóm.";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nOffline fixture, not a real CV.\n%%EOF");

    public static async Task<int> RunAsync(TextWriter output)
    {
        var cases = new List<(string Name, Func<Task> Run)>();
        foreach (var (text, pdf, name) in new[]
        {
            (true, false, "text-only multipart"),
            (false, true, "PDF-only multipart"),
            (true, true, "combined multipart")
        })
        {
            cases.Add((name, async () =>
            {
                using var fixture = new Fixture(text, pdf);
                await fixture.ExpectAsync(0);
                Check(fixture.ExtractionCalls == 1 && fixture.ApiCalls == 3, "Expected two catalogs and exactly one extraction.");
                Check(fixture.PdfCalls == (pdf ? 1 : 0), "Unexpected PDF download count.");
                Check(fixture.Output.ToString().Contains("C#", StringComparison.Ordinal), "Missing catalog skill name in output.");
                Check(fixture.Output.ToString().Contains("Backend developer", StringComparison.Ordinal), "Missing catalog role name in output.");
            }
            ));
        }

        AddDraftCase("unknown profile skill", draft => draft["profile"]!["skill_ids"] = new JsonArray(UnknownId), 1);
        AddDraftCase("malformed skill UUID", draft => draft["profile"]!["skill_ids"] = new JsonArray("not-a-guid"), 1);
        AddDraftCase("non-string skill UUID", draft => draft["profile"]!["skill_ids"] = new JsonArray(12), 1);
        AddDraftCase("duplicate profile skill", draft => draft["profile"]!["skill_ids"] = new JsonArray(SkillId, SkillId), 1);
        AddDraftCase("unknown experience skill", draft => draft["experiences"]![0]!["skill_ids"] = new JsonArray(UnknownId), 1);
        AddDraftCase("duplicate experience skill", draft => draft["experiences"]![0]!["skill_ids"] = new JsonArray(SkillId, SkillId), 1);
        AddDraftCase("unknown preferred role", draft => draft["profile"]!["preferred_role_id"] = UnknownId, 1);
        AddDraftCase("malformed preferred role", draft => draft["profile"]!["preferred_role_id"] = "not-a-guid", 1);
        AddDraftCase("null preferred role", draft => draft["profile"]!["preferred_role_id"] = null, 0);
        AddDraftCase("empty valid draft", draft =>
        {
            draft["profile"]!["skill_ids"] = new JsonArray();
            draft["profile"]!["preferred_role_id"] = null;
            draft["experiences"] = new JsonArray();
        }, 0);
        AddDraftCase("warnings do not fail extraction", draft => draft["warnings"] = new JsonArray(new JsonObject
        {
            ["field"] = "profile.university",
            ["code"] = "MISSING_INFORMATION",
            ["message"] = "Not supplied."
        }), 0);
        AddDraftCase("token echoed in draft is redacted", draft => draft["profile"]!["headline"] = Token, 0);
        foreach (var group in new[] { "profile", "experiences", "certificates", "warnings" })
        {
            AddDraftCase($"missing {group}", draft => draft.Remove(group), 1);
            AddDraftCase($"null {group}", draft => draft[group] = null, 1);
        }
        AddDraftCase("missing profile skill list", draft => draft["profile"]!.AsObject().Remove("skill_ids"), 1);
        AddDraftCase("missing experience skill list", draft => draft["experiences"]![0]!.AsObject().Remove("skill_ids"), 1);
        AddDraftCase("scalar experience entry", draft => draft["experiences"] = new JsonArray(7), 1);

        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable })
        {
            cases.Add(($"HTTP {(int)status} does not retry", async () =>
            {
                using var fixture = new Fixture();
                fixture.ExtractionResponse = () => JsonResponse("{\"error_code\":\"AI_TEST_FAILURE\"}", status);
                await fixture.ExpectAsync(1);
                Check(fixture.ExtractionCalls == 1, "Failed extraction was retried.");
                Check(fixture.Output.ToString().Contains(((int)status).ToString(), StringComparison.Ordinal), "HTTP status missing from output.");
            }
            ));
        }
        foreach (var body in new[] { "not JSON", "[]", "{\"success\":false,\"message\":\"failed\",\"data\":{}}", "{\"success\":true}" })
        {
            cases.Add(("invalid response envelope: " + body, async () =>
            {
                using var fixture = new Fixture();
                fixture.ExtractionResponse = () => JsonResponse(body);
                await fixture.ExpectAsync(1);
                Check(fixture.ExtractionCalls == 1, "Malformed response was retried.");
            }
            ));
        }
        cases.Add(("catalog HTTP failure prevents extraction", async () =>
        {
            using var fixture = new Fixture();
            fixture.SkillResponse = () => JsonResponse("{}", HttpStatusCode.Unauthorized);
            await fixture.ExpectAsync(1);
            Check(fixture.ExtractionCalls == 0 && fixture.ApiCalls == 1, "Catalog failure should stop before extraction.");
        }
        ));
        cases.Add(("malformed catalog prevents extraction", async () =>
        {
            using var fixture = new Fixture();
            fixture.SkillResponse = () => Envelope(new JsonArray(new JsonObject { ["skill_id"] = "invalid", ["name"] = "C#" }));
            await fixture.ExpectAsync(1);
            Check(fixture.ExtractionCalls == 0, "Malformed catalog must not reach extraction.");
        }
        ));
        cases.Add(("API redirect is not followed", async () =>
        {
            using var fixture = new Fixture();
            fixture.ExtractionResponse = () => Redirect("https://other.example.test/collect");
            await fixture.ExpectAsync(1);
            Check(fixture.ApiCalls == 3 && fixture.ExtractionCalls == 1, "API redirect caused an extra request.");
        }
        ));
        cases.Add(("API timeout is controlled without retry", async () =>
        {
            using var fixture = new Fixture();
            fixture.ExtractionResponse = () => throw new TaskCanceledException("Simulated API timeout " + Token);
            await fixture.ExpectAsync(1);
            Check(fixture.ApiCalls == 3 && fixture.ExtractionCalls == 1, "API timeout was retried.");
        }
        ));
        cases.Add(("HTML PDF URL rejected", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ => Task.FromResult(JsonResponse("<html>Login required</html>"));
            await fixture.ExpectAsync(1);
            Check(fixture.ExtractionCalls == 0, "HTML was sent as PDF.");
        }
        ));
        cases.Add(("PDF size header rejected before reading", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new RejectReadStream())
                };
                response.Content.Headers.ContentLength = 4 * 1024 * 1024 + 1;
                return Task.FromResult(response);
            };
            await fixture.ExpectAsync(1);
            Check(fixture.ExtractionCalls == 0, "Oversized PDF was sent to extraction.");
        }
        ));
        cases.Add(("PDF streaming size limit without Content-Length", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ =>
            {
                var bytes = new byte[4 * 1024 * 1024 + 1];
                Pdf.CopyTo(bytes, 0);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new NonSeekableStream(bytes))
                });
            };
            await fixture.ExpectAsync(1);
            Check(fixture.ExtractionCalls == 0, "Chunked oversized PDF was sent to extraction.");
        }
        ));
        cases.Add(("exact 4 MiB PDF accepted", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.ExpectedPdf = new byte[4 * 1024 * 1024];
            Pdf.CopyTo(fixture.ExpectedPdf, 0);
            fixture.PdfResponse = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableStream(fixture.ExpectedPdf))
            });
            await fixture.ExpectAsync(0);
            Check(fixture.PdfCalls == 1 && fixture.ExtractionCalls == 1, "Exact size limit was rejected or retried.");
        }
        ));
        cases.Add(("three HTTPS redirects allowed without JWT", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = call => Task.FromResult(call <= 3 ? Redirect($"https://files{call}.example.test/cv.pdf") : PdfResponse());
            await fixture.ExpectAsync(0);
            Check(fixture.PdfCalls == 4, "Expected three redirects followed by one download.");
        }
        ));
        cases.Add(("fourth redirect rejected", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ => Task.FromResult(Redirect("https://files.example.test/loop.pdf"));
            await fixture.ExpectAsync(1);
            Check(fixture.PdfCalls == 4 && fixture.ExtractionCalls == 0, "Redirect limit was not enforced.");
        }
        ));
        cases.Add(("HTTPS to HTTP redirect rejected", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ => Task.FromResult(Redirect("http://files.example.test/cv.pdf"));
            await fixture.ExpectAsync(1);
            Check(fixture.PdfCalls == 1 && fixture.ExtractionCalls == 0, "Insecure redirect followed.");
        }
        ));
        cases.Add(("relative HTTPS redirect allowed", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = call => Task.FromResult(call == 1 ? Redirect("/download.pdf") : PdfResponse());
            await fixture.ExpectAsync(0);
            Check(fixture.PdfCalls == 2, "Relative redirect was not followed.");
        }
        ));
        cases.Add(("download failure prevents extraction", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            await fixture.ExpectAsync(1);
            Check(fixture.PdfCalls == 1 && fixture.ExtractionCalls == 0, "Download failure retried or extracted.");
        }
        ));
        cases.Add(("download network failure is controlled", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Network failure " + Token));
            await fixture.ExpectAsync(1);
            Check(fixture.ExtractionCalls == 0, "Failed download reached extraction.");
        }
        ));
        cases.Add(("download timeout is controlled without retry", async () =>
        {
            using var fixture = new Fixture(false, true);
            fixture.PdfResponse = _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated download timeout " + Token));
            await fixture.ExpectAsync(1);
            Check(fixture.PdfCalls == 1 && fixture.ExtractionCalls == 0, "Timed out download retried or reached extraction.");
        }
        ));
        cases.Add(("caller cancellation is controlled", async () =>
        {
            using var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await fixture.ExpectAsync(1, cancellationToken: cancellation.Token);
            Check(fixture.ApiCalls == 0 && fixture.PdfCalls == 0, "Cancelled run should stop before HTTP.");
        }
        ));
        foreach (var token in new string?[] { null, "", "bad token", "bad\r\ntoken" })
        {
            cases.Add(("missing or invalid token", async () =>
            {
                using var fixture = new Fixture();
                await fixture.ExpectAsync(1, token);
                Check(fixture.ApiCalls == 0 && fixture.PdfCalls == 0, "Invalid token should fail before HTTP.");
            }
            ));
        }
        foreach (var text in new[] { "", " \n\t", new string('a', 20_001) })
        {
            cases.Add(("empty or oversized text", async () =>
            {
                using var fixture = new Fixture();
                await File.WriteAllTextAsync(fixture.TextPath, text);
                await fixture.ExpectAsync(1);
                Check(fixture.ApiCalls == 0, "Invalid text should fail before HTTP.");
            }
            ));
        }
        cases.Add(("missing text file", async () =>
        {
            using var fixture = new Fixture();
            File.Delete(fixture.TextPath);
            await fixture.ExpectAsync(1);
            Check(fixture.ApiCalls == 0, "Missing file should fail before HTTP.");
        }
        ));
        cases.Add(("exact 20000 text characters accepted", async () =>
        {
            using var fixture = new Fixture();
            fixture.ExpectedText = new string('a', 20_000);
            await File.WriteAllTextAsync(fixture.TextPath, fixture.ExpectedText);
            await fixture.ExpectAsync(0);
            Check(fixture.ExtractionCalls == 1, "Exact text size limit was rejected.");
        }
        ));
        cases.Add(("no source fails before HTTP", async () =>
        {
            using var fixture = new Fixture(false, false);
            await fixture.ExpectAsync(1);
            Check(fixture.ApiCalls == 0 && fixture.PdfCalls == 0, "Empty input should fail before HTTP.");
        }
        ));
        foreach (var args in new[]
        {
            Array.Empty<string>(), new[] { "--unknown", "x" }, new[] { "--text-file" },
            new[] { "--text-file", "bio.txt", "--text-file", "other.txt" },
            new[] { "--pdf-url", "http://example.test/cv.pdf" },
            new[] { "--pdf-url", "https://user:password@example.test/cv.pdf" },
            new[] { "--pdf-url", "not-a-url" },
            new[] { "--text-file", "bio.txt", "--base-url", "not-a-url" },
            new[] { "--text-file", "bio.txt", "--base-url", "http://api.example.test" },
            new[] { "--text-file", "bio.txt", "--base-url", "https://user:password@api.example.test" }
        })
        {
            cases.Add(("invalid CLI arguments: " + string.Join(' ', args), () =>
            {
                var rejected = false;
                try { RunOptions.Parse(args); }
                catch (Exception) { rejected = true; }
                Check(rejected, "Invalid arguments were accepted.");
                return Task.CompletedTask;
            }
            ));
        }
        cases.Add(("CLI defaults and combined sources", () =>
        {
            var parsed = RunOptions.Parse(["--text-file", "bio.txt", "--pdf-url", "https://files.example.test/cv.pdf"]);
            Check(parsed.BaseUrl == new Uri("http://localhost:5217"), "Unexpected default API URL.");
            Check(parsed.TextFile == "bio.txt" && parsed.PdfUrl?.Scheme == "https", "Source arguments lost.");
            return Task.CompletedTask;
        }
        ));

        var failures = 0;
        foreach (var (name, run) in cases)
        {
            try
            {
                await run();
                await output.WriteLineAsync($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                await output.WriteLineAsync($"FAIL {name}: {exception.Message.Replace(Token, "[redacted]", StringComparison.Ordinal)}");
            }
        }
        await output.WriteLineAsync($"Offline self-tests: {cases.Count - failures}/{cases.Count} passed; no network or Bedrock calls.");
        return failures == 0 ? 0 : 1;

        void AddDraftCase(string name, Action<JsonObject> change, int exitCode) => cases.Add((name, async () =>
        {
            using var fixture = new Fixture();
            change(fixture.Draft);
            await fixture.ExpectAsync(exitCode);
        }
        ));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Envelope(JsonNode data) => JsonResponse(new JsonObject
    {
        ["success"] = true,
        ["message"] = "Offline fixture",
        ["data"] = data.DeepClone()
    }.ToJsonString());

    private static HttpResponseMessage PdfResponse() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf) };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _withText;
        private readonly bool _withPdf;
        private readonly HttpClient _api;
        private readonly HttpClient _pdf;
        public readonly string TextPath = Path.Combine(Path.GetTempPath(), $"tindy-extraction-selftest-{Guid.NewGuid():N}.txt");
        public readonly StringWriter Output = new();
        public readonly JsonObject Draft = new()
        {
            ["profile"] = new JsonObject { ["skill_ids"] = new JsonArray(SkillId), ["preferred_role_id"] = RoleId },
            ["experiences"] = new JsonArray(new JsonObject { ["project_name"] = "Offline project", ["skill_ids"] = new JsonArray(SkillId) }),
            ["certificates"] = new JsonArray(),
            ["warnings"] = new JsonArray()
        };
        public Func<HttpResponseMessage>? ExtractionResponse { get; set; }
        public Func<HttpResponseMessage>? SkillResponse { get; set; }
        public Func<int, Task<HttpResponseMessage>> PdfResponse { get; set; } = _ => Task.FromResult(SelfTests.PdfResponse());
        public string ExpectedText { get; set; } = SourceText;
        public byte[] ExpectedPdf { get; set; } = Pdf;
        public int ApiCalls { get; private set; }
        public int PdfCalls { get; private set; }
        public int ExtractionCalls { get; private set; }

        public Fixture(bool withText = true, bool withPdf = false)
        {
            _withText = withText;
            _withPdf = withPdf;
            File.WriteAllText(TextPath, SourceText);
            _api = new HttpClient(new FakeHandler(HandleApiAsync));
            _pdf = new HttpClient(new FakeHandler((request, _) =>
            {
                PdfCalls++;
                Check(request.Method == HttpMethod.Get, "PDF download must use GET.");
                Check(request.RequestUri?.Scheme == "https", "PDF request must stay HTTPS.");
                Check(request.Headers.Authorization is null, "JWT leaked to PDF host.");
                return PdfResponse(PdfCalls);
            }));
        }

        public async Task ExpectAsync(int expected, string? token = Token, CancellationToken cancellationToken = default)
        {
            var runner = new ExtractionRunner(_api, _pdf, Output);
            var options = new RunOptions(new Uri("http://localhost:5217"),
                _withPdf ? new Uri("https://files.example.test/cv.pdf") : null, _withText ? TextPath : null);
            var actual = await runner.RunAsync(options, token, cancellationToken);
            Check(!Output.ToString().Contains(Token, StringComparison.Ordinal), "Token leaked to console output.");
            Check(actual == expected, $"Expected exit {expected}, got {actual}. Output: {Output}");
        }

        private async Task<HttpResponseMessage> HandleApiAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ApiCalls++;
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == Token, "API JWT missing or incorrect.");
            Check(request.RequestUri?.Host == "localhost", "API request changed host.");
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/catalog/skills":
                    Check(request.Method == HttpMethod.Get, "Catalog must use GET.");
                    return SkillResponse?.Invoke() ?? Envelope(new JsonArray(new JsonObject { ["skill_id"] = SkillId, ["name"] = "C#" }));
                case "/api/catalog/professional-roles":
                    Check(request.Method == HttpMethod.Get, "Catalog must use GET.");
                    return Envelope(new JsonArray(new JsonObject { ["professional_role_id"] = RoleId, ["name"] = "Backend developer" }));
                case "/api/ai/extract-profile":
                    ExtractionCalls++;
                    Check(request.Method == HttpMethod.Post, "Extraction must use POST.");
                    Check(request.Content is MultipartFormDataContent, "Extraction must use multipart/form-data.");
                    var parts = ((MultipartFormDataContent)request.Content!).ToDictionary(
                        part => part.Headers.ContentDisposition!.Name!.Trim('"'));
                    Check(parts.Count == (_withText ? 1 : 0) + (_withPdf ? 1 : 0), "Unexpected multipart parts.");
                    if (_withText)
                    {
                        Check(parts.TryGetValue("text", out var part), "Text part missing.");
                        Check(await part!.ReadAsStringAsync(cancellationToken) == ExpectedText, "Text content changed.");
                        Check(part.Headers.ContentDisposition?.FileName is null, "Text incorrectly sent as file.");
                    }
                    if (_withPdf)
                    {
                        Check(parts.TryGetValue("file", out var part), "File part missing.");
                        Check(part!.Headers.ContentType?.MediaType == "application/pdf", "Wrong PDF MIME type.");
                        Check(!string.IsNullOrWhiteSpace(part.Headers.ContentDisposition?.FileName), "PDF filename missing.");
                        Check((await part.ReadAsByteArrayAsync(cancellationToken)).SequenceEqual(ExpectedPdf), "PDF content changed.");
                    }
                    return ExtractionResponse?.Invoke() ?? Envelope(Draft);
                default:
                    throw new InvalidOperationException("Unexpected route; tool must not write profiles or call other endpoints.");
            }
        }

        public void Dispose()
        {
            _api.Dispose();
            _pdf.Dispose();
            Output.Dispose();
            File.Delete(TextPath);
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class RejectReadStream : MemoryStream
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Oversized Content-Length should be rejected before reading the body.");
    }
}
