using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;

namespace Tindy.Infrastructure.Ai;

public sealed partial class BedrockProfileExtractionProvider
{
    private static readonly HashSet<string> QwenRegions = new(StringComparer.Ordinal)
    {
        "us-east-1", "us-east-2", "us-west-2", "eu-south-1", "eu-west-1", "eu-west-2",
        "ap-northeast-1", "ap-south-1", "ap-southeast-2", "sa-east-1"
    };

    private async Task<AiProfileInput> PrepareInputAsync(AiProfileInput input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsQwen) return input;
        if (input.Images is null && input.Pdf is not null)
            input = input with { Images = await _renderer.RenderAsync(input.Pdf, token) };
        if (input.Images is not null && (input.Images.Count is 0 or > PopplerPdfPageRenderer.MaxPages ||
            input.Images.Any(image => image.Format != "png" || image.Bytes.Length is 0 or > PopplerPdfPageRenderer.MaxImageBytes) ||
            input.Images.Sum(image => (long)image.Bytes.Length) > PopplerPdfPageRenderer.MaxTotalImageBytes))
            throw new AiException(AiFailure.DocumentRejected);
        return input;
    }

    private List<object> CreateDocumentContent(AiProfileInput input)
    {
        var content = new List<object>();
        if (IsQwen)
        {
            foreach (var page in input.Images ?? [])
                content.Add(new { image = new { format = page.Format, source = new { bytes = Convert.ToBase64String(page.Bytes) } } });
        }
        else if (input.Pdf is not null)
            content.Add(new { document = new { format = "pdf", name = "CV", source = new { bytes = Convert.ToBase64String(input.Pdf) } } });
        return content;
    }

    // There is exactly one declared tool. Qwen supports any, not Nova's named tool choice.
    private object ToolChoice(string name) => IsQwen
        ? new { any = new { } }
        : (object)new { tool = new { name } };

    private void AddModelFields(Dictionary<string, object> body)
    {
        if (!IsQwen) body["additionalModelRequestFields"] = new { reasoningConfig = new { type = "disabled" } };
    }
}
