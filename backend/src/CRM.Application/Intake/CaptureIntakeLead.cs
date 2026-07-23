using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Application.Common.Workflow;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CRM.Application.Intake;

/// <summary>Jornaya intake payload captured by the Fronter. Every field is mandatory.</summary>
public record IntakeLeadDto(
    string FirstName,
    string LastName,
    string MaritalStatus,
    DateTime CreatedDate,
    string StreetAddress,
    string City,
    string State,
    string Zipcode,
    string PhoneNumber,
    DateTime BirthDate,
    int AgeYears,
    string Email,
    string? JornayaLeadId);

public record IntakeLeadResult(Guid LeadId, string FirstName, string LastName, WorkflowStage Stage);

/// <summary>
/// Captures a Jornaya intake lead. <paramref name="EntryStage"/> is <see cref="WorkflowStage.Fronted"/>
/// for the Fronter form (lead enters the Verifier queue) or <see cref="WorkflowStage.Verified"/> when a
/// Closer adds a lead directly from the Closer queue (skips verification, ready to close).
/// </summary>
public record CaptureIntakeLeadCommand(IntakeLeadDto Input, WorkflowStage EntryStage = WorkflowStage.Fronted)
    : IRequest<IntakeLeadResult>;

public class CaptureIntakeLeadValidator : AbstractValidator<CaptureIntakeLeadCommand>
{
    public CaptureIntakeLeadValidator()
    {
        RuleFor(x => x.Input.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.MaritalStatus).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Input.CreatedDate).NotEmpty();
        RuleFor(x => x.Input.StreetAddress).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Input.City).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.State).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Input.Zipcode).NotEmpty().MaximumLength(15);
        RuleFor(x => x.Input.PhoneNumber).NotEmpty().Matches(@"^[\d\-\+\(\)\s]{7,20}$");
        RuleFor(x => x.Input.BirthDate).NotEmpty();
        RuleFor(x => x.Input.AgeYears).GreaterThan(0).LessThan(130);
        RuleFor(x => x.Input.Email).NotEmpty().EmailAddress();
    }
}

public class CaptureIntakeLeadHandler : IRequestHandler<CaptureIntakeLeadCommand, IntakeLeadResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILeadScorer _scorer;
    private readonly IWorkflowEngine _workflow;
    private readonly IIntakeNotifier _notifier;

    public CaptureIntakeLeadHandler(IApplicationDbContext db, ICurrentUser user, ILeadScorer scorer, IWorkflowEngine workflow, IIntakeNotifier notifier)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _scorer = Guard.AgainstNull(scorer);
        _workflow = Guard.AgainstNull(workflow);
        _notifier = Guard.AgainstNull(notifier);
    }

    public async Task<IntakeLeadResult> Handle(CaptureIntakeLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException("No agency context.");

        var d = request.Input;
        // A closer-added lead skips verification and is already ready to close.
        var toCloser = request.EntryStage == WorkflowStage.Verified;
        var lead = new Lead
        {
            AgencyId = _user.AgencyId.Value,
            FirstName = d.FirstName.Trim(),
            LastName = d.LastName.Trim(),
            PhoneNumber = d.PhoneNumber.Trim(),
            Email = d.Email.Trim(),
            Address = d.StreetAddress.Trim(),
            City = d.City.Trim(),
            State = d.State.Trim(),
            PostalCode = d.Zipcode.Trim(),
            DateOfBirth = DateTime.SpecifyKind(d.BirthDate, DateTimeKind.Utc),
            MaritalStatus = d.MaritalStatus.Trim(),
            AgeYears = d.AgeYears,
            JornayaLeadId = d.JornayaLeadId,
            ConsentCaptured = true,
            Source = toCloser ? "Closer Intake" : "Fronter Intake",
            Stage = request.EntryStage,
            Disposition = LeadDisposition.None,
            VerifierStatus = toCloser ? VerifierStatus.Verified : VerifierStatus.None,
            AssignedUserId = _user.UserId
        };
        _db.Leads.Add(lead);
        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = lead.AgencyId,
            LeadId = lead.Id,
            UserId = _user.UserId.Value,
            FromStage = WorkflowStage.New,
            ToStage = request.EntryStage,
            Disposition = LeadDisposition.None,
            Notes = toCloser ? "Lead captured via Closer intake form." : "Lead captured via Fronter intake form."
        });
        await _db.SaveChangesAsync(ct);

        var scoring = await _scorer.ScoreAsync(lead, ct);
        lead.Score = scoring.Score;
        await _db.SaveChangesAsync(ct);

        await _workflow.PublishAsync(new LeadCreatedEvent
        {
            AgencyId = lead.AgencyId,
            LeadId = lead.Id,
            Phone = lead.PhoneNumber,
            State = lead.State,
            Source = lead.Source,
            Score = lead.Score
        }, ct);

        // Notify the receiving queue's role that a new lead has landed.
        if (toCloser)
            await _notifier.NotifyQueueAsync(lead, CRM.Domain.Enums.Roles.Closer, "New lead to close",
                $"{lead.FirstName} {lead.LastName} — {lead.PhoneNumber}", "/close-queue", ct);
        else
            await _notifier.NotifyQueueAsync(lead, CRM.Domain.Enums.Roles.Verifier, "New lead to verify",
                $"{lead.FirstName} {lead.LastName} — {lead.PhoneNumber}", "/verify-queue", ct);

        return new IntakeLeadResult(lead.Id, lead.FirstName, lead.LastName, lead.Stage);
    }
}
