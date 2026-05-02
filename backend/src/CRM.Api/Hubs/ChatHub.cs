using CRM.Application.Chat;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace CRM.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IMediator _mediator;

    public ChatHub(IMediator mediator) => _mediator = mediator;

    public async Task JoinRoom(Guid roomId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomName(roomId));

    public async Task LeaveRoom(Guid roomId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomName(roomId));

    public async Task<ChatMessageDto> Send(Guid roomId, string body)
    {
        var msg = await _mediator.Send(new SendMessageCommand(roomId, body));
        await Clients.Group(RoomName(roomId)).SendAsync("MessageReceived", msg);
        return msg;
    }

    public async Task Typing(Guid roomId)
    {
        var uid = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        await Clients.OthersInGroup(RoomName(roomId)).SendAsync("Typing", roomId, uid);
    }

    private static string RoomName(Guid roomId) => $"room:{roomId}";
}
