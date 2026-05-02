using CRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace CRM.Api.Hubs;

[Authorize]
public class PresenceHub : Hub
{
    private static readonly ConcurrentDictionary<Guid, AgentStatus> _statuses = new();

    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var uid))
        {
            _statuses[uid] = AgentStatus.Available;
            await Clients.All.SendAsync("PresenceChanged", uid, AgentStatus.Available);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var uid))
        {
            _statuses[uid] = AgentStatus.Offline;
            await Clients.All.SendAsync("PresenceChanged", uid, AgentStatus.Offline);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SetStatus(AgentStatus status)
    {
        if (!TryGetUserId(out var uid)) return;
        _statuses[uid] = status;
        await Clients.All.SendAsync("PresenceChanged", uid, status);
    }

    public Task<Dictionary<Guid, AgentStatus>> GetAll() => Task.FromResult(new Dictionary<Guid, AgentStatus>(_statuses));

    private bool TryGetUserId(out Guid uid)
    {
        var v = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(v, out uid);
    }
}
