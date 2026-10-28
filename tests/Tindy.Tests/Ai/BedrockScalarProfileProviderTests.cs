using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class BedrockScalarProfileProviderTests
{
    [Fact]
    public async Task SendsOnlyRoleReferencesAndScalarToolWithPdfFirst()
    {
        using var handler = new Stub(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = json.RootElement;
            var content = root.GetProperty("input")[1].GetProperty("content");
            Assert.Equal("input_file", content[0].GetProperty("type").GetString());
            using var source = JsonDocument.Parse(content[1].GetProperty("text").GetString()!);
            Assert.Equal("r1", source.RootElement.GetProperty("active_roles")[0].GetProperty("key").GetString());
            Assert.False(source.RootElement.TryGetProperty("active_skills", out _));
            Assert.Equal("extract_profile_scalars", root.GetProperty("tool_choice").GetProperty("name").GetString());
            var schema = root.GetProperty("tools")[0].GetProperty("parameters");
            var properties = schema.GetProperty("properties");
            Assert.Equal(2, properties.EnumerateObject().Count());
            var fields = properties.GetProperty("profile").GetProperty("properties");
            Assert.False(fields.TryGetProperty("skill_ids", out _));
            Assert.False(fields.TryGetProperty("bio", out _));
            Assert.False(fields.TryGetProperty("preferred_role_id", out _));
            var paths = properties.GetProperty("warnings").GetProperty("items").GetProperty("properties")
                .GetProperty("field").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains("profile.university", paths);
            Assert.Contains("profile.learning_goals", paths);
            Assert.Contains("profile.preferred_role_id", paths);
            Assert.DoesNotContain("university", paths);
            Assert.DoesNotContain("profile.preferred_role_key", paths);
            Assert.Contains("introduction", fields.GetProperty("learning_goals").GetProperty("description").GetString());
            Assert.Contains("contributions", fields.GetProperty("previous_projects_and_contribution").GetProperty("description").GetString());
            Assert.Equal(8192, root.GetProperty("max_output_tokens").GetInt32());
            return Success();
        });
        using var provider = Create(handler);
        var result = await provider.ExtractProfileForTestAsync(new("My name is Lan", "%PDF-test"u8.ToArray()), [new("r1", "AI Engineer")]);
        Assert.Contains("Lan", result.RawJson);
        Assert.Equal("tool_use", result.Diagnostics.StopReason);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("end_turn", "extract_profile_scalars")]
    [InlineData("max_tokens", "extract_profile_scalars")]
    [InlineData("tool_use", "extract_lists")]
    public async Task RejectsWrongOrIncompleteToolResults(string reason, string name)
    {
        using var handler = new Stub(_ => Task.FromResult(Success(reason, name)));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractProfileForTestAsync(new("source", null), []));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.Equal(reason, error.Diagnostics!.StopReason);
        Assert.NotNull(error.RawOutput);
    }

    private static HttpResponseMessage Success(string reason = "tool_use", string name = "extract_profile_scalars") =>
        BedrockTestSupport.ToolResponse(name, "{\"profile\":{\"display_name\":\"Lan\"},\"warnings\":[]}", reason);
    private static BedrockProfileExtractionProvider Create(HttpMessageHandler handler) => new(new HttpClient(handler),
        Options.Create(new BedrockOptions { Enabled = true, ApiKey = "test-key" }), NullLogger<BedrockProfileExtractionProvider>.Instance);
    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return callback(request); }
    }
}
