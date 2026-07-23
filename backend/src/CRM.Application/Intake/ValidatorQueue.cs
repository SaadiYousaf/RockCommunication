using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Intake;

/// <summary>A submitted sale as shown in the Validator queue.</summary>
public record ValidatorQueueItem(
    Guid SaleId,
    Guid LeadId,
    string LeadName,
    string LeadPhone,
    string? State,
    string Carrier,
    string? PolicyNumber,
    decimal MonthlyPremium,
    Guid CloserUserId,
    string? CloserName,
    ValidatorStatus Status,
    string? CarrierApproved,
    decimal? CoverageApproved,
    decimal? PremiumApproved,
    string? PlanApproved,
    string? DeclineReason,
    Guid? ValidatorUserId,
    string? ValidatorName,
    DateTime SoldAt,
    DateTime? ValidatedAt);

/// <summary>
/// Every sale in the agency, newest first — a sale lands here as <see cref="ValidatorStatus.Completed"/>
/// the moment the closer submits "Complete and Sold".
/// </summary>
public record ValidatorQueueQuery(int Take = 200) : IRequest<IReadOnlyList<ValidatorQueueItem>>;

/// <summary>Validator sets the status (and approval/decline details) on a submitted sale.</summary>
public record SetValidatorStatusCommand(
    Guid SaleId,
    ValidatorStatus Status,
    string? CarrierApproved,
    decimal? CoverageApproved,
    decimal? PremiumApproved,
    string? PlanApproved,
    string? DeclineReason) : IRequest<ValidatorStatusResult>;

public record ValidatorStatusResult(Guid SaleId, ValidatorStatus Status, WorkflowStage LeadStage);

public class SetValidatorStatusValidator : AbstractValidator<SetValidatorStatusCommand>
{
    public SetValidatorStatusValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty();

        // "Approved" requires the approved carrier / coverage / premium / plan.
        When(x => x.Status == ValidatorStatus.Approved, () =>
        {
            RuleFor(x => x.CarrierApproved).NotEmpty().WithMessage("Carrier Approved is required.");
            RuleFor(x => x.CoverageApproved).NotNull().GreaterThan(0).WithMessage("Coverage Approved is required.");
            RuleFor(x => x.PremiumApproved).NotNull().GreaterThan(0).WithMessage("Premium Approved is required.");
            RuleFor(x => x.PlanApproved).NotEmpty().WithMessage("Plan Approved is required.");
        });

        // "Decline" requires a reason.
        When(x => x.Status == ValidatorStatus.Decline, () =>
            RuleFor(x => x.DeclineReason).NotEmpty().WithMessage("A decline reason is required."));

        // "Error in application information" requires the specific sub-reason.
        When(x => x.Status == ValidatorStatus.ErrorInApplicationInformation, () =>
            RuleFor(x => x.DeclineReason).NotEmpty().WithMessage("Select the application error (banking/payor or identity)."));
    }
}

public class ValidatorQueueHandler :
    IRequestHandler<ValidatorQueueQuery, IReadOnlyList<ValidatorQueueItem>>,
    IRequestHandler<SetValidatorStatusCommand, ValidatorStatusResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public ValidatorQueueHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
    }

    public async Task<IReadOnlyList<ValidatorQueueItem>> Handle(ValidatorQueueQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var take = Math.Clamp(request.Take, 1, 500);
        var raw = await _db.Sales.AsNoTracking()
            .Where(s => s.AgencyId == _user.AgencyId)
            .OrderByDescending(s => s.SoldAt)
            .Take(take)
            .Join(_db.Leads.AsNoTracking(),
                s => s.LeadId, l => l.Id,
                (s, l) => new
                {
                    s.Id, s.LeadId, l.FirstName, l.LastName, l.PhoneNumber, l.State,
                    s.Carrier, s.PolicyNumber, s.MonthlyPremium, s.CloserUserId,
                    s.ValidatorStatus, s.CarrierApproved, s.CoverageApproved, s.PremiumApproved,
                    s.PlanApproved, s.DeclineReason, s.ValidatorUserId, s.SoldAt, s.ValidatedAt
                })
            .ToListAsync(ct);

        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);

        return raw.Select(r => new ValidatorQueueItem(
            r.Id, r.LeadId, $"{r.FirstName} {r.LastName}".Trim(), r.PhoneNumber, r.State,
            r.Carrier, r.PolicyNumber, r.MonthlyPremium,
            r.CloserUserId, byId.TryGetValue(r.CloserUserId, out var c) ? c.UserName : null,
            r.ValidatorStatus, r.CarrierApproved, r.CoverageApproved, r.PremiumApproved,
            r.PlanApproved, r.DeclineReason,
            r.ValidatorUserId, r.ValidatorUserId is { } vid && byId.TryGetValue(vid, out var v) ? v.UserName : null,
            r.SoldAt, r.ValidatedAt)).ToList();
    }

    public async Task<ValidatorStatusResult> Handle(SetValidatorStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var sale = await _db.Sales.FirstOrDefaultAsync(
            s => s.Id == request.SaleId && s.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        sale.ValidatorStatus = request.Status;
        sale.ValidatorUserId = _user.UserId;

        // Approval details are only meaningful for "Approved"; a decline reason only for "Decline".
        if (request.Status == ValidatorStatus.Approved)
        {
            sale.CarrierApproved = request.CarrierApproved?.Trim();
            sale.CoverageApproved = request.CoverageApproved;
            sale.PremiumApproved = request.PremiumApproved;
            sale.PlanApproved = request.PlanApproved?.Trim();
            sale.DeclineReason = null;
        }
        else if (request.Status == ValidatorStatus.Decline ||
                 request.Status == ValidatorStatus.ErrorInApplicationInformation)
        {
            // Both carry a free-text reason in DeclineReason (decline reason, or the
            // application-error sub-reason). The lead stays in the queue (default case below)
            // for an error so the closer can correct and resubmit.
            sale.DeclineReason = request.DeclineReason?.Trim();
        }

        // Reflect the outcome on the lead's stage so the rest of the pipeline stays consistent.
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == sale.LeadId && l.AgencyId == sale.AgencyId, ct);
        if (lead is not null)
        {
            var from = lead.Stage;
            switch (request.Status)
            {
                case ValidatorStatus.Approved:
                    lead.Stage = WorkflowStage.Validated;
                    sale.ValidatedAt ??= DateTime.UtcNow;
                    break;
                case ValidatorStatus.ActivePaid:
                    lead.Stage = WorkflowStage.Funded;
                    sale.ValidatedAt ??= DateTime.UtcNow;
                    sale.FundedAt ??= DateTime.UtcNow;
                    break;
                case ValidatorStatus.Decline:
                case ValidatorStatus.ClientCancelled:
                    lead.Stage = WorkflowStage.Lost;
                    break;
                default:
                    lead.Stage = WorkflowStage.Closed; // Completed / NoUpdate / BadBank / NSF stay in the queue
                    break;
            }
            if (lead.Stage != from)
            {
                lead.UpdatedAt = DateTime.UtcNow;
                _db.LeadActivities.Add(new LeadActivity
                {
                    AgencyId = lead.AgencyId, CallCenterId = lead.CallCenterId,
                    LeadId = lead.Id,
                    UserId = _user.UserId.Value,
                    FromStage = from,
                    ToStage = lead.Stage,
                    Disposition = lead.Disposition,
                    Notes = $"Validator status: {request.Status}."
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return new ValidatorStatusResult(sale.Id, sale.ValidatorStatus, lead?.Stage ?? WorkflowStage.Closed);
    }
}
