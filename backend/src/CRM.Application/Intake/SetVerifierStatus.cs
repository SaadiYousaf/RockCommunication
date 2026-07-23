using CRM.Application.Common.Compliance;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Intake;

public record SetVerifierStatusCommand(
    Guid LeadId,
    VerifierStatus Status,
    string? Notes,
    DateTime? CallbackAt) : IRequest<VerifierStatusResult>;

public record VerifierStatusResult(Guid LeadId, VerifierStatus Status, WorkflowStage Stage);

public class SetVerifierStatusValidator : AbstractValidator<SetVerifierStatusCommand>
{
    public SetVerifierStatusValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Status).NotEqual(VerifierStatus.None).WithMessage("Select a verifier status.");
    }
}

public class SetVerifierStatusHandler : IRequestHandler<SetVerifierStatusCommand, VerifierStatusResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPhoneNormalizer _phone;
    private readonly IIntakeNotifier _notifier;

    public SetVerifierStatusHandler(IApplicationDbContext db, ICurrentUser user, IPhoneNormalizer phone, IIntakeNotifier notifier)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _phone = Guard.AgainstNull(phone);
        _notifier = Guard.AgainstNull(notifier);
    }

    public async Task<VerifierStatusResult> Handle(SetVerifierStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        if (lead.Stage != WorkflowStage.Fronted)
            throw new ConflictException("Only fronted leads awaiting verification can be updated by a verifier.");

        var from = lead.Stage;
        lead.VerifierStatus = request.Status;
        lead.UpdatedAt = DateTime.UtcNow;

        // Map the verifier's outcome onto the pipeline.
        switch (request.Status)
        {
            case VerifierStatus.Verified:
                lead.Stage = WorkflowStage.Verified;              // → Closer queue
                lead.Disposition = LeadDisposition.Interested;
                break;
            case VerifierStatus.NotInterested:
                lead.Stage = WorkflowStage.Lost;
                lead.Disposition = LeadDisposition.NotInterested;
                break;
            case VerifierStatus.Dnc:
                lead.Stage = WorkflowStage.Lost;
                lead.Disposition = LeadDisposition.DoNotCall;
                await AddDncAsync(lead, ct);
                break;
            case VerifierStatus.CallBack:
                lead.Disposition = LeadDisposition.CallBack;      // stays fronted for a retry
                _db.ScheduledCallbacks.Add(new ScheduledCallback
                {
                    AgencyId = lead.AgencyId, CallCenterId = lead.CallCenterId,
                    LeadId = lead.Id,
                    AssignedUserId = _user.UserId.Value,
                    ScheduledFor = request.CallbackAt ?? DateTime.UtcNow.AddHours(1),
                    Reason = request.Notes ?? "Verifier requested callback"
                });
                break;
            case VerifierStatus.Busy:
            case VerifierStatus.DeadAir:
                lead.Disposition = LeadDisposition.NoAnswer;       // stays fronted for a retry
                break;
        }

        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = lead.AgencyId, CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            UserId = _user.UserId.Value,
            FromStage = from,
            ToStage = lead.Stage,
            Disposition = lead.Disposition,
            Notes = $"Verifier status: {request.Status}. {request.Notes}".Trim()
        });

        await _db.SaveChangesAsync(ct);

        // Verified → the lead is now in the Closer queue; notify the closers.
        if (request.Status == VerifierStatus.Verified)
            await _notifier.NotifyQueueAsync(lead, CRM.Domain.Enums.Roles.Closer, "New lead to close",
                $"{lead.FirstName} {lead.LastName} — {lead.PhoneNumber}", "/close-queue", ct);

        return new VerifierStatusResult(lead.Id, lead.VerifierStatus, lead.Stage);
    }

    private async Task AddDncAsync(Lead lead, CancellationToken ct)
    {
        var norm = _phone.Normalize(lead.PhoneNumber);
        if (string.IsNullOrEmpty(norm)) return;
        var exists = await _db.DncEntries.AnyAsync(d => d.AgencyId == lead.AgencyId && d.PhoneNormalized == norm, ct);
        if (exists) return;
        _db.DncEntries.Add(new DncEntry
        {
            AgencyId = lead.AgencyId,
            PhoneNormalized = norm,
            Reason = "Marked DNC by verifier",
            Source = "Verifier"
        });
    }
}
