using CRM.Application.Common.Compliance;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.RealTime;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record ActiveCallDto(
    Guid Id, Guid LeadId, string LeadName, string Phone,
    string Direction, string Status,
    DateTime InitiatedAt, DateTime? AnsweredAt, DateTime? EndedAt,
    bool IsHeld, bool IsMuted, string? RecordingUrl);

public record StartOutboundCallCommand(Guid LeadId) : IRequest<ActiveCallDto>;
public record AnswerCallCommand(Guid CallId) : IRequest<ActiveCallDto>;
public record HangupCallCommand(Guid CallId) : IRequest<ActiveCallDto>;
public record ToggleHoldCommand(Guid CallId, bool Hold) : IRequest<ActiveCallDto>;
public record ToggleMuteCommand(Guid CallId, bool Mute) : IRequest<ActiveCallDto>;
public record SendDtmfCommand(Guid CallId, string Digits) : IRequest<Unit>;
public record SendQuickSmsCommand(Guid LeadId, string Body) : IRequest<Unit>;
public record GetMyActiveCallQuery() : IRequest<ActiveCallDto?>;

public class StartOutboundCallValidator : AbstractValidator<StartOutboundCallCommand>
{
    public StartOutboundCallValidator() => RuleFor(x => x.LeadId).NotEmpty();
}

