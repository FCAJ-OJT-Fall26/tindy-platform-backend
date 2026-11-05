namespace Tindy.Application.Entities;

public sealed class ChatRequest
{
    public Guid RequestId { get; set; }
    public Guid SenderId { get; set; }
    public Guid RecipientId { get; set; }
    public string PairKey { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? ApplicationId { get; set; }
    public string ProjectName { get; set; } = null!;
    public string? PositionTitle { get; set; }
    public string Kind { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public string? EndReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? ConversationId { get; set; }
    public string InputKey { get; set; } = null!;
}

public sealed class ChatBlock
{
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public string PairKey { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
