using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
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
    private static readonly Dictionary<WorkflowStage, WorkflowStage[]> Allowed = new()
    {
        [WorkflowStage.New]       = new[] { WorkflowStage.Fronted, WorkflowStage.Lost },
        [WorkflowStage.Fronted]   = new[] { WorkflowStage.Verified, WorkflowStage.Lost, WorkflowStage.Followup },
        [WorkflowStage.Verified]  = new[] { WorkflowStage.JrClosed, WorkflowStage.Closed, WorkflowStage.Lost, WorkflowStage.Followup },
        [WorkflowStage.JrClosed]  = new[] { WorkflowStage.Closed, WorkflowStage.Lost, WorkflowStage.Followup },
        [WorkflowStage.Closed]    = new[] { WorkflowStage.Validated, WorkflowStage.Lost },
        [WorkflowStage.Validated] = new[] { WorkflowStage.Funded, WorkflowStage.Lost },
        [WorkflowStage.Funded]    = new[] { WorkflowStage.Followup },
        [WorkflowStage.Followup]  = new[] { WorkflowStage.Fronted, WorkflowStage.Verified, WorkflowStage.Closed, WorkflowStage.Winback, WorkflowStage.Lost },
        [WorkflowStage.Winback]   = new[] { WorkflowStage.Fronted, WorkflowStage.Lost },
        [WorkflowStage.Lost]      = new[] { WorkflowStage.Winback }
    };

    public static bool CanTransition(WorkflowStage from, WorkflowStage to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public TransitionLeadHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
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
        if (!CanTransition(lead.Stage, to))
            throw new ConflictException($"Cannot transition lead from {lead.Stage} to {to}.");

        var activity = new LeadActivity
        {
            AgencyId = lead.AgencyId,
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

        return new LeadDto(lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber,
            lead.Email, lead.State, lead.Stage, lead.Disposition,
            lead.AssignedUserId, lead.TeamId, lead.JornayaVerified, lead.CreatedAt);
    }
}
