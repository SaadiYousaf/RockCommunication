using CRM.Application.Common.Assignment;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Commands;

public record BulkLeadActionResult(int Updated, int Skipped, IReadOnlyList<string> Errors);

public record BulkAssignLeadsCommand(IReadOnlyList<Guid> LeadIds, Guid AssigneeUserId)
    : IRequest<BulkLeadActionResult>;

public record BulkSetStageCommand(IReadOnlyList<Guid> LeadIds, WorkflowStage ToStage, LeadDisposition Disposition, string? Notes)
    : IRequest<BulkLeadActionResult>;

public record BulkEnrollCadenceCommand(IReadOnlyList<Guid> LeadIds, Guid CadenceId)
    : IRequest<BulkLeadActionResult>;

public class BulkAssignLeadsValidator : AbstractValidator<BulkAssignLeadsCommand>
{
    public BulkAssignLeadsValidator()
    {
        RuleFor(x => x.LeadIds).NotEmpty().Must(x => x.Count <= 500).WithMessage("Max 500 leads per bulk action.");
        RuleFor(x => x.AssigneeUserId).NotEmpty();
    }
}

public class BulkLeadHandler :
    IRequestHandler<BulkAssignLeadsCommand, BulkLeadActionResult>,
    IRequestHandler<BulkSetStageCommand, BulkLeadActionResult>,
    IRequestHandler<BulkEnrollCadenceCommand, BulkLeadActionResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public BulkLeadHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<BulkLeadActionResult> Handle(BulkAssignLeadsCommand request, CancellationToken ct)
    {
        EnsureManager();
        var leads = await _db.Leads
            .Where(l => request.LeadIds.Contains(l.Id) && l.AgencyId == _user.AgencyId)
            .ToListAsync(ct);

        var updated = 0;
        foreach (var lead in leads)
        {
            lead.AssignedUserId = request.AssigneeUserId;
            lead.UpdatedAt = DateTime.UtcNow;
            updated++;
        }
        await _db.SaveChangesAsync(ct);
        return new BulkLeadActionResult(updated, request.LeadIds.Count - updated, Array.Empty<string>());
    }

    public async Task<BulkLeadActionResult> Handle(BulkSetStageCommand request, CancellationToken ct)
    {
        EnsureManager();
        var leads = await _db.Leads
            .Where(l => request.LeadIds.Contains(l.Id) && l.AgencyId == _user.AgencyId)
            .ToListAsync(ct);

        var updated = 0;
        var errors = new List<string>();
        foreach (var lead in leads)
        {
            if (!CanTransition(lead.Stage, request.ToStage))
            {
                errors.Add($"{lead.FirstName} {lead.LastName}: cannot {lead.Stage} → {request.ToStage}");
                continue;
            }
            _db.LeadActivities.Add(new Domain.Entities.LeadActivity
            {
                AgencyId = lead.AgencyId,
                LeadId = lead.Id,
                UserId = _user.UserId!.Value,
                FromStage = lead.Stage,
                ToStage = request.ToStage,
                Disposition = request.Disposition,
                Notes = request.Notes
            });
            lead.Stage = request.ToStage;
            lead.Disposition = request.Disposition;
            lead.UpdatedAt = DateTime.UtcNow;
            updated++;
        }
        await _db.SaveChangesAsync(ct);
        return new BulkLeadActionResult(updated, request.LeadIds.Count - updated, errors);
    }

    public async Task<BulkLeadActionResult> Handle(BulkEnrollCadenceCommand request, CancellationToken ct)
    {
        EnsureManager();
        var cadence = await _db.Cadences.Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == request.CadenceId && c.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException("Cadence", request.CadenceId);
        if (!cadence.IsActive) throw new ConflictException("Cadence not active.");

        var firstStepDelay = cadence.Steps.OrderBy(s => s.Order).Select(s => s.DelayMinutes).FirstOrDefault();
        var existing = await _db.CadenceEnrollments
            .Where(e => e.CadenceId == cadence.Id && request.LeadIds.Contains(e.LeadId))
            .Select(e => e.LeadId).ToListAsync(ct);

        var updated = 0;
        foreach (var lid in request.LeadIds.Except(existing))
        {
            _db.CadenceEnrollments.Add(new Domain.Entities.CadenceEnrollment
            {
                AgencyId = cadence.AgencyId,
                CadenceId = cadence.Id,
                LeadId = lid,
                CurrentStepOrder = 0,
                NextRunAt = DateTime.UtcNow.AddMinutes(firstStepDelay),
                Status = "Active"
            });
            updated++;
        }
        await _db.SaveChangesAsync(ct);
        return new BulkLeadActionResult(updated, request.LeadIds.Count - updated, Array.Empty<string>());
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null || _user.UserId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager") && !_user.Roles.Contains("TeamLead"))
            throw new ForbiddenAccessException();
    }

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

    private static bool CanTransition(WorkflowStage from, WorkflowStage to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);
}
