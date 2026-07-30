using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Hr;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using DomainRoles = CRM.Domain.Enums.Roles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Dashboard;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>
/// One row in the dashboard "Team status" widget: an employee in the viewer's call centre with
/// today's <see cref="AttendanceStatus"/> (null = unmarked) and their live work state
/// (<see cref="LiveStatus"/>, one of <see cref="TeamLiveStatus"/>).
/// </summary>
public record TeamStatusRowDto(
    Guid EmployeeId, string FullName, string AgentCode, EmployeeDesignation Designation,
    Guid? CallCenterId, string? CallCenterName,
    string? AttendanceStatus, string LiveStatus);

/// <summary>Live-work-state discriminators shared with the client (no magic strings in handlers).</summary>
public static class TeamLiveStatus
{
    public const string OnCall = "OnCall";
    public const string Available = "Available";
    public const string OnBreak = "OnBreak";
    public const string ClockedOut = "ClockedOut";
}

// ── Request ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Every employee in the viewer's call centre with their status today. Attendance comes from the HR
/// marks (<see cref="Domain.Entities.EmployeeAttendance"/>); the live work state reuses the same
/// source the wallboard/supervisor board reads — open agent sessions + their current status log.
/// Scoped per role: call-centre-pinned callers see their own centre only, agency-level callers see
/// the whole agency (optionally narrowed by <paramref name="CallCenterId"/>), and SuperAdmin must
/// name a call centre or gets nothing back.
/// </summary>
public record TeamStatusQuery(Guid? CallCenterId = null) : IRequest<IReadOnlyList<TeamStatusRowDto>>;

// ── Handler ───────────────────────────────────────────────────────────────────

public class TeamStatusHandler : IRequestHandler<TeamStatusQuery, IReadOnlyList<TeamStatusRowDto>>
{
    /// <summary>TeamLead and above — the rank floor for "supervisory/management" (see <see cref="DomainRoles.RankOf"/>).</summary>
    private const int SupervisorRank = 40;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public TeamStatusHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<TeamStatusRowDto>> Handle(TeamStatusQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // A floor agent must not see the whole roster — gate to HR + supervisory/management, the
        // same boundary the wallboard/supervisor board enforce.
        if (!CanView(_user)) throw new ForbiddenAccessException();

        var employeesQuery = _db.Employees.AsNoTracking().AsQueryable();

        if (_user.CallCenterId is { } pinned)
        {
            // Call-centre-pinned caller: their own centre only. Employee is agency-scoped by the
            // global query filter (the call-centre dimension is NOT a filter dimension for it), so
            // we add it here — and ignore the request arg so a pinned user can't peek elsewhere.
            employeesQuery = employeesQuery.Where(e => e.CallCenterId == pinned);
        }
        else if (_user.IsSuperAdmin)
        {
            // Cross-tenant operator: never dump every agency. Require an explicit call centre.
            if (request.CallCenterId is not { } cc) return Array.Empty<TeamStatusRowDto>();
            employeesQuery = employeesQuery.Where(e => e.CallCenterId == cc);
        }
        else if (_user.AgencyId is not null)
        {
            // Agency-level caller: the query filter already scopes to their agency; an optional
            // CallCenterId narrows to a single centre.
            if (request.CallCenterId is { } cc) employeesQuery = employeesQuery.Where(e => e.CallCenterId == cc);
        }
        else
        {
            // Authenticated, no agency, not SuperAdmin → fail closed.
            return Array.Empty<TeamStatusRowDto>();
        }

        var employees = await employeesQuery
            .OrderBy(e => e.FullName)
            .Select(e => new { e.Id, e.FullName, e.AgentCode, e.Designation, e.CallCenterId, e.UserId })
            .ToListAsync(ct);
        if (employees.Count == 0) return Array.Empty<TeamStatusRowDto>();

        var empIds = employees.Select(e => e.Id).ToList();

        // Today's attendance marks (absent from the dictionary → unmarked → null in the DTO).
        var today = DateTime.UtcNow.Date;
        var marks = await _db.EmployeeAttendances.AsNoTracking()
            .Where(a => a.Date == today && empIds.Contains(a.EmployeeId))
            .Select(a => new { a.EmployeeId, a.Status })
            .ToDictionaryAsync(a => a.EmployeeId, a => a.Status, ct);

        // Live work state — best-effort; a presence failure must never blank the attendance column.
        var userIds = employees
            .Where(e => e.UserId is { } u && u != Guid.Empty)
            .Select(e => e.UserId!.Value).Distinct().ToList();
        var liveByUser = await LiveStatusByUserAsync(userIds, ct);

        var ccNames = await CallCenterNamesAsync(employees.Select(e => e.CallCenterId), ct);

        return employees.Select(e =>
        {
            var attendance = marks.TryGetValue(e.Id, out var s) ? s.ToString() : null;
            var live = e.UserId is { } uid && liveByUser.TryGetValue(uid, out var st)
                ? st : TeamLiveStatus.ClockedOut;
            return new TeamStatusRowDto(e.Id, e.FullName, e.AgentCode, e.Designation, e.CallCenterId,
                NameOrNull(ccNames, e.CallCenterId), attendance, live);
        }).ToList();
    }