public class CallControlHandler :
    IRequestHandler<StartOutboundCallCommand, ActiveCallDto>,
    IRequestHandler<AnswerCallCommand, ActiveCallDto>,
    IRequestHandler<HangupCallCommand, ActiveCallDto>,
    IRequestHandler<ToggleHoldCommand, ActiveCallDto>,
    IRequestHandler<ToggleMuteCommand, ActiveCallDto>,
    IRequestHandler<SendDtmfCommand, Unit>,
    IRequestHandler<SendQuickSmsCommand, Unit>,
    IRequestHandler<GetMyActiveCallQuery, ActiveCallDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDialerProvider _dialer;
    private readonly ISmsProvider _sms;
    private readonly IComplianceGuard _compliance;
    private readonly IAgentNotifier _notifier;

    // In-memory call control state (hold/mute) keyed by call id.
    // For production with multiple API instances behind a load balancer, push to Redis.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CallState> _state = new();

    public CallControlHandler(IApplicationDbContext db, ICurrentUser user, IDialerProvider dialer,
        ISmsProvider sms, IComplianceGuard compliance, IAgentNotifier notifier)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _dialer = Guard.AgainstNull(dialer); _sms = Guard.AgainstNull(sms); _compliance = Guard.AgainstNull(compliance); _notifier = Guard.AgainstNull(notifier);
    }

    public async Task<ActiveCallDto> Handle(StartOutboundCallCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAgent();
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var compliance = await _compliance.CheckOutboundDialAsync(lead.AgencyId, lead.PhoneNumber, lead.State, ct);
        if (!compliance.Allowed)
            throw new ConflictException(compliance.BlockReason ?? "Call blocked by compliance.");

        var providerCallId = Guid.NewGuid().ToString("N");
        var dial = await _dialer.DialAsync(_user.UserId!.Value, lead.PhoneNumber, lead.Id, ct);

        var call = new CallRecord
        {
            AgencyId = lead.AgencyId,
            LeadId = lead.Id,
            AgentUserId = _user.UserId.Value,
            Provider = _dialer.Name,
            ProviderCallId = string.IsNullOrEmpty(dial.CallId) ? providerCallId : dial.CallId,
            Status = "ringing",
            Direction = "Outbound",
            InitiatedAt = DateTime.UtcNow
        };
        _db.CallRecords.Add(call);
        await _db.SaveChangesAsync(ct);

        _state[call.Id] = new CallState();

        var dto = ToDto(call, lead, _state[call.Id]);
        await _notifier.PushAsync(_user.UserId.Value, AgentEvents.CallRinging, dto, ct);
        return dto;
    }

    public async Task<ActiveCallDto> Handle(AnswerCallCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (call, lead) = await LoadAsync(request.CallId, ct);
        if (call.AnsweredAt is null)
        {
            call.AnsweredAt = DateTime.UtcNow;
            call.Status = "answered";
            await _db.SaveChangesAsync(ct);
        }
        var dto = ToDto(call, lead, _state.GetOrAdd(call.Id, _ => new()));
        await _notifier.PushAsync(call.AgentUserId, AgentEvents.CallAnswered, dto, ct);
        return dto;
    }

    public async Task<ActiveCallDto> Handle(HangupCallCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (call, lead) = await LoadAsync(request.CallId, ct);
        try { await _dialer.HangupAsync(call.ProviderCallId, ct); } catch { }

        if (call.EndedAt is null)
        {
            call.EndedAt = DateTime.UtcNow;
            call.Status = "ended";
            await _db.SaveChangesAsync(ct);
        }
        _state.TryRemove(call.Id, out _);

        var dto = ToDto(call, lead, new CallState());
        await _notifier.PushAsync(call.AgentUserId, AgentEvents.CallEnded, dto, ct);
        return dto;
    }

    public async Task<ActiveCallDto> Handle(ToggleHoldCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (call, lead) = await LoadAsync(request.CallId, ct);
        var s = _state.GetOrAdd(call.Id, _ => new());
        s.IsHeld = request.Hold;
        var dto = ToDto(call, lead, s);
        await _notifier.PushAsync(call.AgentUserId, AgentEvents.CallStateChanged, dto, ct);
        return dto;
    }

    public async Task<ActiveCallDto> Handle(ToggleMuteCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (call, lead) = await LoadAsync(request.CallId, ct);
        var s = _state.GetOrAdd(call.Id, _ => new());
        s.IsMuted = request.Mute;
        var dto = ToDto(call, lead, s);
        await _notifier.PushAsync(call.AgentUserId, AgentEvents.CallStateChanged, dto, ct);
        return dto;
    }

    public Task<Unit> Handle(SendDtmfCommand request, CancellationToken ct) => Task.FromResult(Unit.Value);

    public async Task<Unit> Handle(SendQuickSmsCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAgent();
        var lead = await _db.Leads.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);
        if (string.IsNullOrEmpty(lead.PhoneNumber)) throw new ConflictException("Lead has no phone.");
        await _sms.SendAsync(new SmsMessage(lead.PhoneNumber, request.Body, _user.UserName), ct);
        return Unit.Value;
    }

    public async Task<ActiveCallDto?> Handle(GetMyActiveCallQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var call = await _db.CallRecords
            .Where(c => c.AgencyId == _user.AgencyId && c.AgentUserId == _user.UserId && c.EndedAt == null)
            .OrderByDescending(c => c.InitiatedAt).FirstOrDefaultAsync(ct);
        if (call is null) return null;
        var lead = await _db.Leads.AsNoTracking().FirstAsync(l => l.Id == call.LeadId, ct);
        return ToDto(call, lead, _state.GetOrAdd(call.Id, _ => new()));
    }

    private async Task<(CallRecord call, Lead lead)> LoadAsync(Guid callId, CancellationToken ct)
    {
        EnsureAgent();
        var call = await _db.CallRecords.FirstOrDefaultAsync(
            c => c.Id == callId && c.AgencyId == _user.AgencyId && c.AgentUserId == _user.UserId, ct)
            ?? throw new NotFoundException(nameof(CallRecord), callId);
        var lead = await _db.Leads.AsNoTracking().FirstAsync(l => l.Id == call.LeadId, ct);
        return (call, lead);
    }

    private void EnsureAgent()
    {
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
    }

    private static ActiveCallDto ToDto(CallRecord c, Lead l, CallState s) => new(
        c.Id, l.Id, $"{l.FirstName} {l.LastName}".Trim(), l.PhoneNumber,
        c.Direction, c.Status, c.InitiatedAt, c.AnsweredAt, c.EndedAt,
        s.IsHeld, s.IsMuted, c.RecordingUrl);

    private class CallState
    {
        public bool IsHeld { get; set; }
        public bool IsMuted { get; set; }
    }
}
