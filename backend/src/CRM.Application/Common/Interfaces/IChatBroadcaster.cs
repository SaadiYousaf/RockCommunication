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
}
