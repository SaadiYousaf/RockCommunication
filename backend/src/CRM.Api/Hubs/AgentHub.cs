using CRM.Application.Common.RealTime;
using CRM.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace CRM.Api.Hubs;

[Authorize]
public class AgentHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var uid))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(uid));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var uid))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(uid));
        await base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(Guid userId) => $"agent-{userId:N}";

    private bool TryGetUserId(out Guid uid)
    {
        var v = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(v, out uid);
    }
}

public class AgentNotifier : IAgentNotifier
{
    private readonly IHubContext<AgentHub> _hub;
    public AgentNotifier(IHubContext<AgentHub> hub) => _hub = Guard.AgainstNull(hub);

    public Task PushAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
        => _hub.Clients.Group(AgentHub.GroupName(userId)).SendAsync(eventName, payload, ct);
}