    /// <summary>HR (attendance) plus supervisory/management (live floor) — anyone below TeamLead is out.</summary>
    private static bool CanView(ICurrentUser user) =>
        HrAccess.IsHr(user) || DomainRoles.RankOf(user.Roles) >= SupervisorRank;

    /// <summary>
    /// Current live status per user, derived from the SAME source as the wallboard/supervisor board:
    /// each open agent session (<c>ClockOutAt == null</c>) and its current status log
    /// (<c>UntilAt == null</c>). Users not clocked in are simply absent → treated as clocked out.
    /// Wrapped so a live-data failure degrades to "clocked out" rather than crashing the widget.
    /// </summary>
    private async Task<Dictionary<Guid, string>> LiveStatusByUserAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        if (userIds.Count == 0) return result;
        try
        {
            var openSessionIds = await _db.AgentSessions.AsNoTracking()
                .Where(s => s.ClockOutAt == null && userIds.Contains(s.UserId))
                .Select(s => s.Id).ToListAsync(ct);
            if (openSessionIds.Count == 0) return result;

            var currentLogs = await _db.AgentStatusLogs.AsNoTracking()
                .Where(l => l.UntilAt == null && openSessionIds.Contains(l.SessionId!.Value))
                .Select(l => new { l.UserId, l.Status })
                .ToListAsync(ct);

            foreach (var log in currentLogs)
                result[log.UserId] = MapLiveStatus(log.Status);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Presence is best-effort — everyone falls back to "clocked out"; attendance still renders.
            return new Dictionary<Guid, string>();
        }
        return result;
    }

    private static string MapLiveStatus(AgentStatus status) => status switch
    {
        AgentStatus.OnCall => TeamLiveStatus.OnCall,
        AgentStatus.Available => TeamLiveStatus.Available,
        // Break / Lunch / Training / Meeting are all "clocked in but away from live calls".
        AgentStatus.Break or AgentStatus.Lunch or AgentStatus.Training or AgentStatus.Meeting => TeamLiveStatus.OnBreak,
        _ => TeamLiveStatus.ClockedOut,   // Offline / anything unexpected
    };

    private async Task<Dictionary<Guid, string>> CallCenterNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(x => x is { } g && g != Guid.Empty).Select(x => x!.Value).Distinct().ToList();
        if (set.Count == 0) return new();
        return (await _db.CallCenters.AsNoTracking().IgnoreQueryFilters()
                .Where(c => set.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.Name);
    }

    private static string? NameOrNull(Dictionary<Guid, string> names, Guid? id) =>
        id is { } g && names.TryGetValue(g, out var n) ? n : null;
}
