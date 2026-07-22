using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record TransferCallCommand(Guid CallRecordId, Guid TargetAgentUserId, string TransferType, string? Note)
    : IRequest<Unit>;

public class TransferCallValidator : AbstractValidator<TransferCallCommand>
{
    public TransferCallValidator()
    {
        RuleFor(x => x.CallRecordId).NotEmpty();
        RuleFor(x => x.TargetAgentUserId).NotEmpty();
        RuleFor(x => x.TransferType).Must(t => t is "warm" or "cold" or "conference")
            .WithMessage("transferType must be warm, cold or conference.");
    }
}

public class TransferCallHandler : IRequestHandler<TransferCallCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDialerProvider _dialer;
    private readonly INotificationDispatcher _notify;

    public TransferCallHandler(IApplicationDbContext db, ICurrentUser user, IDialerProvider dialer, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _dialer = Guard.AgainstNull(dialer); _notify = Guard.AgainstNull(notify);
    }

    public async Task<Unit> Handle(TransferCallCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var call = await _db.CallRecords.FirstOrDefaultAsync(
            c => c.Id == request.CallRecordId && c.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException("Call", request.CallRecordId);

        if (call.AgentUserId != _user.UserId)
            throw new ForbiddenAccessException("Only the agent on the call can transfer it.");

        await _dialer.DialAsync(_user.UserId.Value, $"transfer:{request.TransferType}:{request.TargetAgentUserId}", call.LeadId, ct);

        if (request.TransferType == "cold")
        {
            call.AgentUserId = request.TargetAgentUserId;
        }
        call.Notes = $"{call.Notes}\n[transfer-{request.TransferType}] {request.Note}".Trim();
        await _db.SaveChangesAsync(ct);

        await _notify.DispatchAsync(new NotificationPayload(
            call.AgencyId, request.TargetAgentUserId,
            $"Incoming {request.TransferType} transfer",
            $"From {_user.UserName ?? "agent"} for lead {call.LeadId}. {request.Note}"),
            new[] { NotificationChannelType.InApp }, ct);

        return Unit.Value;
    }
}

// Dial-mode preferences live on Campaign — the agent panel reads it; this query exposes effective mode for current agent.
public record GetEffectiveDialModeQuery(Guid? CampaignId) : IRequest<string>;

public class GetEffectiveDialModeHandler : IRequestHandler<GetEffectiveDialModeQuery, string>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public GetEffectiveDialModeHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<string> Handle(GetEffectiveDialModeQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (request.CampaignId is null) return "Manual";
        var campaign = await _db.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId && c.AgencyId == _user.AgencyId, ct);
        return campaign?.DialMode ?? "Manual";
    }
}
