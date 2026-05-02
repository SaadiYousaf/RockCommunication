using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.RealTime;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

/// <summary>
/// ACD endpoint — when a call comes into the agency's number, the dialer/PBX hits this endpoint
/// to ask the CRM "who should this go to?". The CRM picks the best available agent (skill + idle-time)
/// and writes a QueuedCall record so the wait time can be measured.
/// </summary>
public record RouteInboundCallCommand(
    Guid AgencyId, string Provider, string ProviderCallId,
    string FromPhone, string? DialedNumber) : IRequest<RouteInboundCallResult>;

public record RouteInboundCallResult(Guid? AgentUserId, Guid? QueueId, int? PositionInQueue, string Decision, string? VoicemailAssetUrl);

public class RouteInboundCallValidator : AbstractValidator<RouteInboundCallCommand>
{
    public RouteInboundCallValidator()
    {
        RuleFor(x => x.AgencyId).NotEmpty();
        RuleFor(x => x.Provider).NotEmpty();
        RuleFor(x => x.ProviderCallId).NotEmpty();
        RuleFor(x => x.FromPhone).NotEmpty();
    }
}

public class RouteInboundCallHandler : IRequestHandler<RouteInboundCallCommand, RouteInboundCallResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IAgentNotifier _notifier;
    public RouteInboundCallHandler(IApplicationDbContext db, IAgentNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<RouteInboundCallResult> Handle(RouteInboundCallCommand request, CancellationToken ct)
    {
        var queue = await _db.InboundQueues
            .Where(q => q.AgencyId == request.AgencyId && q.IsActive
                && (request.DialedNumber == null || q.PhoneNumber == null || q.PhoneNumber == request.DialedNumber))
            .OrderByDescending(q => q.PhoneNumber == request.DialedNumber)
            .FirstOrDefaultAsync(ct);

        if (queue is null)
            return new RouteInboundCallResult(null, null, null, "no-queue", null);

        var queuedCall = new QueuedCall
        {
            AgencyId = request.AgencyId,
            InboundQueueId = queue.Id,
            FromPhone = request.FromPhone,
            Provider = request.Provider,
            ProviderCallId = request.ProviderCallId,
            Status = "Waiting"
        };
        _db.QueuedCalls.Add(queuedCall);
        await _db.SaveChangesAsync(ct);

        var pickedAgent = await PickBestAgentAsync(queue, ct);

        if (pickedAgent is null)
        {
            var ahead = await _db.QueuedCalls.AsNoTracking()
                .CountAsync(q => q.InboundQueueId == queue.Id && q.Status == "Waiting" && q.EnteredAt < queuedCall.EnteredAt, ct);
            string? voicemailUrl = null;
            if (queue.VoicemailAssetId is { } vmId)
            {
                voicemailUrl = await _db.VoicemailAssets.AsNoTracking()
                    .Where(v => v.Id == vmId).Select(v => v.Url).FirstOrDefaultAsync(ct);
            }
            return new RouteInboundCallResult(null, queue.Id, ahead + 1, "queued-no-agent", voicemailUrl);
        }

        queuedCall.AnsweredByUserId = pickedAgent;
        queuedCall.AnsweredAt = DateTime.UtcNow;
        queuedCall.Status = "Answered";

        var leadId = await ResolveLeadIdAsync(request.AgencyId, request.FromPhone, ct);
        var record = new CallRecord
        {
            AgencyId = request.AgencyId,
            LeadId = leadId,
            AgentUserId = pickedAgent.Value,
            Provider = request.Provider,
            ProviderCallId = request.ProviderCallId,
            Status = "ringing",
            Direction = "Inbound",
            InitiatedAt = queuedCall.EnteredAt,
            AnsweredAt = queuedCall.AnsweredAt
        };
        _db.CallRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        await _notifier.PushAsync(pickedAgent.Value, AgentEvents.IncomingCall, new
        {
            callId = record.Id,
            leadId,
            phone = request.FromPhone,
            provider = request.Provider,
            providerCallId = request.ProviderCallId,
            queueId = queue.Id,
            queueName = queue.Name
        }, ct);

        return new RouteInboundCallResult(pickedAgent, queue.Id, 0, "answered", null);
    }

    private async Task<Guid?> PickBestAgentAsync(InboundQueue queue, CancellationToken ct)
    {
        var openSessions = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.AgencyId == queue.AgencyId && s.ClockOutAt == null)
            .Select(s => s.Id).ToListAsync(ct);

        var availableNow = await _db.AgentStatusLogs.AsNoTracking()
            .Where(l => openSessions.Contains(l.SessionId!.Value)
                && l.UntilAt == null && l.Status == AgentStatus.Available)
            .Select(l => new { l.UserId, l.FromAt })
            .ToListAsync(ct);

        if (availableNow.Count == 0) return null;
        var availableIds = availableNow.Select(a => a.UserId).ToList();

        if (!string.IsNullOrEmpty(queue.RequiredSkillCode))
        {
            var skill = await _db.Skills.AsNoTracking()
                .FirstOrDefaultAsync(s => s.AgencyId == queue.AgencyId && s.Code == queue.RequiredSkillCode, ct);
            if (skill is not null)
            {
                var skilled = await _db.AgentSkills.AsNoTracking()
                    .Where(a => a.SkillId == skill.Id && availableIds.Contains(a.UserId))
                    .Select(a => a.UserId).ToListAsync(ct);
                if (skilled.Count > 0) availableIds = skilled;
            }
        }

        return queue.Strategy.ToLowerInvariant() switch
        {
            "longest-idle" => availableNow.Where(a => availableIds.Contains(a.UserId))
                .OrderBy(a => a.FromAt).Select(a => (Guid?)a.UserId).FirstOrDefault(),
            "round-robin" => availableIds.OrderBy(_ => Guid.NewGuid()).Select(uid => (Guid?)uid).FirstOrDefault(),
            _ => availableIds.FirstOrDefault()
        };
    }

    private async Task<Guid> ResolveLeadIdAsync(Guid agencyId, string fromPhone, CancellationToken ct)
    {
        var existing = await _db.Leads.AsNoTracking()
            .Where(l => l.AgencyId == agencyId && l.PhoneNumber == fromPhone)
            .Select(l => l.Id).FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty) return existing;
        var placeholder = new Lead
        {
            AgencyId = agencyId,
            FirstName = "Inbound",
            LastName = "Caller",
            PhoneNumber = fromPhone,
            Stage = WorkflowStage.New,
            Source = "InboundCall"
        };
        _db.Leads.Add(placeholder);
        await _db.SaveChangesAsync(ct);
        return placeholder.Id;
    }
}
