using CRM.Application.Chat;

namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Pushes chat events to clients in real time.
/// Implemented in CRM.Api on top of SignalR; abstracted here so the Application
/// layer can broadcast without taking a dependency on SignalR.
/// </summary>
public interface IChatBroadcaster
{
    Task BroadcastMessageAsync(Guid roomId, ChatMessageDto message, CancellationToken ct = default);

    /// <summary>Notifies room members that <paramref name="userId"/> read the room up to <paramref name="readAt"/>.</summary>
    Task BroadcastRoomReadAsync(Guid roomId, Guid userId, DateTime readAt, CancellationToken ct = default);

    /// <summary>Pushes a message's full reaction set after a reaction was added/removed.</summary>
    Task BroadcastReactionsAsync(Guid roomId, Guid messageId, IReadOnlyList<ReactionDto> reactions, CancellationToken ct = default);

    /// <summary>Pushes an edited message so clients replace their copy in place.</summary>
    Task BroadcastMessageEditedAsync(Guid roomId, ChatMessageDto message, CancellationToken ct = default);

    /// <summary>Notifies room members that a message was deleted so clients remove it.</summary>
    Task BroadcastMessageDeletedAsync(Guid roomId, Guid messageId, CancellationToken ct = default);
}
