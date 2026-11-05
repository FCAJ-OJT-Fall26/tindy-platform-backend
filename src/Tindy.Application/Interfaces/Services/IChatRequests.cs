using Tindy.Application.DTOs.Chat;
namespace Tindy.Application.Interfaces.Services;

public interface IChatRequests
{
    Task<ChatRequestResult> CreateAsync(Guid user, CreateChatRequest input, CancellationToken ct);
    Task<ChatRequestsPage> ListAsync(Guid user, string direction, bool history, int page, CancellationToken ct);
    Task<ChatRequestDto> ActAsync(Guid user, Guid requestId, string action, CancellationToken ct);
    Task SetBlockAsync(Guid user, Guid target, bool blocked, CancellationToken ct);
    Task<IReadOnlyList<ChatBlockDto>> BlocksAsync(Guid user, CancellationToken ct);
    Task<IReadOnlyList<ChatContextOption>> ContextsAsync(Guid user, Guid? target, CancellationToken ct);
    Task SweepAsync(CancellationToken ct);
    Task<ChatMessageDto> ShareContextAsync(Guid user, Guid conversationId, ShareChatContextRequest input, CancellationToken ct);
}
