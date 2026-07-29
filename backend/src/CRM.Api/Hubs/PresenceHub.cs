using CRM.Application.Common.Interfaces;
using CRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace CRM.Api.Hubs;

[Authorize]
public class PresenceHub : Hub
{
    // userId → (agencyId, status). AgencyId lets every broadcast + GetAll be scoped to a single
    // agency — previously this used Clients.All and returned the whole dictionary, leaking every
    // agency's user GUIDs + online status to every other tenant.
    private static readonly ConcurrentDictionary<Guid, (Guid AgencyId, AgentStatus Status)> _statuses = new();

    public override async Task OnConnectedAsync()
    {
        if (TryGetIds(out var uid, out var agencyId))
        {
            _statuses[uid] = (agencyId, AgentStatus.Available);
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(agencyId));
            await Clients.Group(Group(agencyId)).SendAsync("PresenceChanged", uid, AgentStatus.Available);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetIds(out var uid, out var agencyId))
        {
            _statuses[uid] = (agencyId, AgentStatus.Offline);
            await Clients.Group(Group(agencyId)).SendAsync("PresenceChanged", uid, AgentStatus.Offline);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SetStatus(AgentStatus status)
    {
        if (!TryGetIds(out var uid, out var agencyId)) return;
        _statuses[uid] = (agencyId, status);
        await Clients.Group(Group(agencyId)).SendAsync("PresenceChanged", uid, status);
    }

    public Task<Dictionary<Guid, AgentStatus>> GetAll()
    {
        if (!TryGetIds(out _, out var agencyId)) return Task.FromResult(new Dictionary<Guid, AgentStatus>());
        var mine = _statuses.Where(kv => kv.Value.AgencyId == agencyId)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Status);
        return Task.FromResult(mine);
    }

    private static string Group(Guid agencyId) => $"presence:{agencyId}";

    private bool TryGetIds(out Guid uid, out Guid agencyId)
    {
        Guid.TryParse(Context.User?.FindFirstValue(CustomJwtClaims.Agency), out agencyId);
        var v = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(v, out uid);
    }
}
