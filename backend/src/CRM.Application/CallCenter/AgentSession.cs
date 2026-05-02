using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Workflow;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record AgentSessionDto(Guid Id, Guid UserId, DateTime ClockInAt, DateTime? ClockOutAt,
    AgentStatus CurrentStatus, string? CurrentReason);

public record ClockInCommand() : IRequest<AgentSessionDto>;
public record ClockOutCommand() : IRequest<AgentSessionDto>;
public record SetAgentStatusCommand(AgentStatus Status, string? Reason) : IRequest<AgentSessionDto>;
public record WrapUpCallCommand(Guid CallRecordId, string WrapUpCode, string? Notes) : IRequest<Unit>;
public record GetMySessionQuery() : IRequest<AgentSessionDto?>;

public class WrapUpCallValidator : AbstractValidator<WrapUpCallCommand>
{
    public WrapUpCallValidator()
    {
        RuleFor(x => x.CallRecordId).NotEmpty();
        RuleFor(x => x.WrapUpCode).NotEmpty();
    }
}

public class AgentSessionHandler :
    IRequestHandler<ClockInCommand, AgentSessionDto>,
    IRequestHandler<ClockOutCommand, AgentSessionDto>,
    IRequestHandler<SetAgentStatusCommand, AgentSessionDto>,
    IRequestHandler<WrapUpCallCommand, Unit>,
    IRequestHandler<GetMySessionQuery, AgentSessionDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IWorkflowEngine _workflow;

    public AgentSessionHandler(IApplicationDbContext db, ICurrentUser user, IWorkflowEngine workflow)
    {
        _db = db;
        _user = user;
        _workflow = workflow;
    }

    public async Task<AgentSessionDto> Handle(ClockInCommand request, CancellationToken ct)
    {
        EnsureAuthenticated();
        var open = await CurrentSessionAsync(ct);
        if (open is not null) return await ToDto(open, ct);

        var session = new AgentSession
        {
            AgencyId = _user.AgencyId!.Value,
            UserId = _user.UserId!.Value
        };
        _db.AgentSessions.Add(session);
        _db.AgentStatusLogs.Add(new AgentStatusLog
        {
            AgencyId = session.AgencyId,
            UserId = session.UserId,
            SessionId = session.Id,
            Status = AgentStatus.Available,
            FromAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return await ToDto(session, ct);
    }

    public async Task<AgentSessionDto> Handle(ClockOutCommand request, CancellationToken ct)
    {
        EnsureAuthenticated();
        var session = await CurrentSessionAsync(ct)
            ?? throw new ConflictException("Not clocked in.");

        var lastLog = await _db.AgentStatusLogs
            .Where(l => l.SessionId == session.Id && l.UntilAt == null)
            .OrderByDescending(l => l.FromAt).FirstOrDefaultAsync(ct);
        if (lastLog is not null) lastLog.UntilAt = DateTime.UtcNow;

        _db.AgentStatusLogs.Add(new AgentStatusLog
        {
            AgencyId = session.AgencyId,
            UserId = session.UserId,
            SessionId = session.Id,
            Status = AgentStatus.Offline,
            FromAt = DateTime.UtcNow
        });

        session.ClockOutAt = DateTime.UtcNow;
        AccumulateTotals(session, lastLog);
        await _db.SaveChangesAsync(ct);
        return await ToDto(session, ct);
    }

    public async Task<AgentSessionDto> Handle(SetAgentStatusCommand request, CancellationToken ct)
    {
        EnsureAuthenticated();
        var session = await CurrentSessionAsync(ct)
            ?? throw new ConflictException("Not clocked in.");

        // Block "Available" if there is an unresolved on-call wrap-up
        if (request.Status == AgentStatus.Available)
        {
            var unwrapped = await _db.CallRecords.AnyAsync(c =>
                c.AgencyId == session.AgencyId &&
                c.AgentUserId == session.UserId &&
                c.EndedAt != null &&
                (c.WrapUpCode == null || c.WrapUpCode == ""), ct);
            if (unwrapped)
                throw new ConflictException("You must wrap up your last call before going available.");
        }

        var lastLog = await _db.AgentStatusLogs
            .Where(l => l.SessionId == session.Id && l.UntilAt == null)
            .OrderByDescending(l => l.FromAt).FirstOrDefaultAsync(ct);
        if (lastLog is not null) lastLog.UntilAt = DateTime.UtcNow;

        _db.AgentStatusLogs.Add(new AgentStatusLog
        {
            AgencyId = session.AgencyId,
            UserId = session.UserId,
            SessionId = session.Id,
            Status = request.Status,
            Reason = request.Reason,
            FromAt = DateTime.UtcNow
        });
        AccumulateTotals(session, lastLog);
        await _db.SaveChangesAsync(ct);
        return await ToDto(session, ct);
    }

    public async Task<Unit> Handle(WrapUpCallCommand request, CancellationToken ct)
    {
        EnsureAuthenticated();
        var call = await _db.CallRecords.FirstOrDefaultAsync(
            c => c.Id == request.CallRecordId && c.AgentUserId == _user.UserId
                && c.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(CallRecord), request.CallRecordId);

        var code = await _db.WrapUpCodes.FirstOrDefaultAsync(
            w => w.AgencyId == _user.AgencyId && w.Code == request.WrapUpCode && w.IsActive, ct)
            ?? throw new ConflictException($"Unknown wrap-up code '{request.WrapUpCode}'.");

        if (call.EndedAt is null) call.EndedAt = DateTime.UtcNow;
        call.WrapUpCode = code.Code;
        call.Notes = request.Notes;
        await _db.SaveChangesAsync(ct);

        await _workflow.PublishAsync(new CallCompletedEvent
        {
            AgencyId = call.AgencyId,
            CallId = call.Id,
            LeadId = call.LeadId,
            AgentUserId = call.AgentUserId,
            WrapUpCode = call.WrapUpCode,
            TalkTime = call.TalkTime,
            Direction = call.Direction
        }, ct);

        return Unit.Value;
    }

    public async Task<AgentSessionDto?> Handle(GetMySessionQuery request, CancellationToken ct)
    {
        EnsureAuthenticated();
        var session = await CurrentSessionAsync(ct);
        return session is null ? null : await ToDto(session, ct);
    }

    private void EnsureAuthenticated()
    {
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
    }

    private Task<AgentSession?> CurrentSessionAsync(CancellationToken ct) =>
        _db.AgentSessions
            .Where(s => s.AgencyId == _user.AgencyId && s.UserId == _user.UserId && s.ClockOutAt == null)
            .OrderByDescending(s => s.ClockInAt)
            .FirstOrDefaultAsync(ct);

    private static void AccumulateTotals(AgentSession session, AgentStatusLog? log)
    {
        if (log is null || log.UntilAt is null) return;
        var dur = log.UntilAt.Value - log.FromAt;
        switch (log.Status)
        {
            case AgentStatus.Available: session.TotalAvailable += dur; break;
            case AgentStatus.OnCall: session.TotalOnCall += dur; break;
            case AgentStatus.Break:
            case AgentStatus.Lunch: session.TotalBreak += dur; break;
        }
    }

    private async Task<AgentSessionDto> ToDto(AgentSession s, CancellationToken ct)
    {
        var current = await _db.AgentStatusLogs
            .Where(l => l.SessionId == s.Id && l.UntilAt == null)
            .OrderByDescending(l => l.FromAt).FirstOrDefaultAsync(ct);
        return new AgentSessionDto(s.Id, s.UserId, s.ClockInAt, s.ClockOutAt,
            current?.Status ?? AgentStatus.Offline, current?.Reason);
    }
}
