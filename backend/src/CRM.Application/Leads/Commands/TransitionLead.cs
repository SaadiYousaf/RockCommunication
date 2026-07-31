using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Workflow;
using CRM.Application.Intake;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Constants;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using DomainRoles = CRM.Domain.Enums.Roles;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Commands;

public record TransitionLeadCommand(Guid LeadId, TransitionLeadDto Input) : IRequest<LeadDto>;

public class TransitionLeadValidator : AbstractValidator<TransitionLeadCommand>
{
    public TransitionLeadValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Input.ToStage).IsInEnum();
    }
}

public class TransitionLeadHandler : IRequestHandler<TransitionLeadCommand, LeadDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIntakeNotifier _notifier;
    private readonly IWorkflowEngine _workflow;

    public TransitionLeadHandler(IApplicationDbContext db, ICurrentUser user, IIntakeNotifier notifier, IWorkflowEngine workflow)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _notifier = Guard.AgainstNull(notifier);
        _workflow = Guard.AgainstNull(workflow);
    }

    public async Task<LeadDto> Handle(TransitionLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null)
            throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var to = request.Input.ToStage;
        if (!LeadStagePolicy.CanTransition(lead.Stage, to))
            throw new ConflictException($"Cannot transition lead from {lead.Stage} to {to}.");

        var activity = new LeadActivity
        {
            AgencyId = lead.AgencyId, CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            UserId = _user.UserId.Value,
            FromStage = lead.Stage,
            ToStage = to,
            Disposition = request.Input.Disposition,
            Notes = request.Input.Notes
        };
        _db.LeadActivities.Add(activity);

        lead.Stage = to;
        lead.Disposition = request.Input.Disposition;
        lead.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Fire the stage-change workflow event AFTER the save — best-effort, so a rule failure can
        // never roll back or fail the transition. Lets rules react to a lead moving stages
        // (auto follow-up, notify the next queue, SMS on move-to-Followup, webhook out, …).
        try
        {
            await _workflow.PublishAsync(new LeadStageChangedEvent
            {
                AgencyId = lead.AgencyId,
                LeadId = lead.Id,
                FromStage = activity.FromStage.ToString(),
                ToStage = to.ToString(),
                AgentUserId = _user.UserId.Value,
            }, ct);
        }
        catch { /* workflow publish is advisory; never break the transition */ }

        // Moving a lead into a stage hands it to another role's work queue — notify that role
        // (the specific intake handlers do this on their own paths; this covers the generic one).
        if (LeadStagePolicy.QueueOwnerRole(to) is { } ownerRole)
        {
            var route = ownerRole == DomainRoles.Verifier ? AppConstants.QueueRoutes.VerifyQueue
                      : ownerRole == DomainRoles.Closer ? AppConstants.QueueRoutes.CloseQueue
                      : AppConstants.QueueRoutes.ValidateQueue;
            await _notifier.NotifyQueueAsync(lead, ownerRole, "New lead in your queue",
                $"{lead.FirstName} {lead.LastName} — {lead.PhoneNumber}", route, ct);
        }

        return new LeadDto(lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber,
            lead.Email, lead.State, lead.Stage, lead.Disposition,
            lead.AssignedUserId, lead.TeamId, lead.JornayaVerified, lead.CreatedAt);
    }
}
