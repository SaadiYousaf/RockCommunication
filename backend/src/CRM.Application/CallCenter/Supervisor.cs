using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record LiveAgentDto(
    Guid UserId, string UserName, AgentStatus Status, string? Reason,
    DateTime SinceAt, TimeSpan Duration,
    Guid? CurrentCallId, string? CurrentCallStatus);

public record LiveAgentBoardQuery() : IRequest<IReadOnlyList<LiveAgentDto>>;

public class LiveAgentBoardHandler : IRequestHandler<LiveAgentBoardQuery, IReadOnlyList<LiveAgentDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public LiveAgentBoardHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _identity = Guard.AgainstNull(identity);
    }

    public async Task<IReadOnlyList<LiveAgentDto>> Handle(LiveAgentBoardQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var openSessions = await _db.AgentSessions
            .Where(s => s.AgencyId == _user.AgencyId && s.ClockOutAt == null)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var logs = await _db.AgentStatusLogs
            .Where(l => openSessions.Contains(l.SessionId!.Value) && l.UntilAt == null)
            .ToListAsync(ct);

        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);

        var list = new List<LiveAgentDto>();
        foreach (var l in logs)
        {
            byId.TryGetValue(l.UserId, out var u);
            var liveCall = await _db.CallRecords
                .Where(c => c.AgentUserId == l.UserId && c.AgencyId == _user.AgencyId && c.EndedAt == null)
                .OrderByDescending(c => c.InitiatedAt).FirstOrDefaultAsync(ct);
            list.Add(new LiveAgentDto(l.UserId, u?.UserName ?? l.UserId.ToString(),
                l.Status, l.Reason, l.FromAt, DateTime.UtcNow - l.FromAt,
                liveCall?.Id, liveCall?.Status));
        }
        return list.OrderBy(a => a.UserName).ToList();
    }
}

// Force-logout / status change for supervisor
public record ForceAgentStatusCommand(Guid UserId, AgentStatus Status, string? Reason) : IRequest<Unit>;

public class ForceAgentStatusHandler : IRequestHandler<ForceAgentStatusCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public ForceAgentStatusHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<Unit> Handle(ForceAgentStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager") && !_user.Roles.Contains("TeamLead"))
            throw new ForbiddenAccessException();

        var session = await _db.AgentSessions
            .Where(s => s.AgencyId == _user.AgencyId && s.UserId == request.UserId && s.ClockOutAt == null)
            .OrderByDescending(s => s.ClockInAt).FirstOrDefaultAsync(ct);
        if (session is null) return Unit.Value;

        var lastLog = await _db.AgentStatusLogs
            .Where(l => l.SessionId == session.Id && l.UntilAt == null)
            .OrderByDescending(l => l.FromAt).FirstOrDefaultAsync(ct);
        if (lastLog is not null) lastLog.UntilAt = DateTime.UtcNow;

        _db.AgentStatusLogs.Add(new Domain.Entities.AgentStatusLog
        {
            AgencyId = session.AgencyId,
            UserId = session.UserId,
            SessionId = session.Id,
            Status = request.Status,
            Reason = request.Reason ?? "Supervisor change",
            FromAt = DateTime.UtcNow
        });
        if (request.Status == AgentStatus.Offline)
            session.ClockOutAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// Listen / Whisper / Barge — placeholders that ask the dialer to start a coaching call
public record CoachCallCommand(Guid TargetUserId, string Mode) : IRequest<Unit>;

public class CoachCallHandler : IRequestHandler<CoachCallCommand, Unit>
{
    private readonly ICurrentUser _user;
    private readonly IDialerProvider _dialer;
    public CoachCallHandler(ICurrentUser user, IDialerProvider dialer) { _user = Guard.AgainstNull(user); _dialer = Guard.AgainstNull(dialer); }

    public Task<Unit> Handle(CoachCallCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager") && !_user.Roles.Contains("TeamLead"))
            throw new ForbiddenAccessException();

        // Vici and most dialers expose a function for monitor/whisper/barge — wired to provider for future expansion.
        // Currently delegates to the configured dialer with a synthetic phone "coach:<mode>:<targetUser>"
        return _dialer.DialAsync(_user.UserId.Value,
            $"coach:{request.Mode.ToLowerInvariant()}:{request.TargetUserId}", request.TargetUserId, ct)
            .ContinueWith(_ => Unit.Value);
    }
}
