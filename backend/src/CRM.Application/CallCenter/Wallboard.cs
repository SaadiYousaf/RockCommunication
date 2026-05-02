using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record WallboardSnapshot(
    int AgentsClockedIn, int AgentsAvailable, int AgentsOnCall, int AgentsOnBreak,
    int CallsAnsweredToday, int CallsAbandonedToday, int LeadsCreatedToday, int SalesClosedToday,
    int CallsWaitingNow, int LongestWaitSeconds,
    IReadOnlyList<TopAgentDto> TopAgentsToday);

public record TopAgentDto(Guid UserId, string UserName, int Sales, int Calls, decimal? Premium);

public record GetWallboardQuery() : IRequest<WallboardSnapshot>;

public class WallboardHandler : IRequestHandler<GetWallboardQuery, WallboardSnapshot>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    public WallboardHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity) { _db = db; _user = user; _identity = identity; }

    public async Task<WallboardSnapshot> Handle(GetWallboardQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var aid = _user.AgencyId.Value;
        var todayUtc = DateTime.UtcNow.Date;

        var openSessionIds = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.AgencyId == aid && s.ClockOutAt == null)
            .Select(s => s.Id).ToListAsync(ct);
        var currentLogs = await _db.AgentStatusLogs.AsNoTracking()
            .Where(l => openSessionIds.Contains(l.SessionId!.Value) && l.UntilAt == null)
            .Select(l => l.Status).ToListAsync(ct);

        var clockedIn = currentLogs.Count;
        var available = currentLogs.Count(s => s == AgentStatus.Available);
        var onCall = currentLogs.Count(s => s == AgentStatus.OnCall);
        var onBreak = currentLogs.Count(s => s == AgentStatus.Break || s == AgentStatus.Lunch);

        var callsAnswered = await _db.CallRecords.AsNoTracking()
            .CountAsync(c => c.AgencyId == aid && c.AnsweredAt != null && c.AnsweredAt >= todayUtc, ct);
        var callsAbandoned = await _db.QueuedCalls.AsNoTracking()
            .CountAsync(c => c.AgencyId == aid && c.Status == "Abandoned" && c.EnteredAt >= todayUtc, ct);
        var leadsCreated = await _db.Leads.AsNoTracking()
            .CountAsync(l => l.AgencyId == aid && l.CreatedAt >= todayUtc, ct);
        var salesClosed = await _db.Sales.AsNoTracking()
            .CountAsync(s => s.AgencyId == aid && s.SoldAt >= todayUtc, ct);

        var waitingNow = await _db.QueuedCalls.AsNoTracking()
            .Where(q => q.AgencyId == aid && q.Status == "Waiting")
            .Select(q => q.EnteredAt).ToListAsync(ct);
        var longestWait = waitingNow.Count == 0 ? 0
            : (int)(DateTime.UtcNow - waitingNow.Min()).TotalSeconds;

        var topRaw = await _db.Sales.AsNoTracking()
            .Where(s => s.AgencyId == aid && s.SoldAt >= todayUtc)
            .GroupBy(s => s.CloserUserId)
            .Select(g => new { UserId = g.Key, Sales = g.Count(), Premium = g.Sum(x => (decimal?)x.MonthlyPremium) })
            .OrderByDescending(x => x.Sales).Take(5).ToListAsync(ct);
        var users = await _identity.ListUsersAsync(aid, ct);
        var byId = users.ToDictionary(u => u.Id);
        var top = topRaw.Select(t => new TopAgentDto(t.UserId,
            byId.TryGetValue(t.UserId, out var u) ? u.UserName : t.UserId.ToString(),
            t.Sales, 0, t.Premium)).ToList();

        return new WallboardSnapshot(clockedIn, available, onCall, onBreak,
            callsAnswered, callsAbandoned, leadsCreated, salesClosed,
            waitingNow.Count, longestWait, top);
    }
}

public record AgentLeaderboardDto(Guid UserId, string UserName,
    int CallsToday, int SalesToday, decimal PremiumToday, int LeadsTransitionedToday);

public record GetLeaderboardQuery(string Period = "today") : IRequest<IReadOnlyList<AgentLeaderboardDto>>;

public class LeaderboardHandler : IRequestHandler<GetLeaderboardQuery, IReadOnlyList<AgentLeaderboardDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    public LeaderboardHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity) { _db = db; _user = user; _identity = identity; }

    public async Task<IReadOnlyList<AgentLeaderboardDto>> Handle(GetLeaderboardQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var aid = _user.AgencyId.Value;
        var since = request.Period switch
        {
            "week" => DateTime.UtcNow.AddDays(-7),
            "month" => DateTime.UtcNow.AddDays(-30),
            _ => DateTime.UtcNow.Date
        };

        var users = await _identity.ListUsersAsync(aid, ct);

        var calls = await _db.CallRecords.AsNoTracking()
            .Where(c => c.AgencyId == aid && c.InitiatedAt >= since)
            .GroupBy(c => c.AgentUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.AgencyId == aid && s.SoldAt >= since)
            .GroupBy(s => s.CloserUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count(), Premium = g.Sum(x => (decimal?)x.MonthlyPremium) ?? 0 })
            .ToDictionaryAsync(x => x.UserId, x => x, ct);

        var transitions = await _db.LeadActivities.AsNoTracking()
            .Where(a => a.AgencyId == aid && a.OccurredAt >= since)
            .GroupBy(a => a.UserId).Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return users.Select(u => new AgentLeaderboardDto(u.Id, u.UserName,
            calls.GetValueOrDefault(u.Id, 0),
            sales.TryGetValue(u.Id, out var s1) ? s1.Count : 0,
            sales.TryGetValue(u.Id, out var s2) ? s2.Premium : 0,
            transitions.GetValueOrDefault(u.Id, 0)))
            .OrderByDescending(x => x.SalesToday)
            .ThenByDescending(x => x.PremiumToday)
            .Take(50).ToList();
    }
}
