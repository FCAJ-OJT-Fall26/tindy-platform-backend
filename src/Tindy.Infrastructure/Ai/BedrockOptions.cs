namespace Tindy.Infrastructure.Ai;

public sealed class BedrockOptions
{
    public const string QwenModel = "qwen.qwen3-vl-235b-a22b";
    public const string NovaModel = "global.amazon.nova-2-lite-v1:0";
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string ModelId { get; set; } = QwenModel;
    public int TimeoutSeconds { get; set; } = 90;
}
