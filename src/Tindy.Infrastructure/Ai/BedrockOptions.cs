namespace Tindy.Infrastructure.Ai;

public sealed class BedrockOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string ModelId { get; set; } = "google.gemma-4-31b";
    public int TimeoutSeconds { get; set; } = 90;
}
