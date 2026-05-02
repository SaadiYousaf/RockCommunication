using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Application.Common.Workflow;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CRM.Application.Leads.Commands;

public record CreateLeadCommand(CreateLeadDto Input) : IRequest<LeadDto>;

public class CreateLeadValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadValidator()
    {
        RuleFor(x => x.Input.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.PhoneNumber).NotEmpty().Matches(@"^[\d\-\+\(\)\s]{7,20}$");
        RuleFor(x => x.Input.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Input.Email));
    }
}

public class CreateLeadHandler : IRequestHandler<CreateLeadCommand, LeadDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILeadScorer _scorer;
    private readonly IWorkflowEngine _workflow;

    public CreateLeadHandler(IApplicationDbContext db, ICurrentUser user, ILeadScorer scorer, IWorkflowEngine workflow)
    {
        _db = db;
        _user = user;
        _scorer = scorer;
        _workflow = workflow;
    }

    public async Task<LeadDto> Handle(CreateLeadCommand request, CancellationToken ct)
    {
        if (_user.AgencyId is null)
            throw new ForbiddenAccessException("No agency context.");

        var d = request.Input;
        var lead = new Lead
        {
            AgencyId = _user.AgencyId.Value,
            FirstName = d.FirstName.Trim(),
            LastName = d.LastName.Trim(),
            PhoneNumber = d.PhoneNumber.Trim(),
            Email = d.Email?.Trim(),
            Address = d.Address,
            City = d.City,
            State = d.State,
            PostalCode = d.PostalCode,
            DateOfBirth = d.DateOfBirth,
            Source = d.Source,
            JornayaLeadId = d.JornayaLeadId,
            Stage = WorkflowStage.New,
            Disposition = LeadDisposition.None
        };

        _db.Leads.Add(lead);
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
            CampaignId = lead.CampaignId,
            Score = lead.Score
        }, ct);

        return new LeadDto(lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber,
            lead.Email, lead.State, lead.Stage, lead.Disposition,
            lead.AssignedUserId, lead.TeamId, lead.JornayaVerified, lead.CreatedAt);
    }
}
