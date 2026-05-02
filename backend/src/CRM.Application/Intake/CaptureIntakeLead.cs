using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Application.Common.Workflow;
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

public record CaptureIntakeLeadCommand(IntakeLeadDto Input) : IRequest<IntakeLeadResult>;

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

    public CaptureIntakeLeadHandler(IApplicationDbContext db, ICurrentUser user, ILeadScorer scorer, IWorkflowEngine workflow)
    {
        _db = db;
        _user = user;
        _scorer = scorer;
        _workflow = workflow;
    }

    public async Task<IntakeLeadResult> Handle(CaptureIntakeLeadCommand request, CancellationToken ct)
    {
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException("No agency context.");

        var d = request.Input;
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
            Source = "Fronter Intake",
            // Fronter has captured the lead → it enters the Verifier queue immediately.
            Stage = WorkflowStage.Fronted,
            Disposition = LeadDisposition.None,
            VerifierStatus = VerifierStatus.None,
            AssignedUserId = _user.UserId
        };
        _db.Leads.Add(lead);
        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = lead.AgencyId,
            LeadId = lead.Id,
            UserId = _user.UserId.Value,
            FromStage = WorkflowStage.New,
            ToStage = WorkflowStage.Fronted,
            Disposition = LeadDisposition.None,
            Notes = "Lead captured via Fronter intake form."
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

        return new IntakeLeadResult(lead.Id, lead.FirstName, lead.LastName, lead.Stage);
    }
}
