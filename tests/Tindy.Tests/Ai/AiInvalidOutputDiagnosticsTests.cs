using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Tindy.Api.ExceptionHandlers;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiInvalidOutputDiagnosticsTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public async Task OnlyDevelopmentExposesRawOutput(string environment, bool expected)
    {
        const string raw = "```json\n{invalid: '<script>debug</script>'}\n```";
        var exception = new AiException(AiFailure.InvalidOutput, raw)
        { Diagnostics = new("max_tokens", 120, 4096, 4096, 1000, "output_token_limit") };
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/ai/extract-profile";
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance,
            new TestEnvironment { EnvironmentName = environment });

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(502, context.Response.StatusCode);
        Assert.Equal("AI_INVALID_OUTPUT", json.RootElement.GetProperty("error_code").GetString());
        Assert.Equal(expected, json.RootElement.TryGetProperty("raw_model_output", out var output));
        if (expected)
        {
            Assert.Equal(raw, output.GetString());
            Assert.Equal("model_text", json.RootElement.GetProperty("raw_output_kind").GetString());
            Assert.False(json.RootElement.GetProperty("raw_output_truncated").GetBoolean());
            Assert.Equal("no-store", context.Response.Headers.CacheControl);
            Assert.Equal("max_tokens", json.RootElement.GetProperty("ai_diagnostics").GetProperty("stop_reason").GetString());
        }
        else
        {
            Assert.DoesNotContain("raw_output", json.RootElement.GetRawText());
            Assert.False(json.RootElement.TryGetProperty("ai_diagnostics", out _));
        }
        Assert.DoesNotContain(raw, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void DiagnosticsAreBoundedByUtf8BytesWithoutSplittingCharacters()
    {
        var raw = string.Concat(Enumerable.Repeat("🧪", AiProfileLimits.MaxOutputBytes));
        var exception = new AiException(AiFailure.InvalidOutput, raw);
        Assert.NotNull(exception.RawOutput);
        Assert.True(Encoding.UTF8.GetByteCount(exception.RawOutput) <= AiProfileLimits.MaxOutputBytes);
        Assert.StartsWith(exception.RawOutput, raw);
        Assert.DoesNotContain('\ufffd', exception.RawOutput);
        Assert.True(exception.RawOutputTruncated);
    }

    [Fact]
    public void OtherFailuresDoNotCarryProviderBodies()
    {
        var exception = new AiException(AiFailure.Provider, "private-provider-response");
        Assert.Null(exception.RawOutput);
        Assert.Null(exception.RawOutputKind);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tindy.Tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
