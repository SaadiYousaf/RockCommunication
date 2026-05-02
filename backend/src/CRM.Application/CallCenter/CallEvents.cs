using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

/// <summary>
/// Inbound webhook from dialer. Idempotent on (provider, providerCallId, eventType, occurredAt).
/// </summary>
public record DialerEventCommand(
    string Provider,
    string ProviderCallId,
    string EventType, // "answered" | "ended" | "voicemail" | "abandoned" | "ringing"
    DateTime OccurredAt,
    string? RecordingUrl,
    string? Phone,
    Guid? AgencyId,
    Guid? AgentUserId,
    Guid? LeadId
) : IRequest<Unit>;

public class DialerEventValidator : AbstractValidator<DialerEventCommand>
{
    public DialerEventValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();
        RuleFor(x => x.ProviderCallId).NotEmpty();
        RuleFor(x => x.EventType).NotEmpty();
    }
}

public class DialerEventHandler : IRequestHandler<DialerEventCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    public DialerEventHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(DialerEventCommand request, CancellationToken ct)
    {
        var record = await _db.CallRecords
            .FirstOrDefaultAsync(c => c.Provider == request.Provider && c.ProviderCallId == request.ProviderCallId, ct);

        if (record is null)
        {
            if (request.AgencyId is null || request.AgentUserId is null || request.LeadId is null)
                throw new ConflictException("First event for unknown call must include agencyId, agentUserId, leadId.");

            record = new CallRecord
            {
                AgencyId = request.AgencyId.Value,
                LeadId = request.LeadId.Value,
                AgentUserId = request.AgentUserId.Value,
                Provider = request.Provider,
                ProviderCallId = request.ProviderCallId,
                Status = request.EventType,
                Direction = "Inbound",
                InitiatedAt = request.OccurredAt
            };
            _db.CallRecords.Add(record);
        }
        else
        {
            record.Status = request.EventType;
        }

        switch (request.EventType.ToLowerInvariant())
        {
            case "answered":
                record.AnsweredAt ??= request.OccurredAt;
                break;
            case "ended":
            case "abandoned":
            case "voicemail":
                record.EndedAt ??= request.OccurredAt;
                break;
        }

        if (!string.IsNullOrEmpty(request.RecordingUrl))
            record.RecordingUrl = request.RecordingUrl;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
