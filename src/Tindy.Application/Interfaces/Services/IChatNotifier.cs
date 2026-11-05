using Tindy.Application.DTOs.Chat;
namespace Tindy.Application.Interfaces.Services;

public interface IChatNotifier
{
    Task MessageCreatedAsync(IEnumerable<Guid> users, ChatMessageDto message, CancellationToken ct);
    Task RequestsChangedAsync(IEnumerable<Guid> users, CancellationToken ct);
    Task ReadAsync(IEnumerable<Guid> users, Guid conversationId, CancellationToken ct);
}