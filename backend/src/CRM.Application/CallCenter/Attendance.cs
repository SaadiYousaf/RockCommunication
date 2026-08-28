using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

// ── Attendance / Time & Activity ─────────────────────────────────────────────
// Clock-in/out, break, lunch and status timeline for each user, built from AgentSession +
// AgentStatusLog. A management view — gated to Admin / CallCenterAdmin / SuperAdmin. Durations are
// recomputed live from the status segments so an open (currently-clocked-in) session stays accurate.

public record AttendanceSegmentDto(string Status, string? Reason, DateTime FromAt, DateTime? UntilAt, double Minutes);

public record AttendanceSessionDto(
    Guid Id, DateTime ClockInAt, DateTime? ClockOutAt,
    double ClockedMinutes, double AvailableMinutes, double OnCallMinutes, double BreakMinutes,
    IReadOnlyList<AttendanceSegmentDto> Segments);

public record AttendanceRowDto(
    Guid UserId, string UserName,
    bool ClockedIn, string CurrentStatus,
    int SessionCount, DateTime? FirstClockIn, DateTime? LastActivity,
    double TotalClockedMinutes, double TotalAvailableMinutes, double TotalOnCallMinutes, double TotalBreakMinutes,
    IReadOnlyList<AttendanceSessionDto> Sessions);

/// <summary>
/// Attendance for sessions overlapping [From, To). AgencyId is honored for SuperAdmin only
/// (they must target one agency); tenant callers are scoped by the call-center query filter —
/// an agency-level Admin sees every call centre, a pinned Call Center Admin sees only theirs.
/// </summary>
public record AttendanceQuery(DateTime From, DateTime To, Guid? UserId = null, Guid? AgencyId = null)
    : IRequest<IReadOnlyList<AttendanceRowDto>>;

public class AttendanceHandler : IRequestHandler<AttendanceQuery, IReadOnlyList<AttendanceRowDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public AttendanceHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _identity = Guard.AgainstNull(identity);
    }

    public async Task<IReadOnlyList<AttendanceRowDto>> Handle(AttendanceQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);

        Guid agencyId;
        IQueryable<AgentSession> sessQ;
        IQueryable<AgentStatusLog> logQ;
        if (_user.IsSuperAdmin)
        {
            // Prefer an explicit ?agencyId, but fall back to the agency this session is scoped to
            // (POST /api/auth/context) before refusing — otherwise a SuperAdmin who has already
            // picked a working context gets a 403 that the page can only render as "no data".
            var target =
                request.AgencyId is { } aid && aid != Guid.Empty ? aid
                : _user.AgencyId is { } scoped && scoped != Guid.Empty ? scoped
                : Guid.Empty;
            if (target == Guid.Empty)
                throw new ForbiddenAccessException("Select an agency to view attendance.");
            agencyId = target;
            sessQ = _db.AgentSessions.IgnoreQueryFilters().Where(s => s.AgencyId == agencyId && !s.IsDeleted);
            logQ = _db.AgentStatusLogs.IgnoreQueryFilters().Where(l => l.AgencyId == agencyId && !l.IsDeleted);
        }
        else
        {
            if (_user.AgencyId is not { } uaid || uaid == Guid.Empty) throw new ForbiddenAccessException();
            agencyId = uaid;
            sessQ = _db.AgentSessions;          // call-center query filter applies
            logQ = _db.AgentStatusLogs;
        }

        // Sessions overlapping the window: started before To AND (still open OR ended at/after From).
        sessQ = sessQ.Where(s => s.ClockInAt < request.To && (s.ClockOutAt == null || s.ClockOutAt >= request.From));
        if (request.UserId is { } uid) sessQ = sessQ.Where(s => s.UserId == uid);

        var sessions = await sessQ.AsNoTracking().OrderByDescending(s => s.ClockInAt).ToListAsync(ct);
        if (sessions.Count == 0) return Array.Empty<AttendanceRowDto>();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var logs = await logQ.AsNoTracking()
            .Where(l => l.SessionId != null && sessionIds.Contains(l.SessionId.Value))
            .OrderBy(l => l.FromAt).ToListAsync(ct);
        var logsBySession = logs.GroupBy(l => l.SessionId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var names = await _identity.ListUserNamesAsync(agencyId, ct);
        var now = DateTime.UtcNow;

        static double Minutes(DateTime from, DateTime? until, DateTime now)
            => Math.Max(0, ((until ?? now) - from).TotalMinutes);

        var sessionDtos = sessions.Select(s =>
        {
            var segs = (logsBySession.TryGetValue(s.Id, out var ls) ? ls : new List<AgentStatusLog>())
                .Select(l => new AttendanceSegmentDto(
                    l.Status.ToString(), l.Reason, l.FromAt, l.UntilAt, Minutes(l.FromAt, l.UntilAt, now)))
                .ToList();

            double SumFor(params AgentStatus[] statuses)
            {
                var set = statuses.Select(x => x.ToString()).ToHashSet();
                return segs.Where(x => set.Contains(x.Status)).Sum(x => x.Minutes);
            }

            return (s.UserId, dto: new AttendanceSessionDto(
                s.Id, s.ClockInAt, s.ClockOutAt,
                Minutes(s.ClockInAt, s.ClockOutAt, now),
                SumFor(AgentStatus.Available),
                SumFor(AgentStatus.OnCall),
                SumFor(AgentStatus.Break, AgentStatus.Lunch),
                segs));
        }).ToList();

        return sessionDtos
            .GroupBy(x => x.UserId)
            .Select(g =>
            {
                var userSessions = g.Select(x => x.dto).OrderByDescending(x => x.ClockInAt).ToList();
                var open = userSessions.FirstOrDefault(x => x.ClockOutAt == null);
                var currentStatus = open is null ? "Offline"
                    : logsBySession.TryGetValue(open.Id, out var ols)
                        ? (ols.LastOrDefault(l => l.UntilAt == null)?.Status.ToString() ?? "Offline")
                        : "Offline";
                return new AttendanceRowDto(
                    g.Key, names.TryGetValue(g.Key, out var n) ? n : "—",
                    open is not null, currentStatus,
                    userSessions.Count,
                    userSessions.Min(x => x.ClockInAt),
                    userSessions.Max(x => (DateTime?)(x.ClockOutAt ?? x.ClockInAt)),
                    userSessions.Sum(x => x.ClockedMinutes),
                    userSessions.Sum(x => x.AvailableMinutes),
                    userSessions.Sum(x => x.OnCallMinutes),
                    userSessions.Sum(x => x.BreakMinutes),
                    userSessions);
            })
            .OrderByDescending(r => r.ClockedIn).ThenBy(r => r.UserName)
            .ToList();
    }
}
