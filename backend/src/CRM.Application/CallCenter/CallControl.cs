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

/// <summary>Places a test call to a raw phone number (no lead) so telephony can be verified.</summary>
public record TestDialCommand(string PhoneNumber) : IRequest<TestDialResult>;
public record TestDialResult(string CallId, string Status, string Provider, IReadOnlyList<string> Warnings);

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
    IRequestHandler<GetMyActiveCallQuery, ActiveCallDto?>,
    IRequestHandler<TestDialCommand, TestDialResult>
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
            CallCenterId = lead.CallCenterId,
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

    public async Task<TestDialResult> Handle(TestDialCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAgent();
        var phone = (request.PhoneNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(phone)) throw new ConflictException("Enter a phone number to test.");

        // Honor compliance even for test calls so we never dial a DNC number (state unknown for a raw number).
        var compliance = await _compliance.CheckOutboundDialAsync(_user.AgencyId!.Value, phone, null, ct);
        if (!compliance.Allowed)
            throw new ConflictException(compliance.BlockReason ?? "Call blocked by compliance.");

        var dial = await _dialer.DialAsync(_user.UserId!.Value, phone, Guid.Empty, ct);
        return new TestDialResult(dial.CallId, dial.Status, _dialer.Name, compliance.Warnings);
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
        // SMS is subject to the same DNC/TCPA guard as an outbound dial — don't let the text path bypass it.
        var compliance = await _compliance.CheckOutboundDialAsync(lead.AgencyId, lead.PhoneNumber, lead.State, ct);
        if (!compliance.Allowed)
            throw new ConflictException(compliance.BlockReason ?? "Text blocked by compliance (DNC/TCPA).");
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
        // Data-integrity guard: a call can reference a lead that no longer exists (e.g. a dialer
        // webhook with a bogus/soft-deleted leadId). Degrade gracefully to "no active call" rather
        // than 500 the agent's whole panel.
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == call.LeadId, ct);
        if (lead is null) return null;
        return ToDto(call, lead, _state.GetOrAdd(call.Id, _ => new()));
    }

    private async Task<(CallRecord call, Lead lead)> LoadAsync(Guid callId, CancellationToken ct)
    {
        EnsureAgent();
        var call = await _db.CallRecords.FirstOrDefaultAsync(
            c => c.Id == callId && c.AgencyId == _user.AgencyId && c.AgentUserId == _user.UserId, ct)
            ?? throw new NotFoundException(nameof(CallRecord), callId);
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == call.LeadId, ct)
            ?? throw new NotFoundException(nameof(Lead), call.LeadId);
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
