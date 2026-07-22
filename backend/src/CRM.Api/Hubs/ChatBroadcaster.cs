using CRM.Application.Chat;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using Microsoft.AspNetCore.SignalR;

namespace CRM.Api.Hubs;

/// <summary>
/// Bridges <see cref="IChatBroadcaster"/> (Application) to SignalR's <see cref="ChatHub"/>.
/// Used by the HTTP-side <c>SendMessageHandler</c> to fan a freshly-saved message out to
/// every client that has joined the room's SignalR group, so React clients receive the
/// message in real time instead of having to refetch.
/// </summary>
public class ChatBroadcaster : IChatBroadcaster
{
    private readonly IHubContext<ChatHub> _hub;

    public ChatBroadcaster(IHubContext<ChatHub> hub) => _hub = Guard.AgainstNull(hub);

    public Task BroadcastMessageAsync(Guid roomId, ChatMessageDto message, CancellationToken ct = default)
        => _hub.Clients.Group($"room:{roomId}").SendAsync("MessageReceived", message, ct);

    public Task BroadcastRoomReadAsync(Guid roomId, Guid userId, DateTime readAt, CancellationToken ct = default)
        => _hub.Clients.Group($"room:{roomId}").SendAsync("RoomRead", new { roomId, userId, readAt }, ct);
}
