namespace Tindy.Infrastructure.Ai;

public sealed class BedrockOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Region { get; set; } = "ap-southeast-1";
    public string ModelId { get; set; } = "global.amazon.nova-2-lite-v1:0";
    public int TimeoutSeconds { get; set; } = 60;
}
