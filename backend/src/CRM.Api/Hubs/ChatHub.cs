using CRM.Application.Chat;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CRM.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IMediator _mediator;
    private readonly IApplicationDbContext _db;

    public ChatHub(IMediator mediator, IApplicationDbContext db)
    {
        _mediator = Guard.AgainstNull(mediator);
        _db = Guard.AgainstNull(db);
    }

    /// <summary>
    /// Put this connection into every room the user belongs to, as soon as it connects.
    ///
    /// Previously a client only received messages for rooms it had explicitly joined, and the only
    /// thing that ever called JoinRoom was the chat page for the room being looked at. So a message
    /// reached you only if you were already on the chat page, with that very conversation open —
    /// anywhere else in the CRM, nothing arrived until a thirty-second poll noticed the count had
    /// moved. That is not what being messaged should feel like.
    ///
    /// Joining here means one connection, held for as long as the app is open, receives everything
    /// the user is entitled to. LeaveRoom is left alone: it is about an explicitly opened room, and
    /// the per-connection groups are discarded when the connection ends anyway.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var uid))
        {
            var roomIds = await _db.ChatRoomMembers
                .Where(m => m.UserId == uid)
                .Select(m => m.RoomId)
                .ToListAsync();

            foreach (var roomId in roomIds)
                await Groups.AddToGroupAsync(Context.ConnectionId, RoomName(roomId));
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinRoom(Guid roomId)
    {
        // Membership check — WITHOUT it any authenticated user could join any room's group
        // (even one in another agency) and live-eavesdrop on every message broadcast to it.
        if (!TryGetUserId(out var uid)) return;
        var isMember = await _db.ChatRoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == uid);
        if (!isMember) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomName(roomId));
    }

    public async Task LeaveRoom(Guid roomId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomName(roomId));

    public async Task<ChatMessageDto> Send(Guid roomId, string body)
    {
        Guard.AgainstNull(body);
        // The command handler already fans the saved message out to the room group (via the
        // ChatBroadcaster). Do NOT broadcast again here, or every recipient receives it twice.
        return await _mediator.Send(new SendMessageCommand(roomId, body));
    }

    public async Task Typing(Guid roomId)
    {
        // Same membership gate as JoinRoom/Send — a non-member must not be able to inject a
        // "typing" signal into a room they don't belong to.
        if (!TryGetUserId(out var uid)) return;
        if (!await _db.ChatRoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == uid)) return;
        await Clients.OthersInGroup(RoomName(roomId)).SendAsync("Typing", roomId, uid);
    }

    private bool TryGetUserId(out Guid uid)
    {
        var v = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(v, out uid);
    }

    private static string RoomName(Guid roomId) => $"room:{roomId}";
}
