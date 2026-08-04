using CRM.Application.Common.Commission;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;
using ValidationException = CRM.Application.Common.Exceptions.ValidationException;

namespace CRM.Application.Intake;

/// <summary>A submitted sale as shown in the Validator (Submission Agent) queue.</summary>
public record ValidatorQueueItem(
    Guid SaleId,
    Guid LeadId,
    Guid AgencyId,
    string AgencyName,
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
    Guid? LicenseAgentUserId,
    string? LicenseAgentName,
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
    string? DeclineReason,
    /// <summary>Agency-level License Agent to assign on approval (optional). Must belong to the sale's agency.</summary>
    Guid? LicenseAgentUserId = null) : IRequest<ValidatorStatusResult>;

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
    private readonly ICommissionEngine _commission;
    private readonly INotificationDispatcher _notify;

    public ValidatorQueueHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity,
        ICommissionEngine commission, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _commission = Guard.AgainstNull(commission);
        _notify = Guard.AgainstNull(notify);
    }

    public async Task<IReadOnlyList<ValidatorQueueItem>> Handle(ValidatorQueueQuery request, CancellationToken ct)
    {
        // A "central" (SMH-level) Submission Agent validates sales across ALL agencies; an
        // agency-scoped validator only sees their own. Central reads bypass the tenant query
        // filter, so we must re-add the soft-delete predicate by hand (IgnoreQueryFilters drops it too).
        var central = DomainRoles.IsCentralSubmissionAgent(_user.AgencyId, _user.Roles);
        if (!central && _user.AgencyId is null) throw new ForbiddenAccessException();

        var take = Math.Clamp(request.Take, 1, 500);

        var salesQ = _db.Sales.AsNoTracking();
        salesQ = central
            ? salesQ.IgnoreQueryFilters().Where(s => !s.IsDeleted)
            : salesQ.Where(s => s.AgencyId == _user.AgencyId);

        var sales = await salesQ.OrderByDescending(s => s.SoldAt).Take(take).ToListAsync(ct);

        var leadIds = sales.Select(s => s.LeadId).Distinct().ToList();
        var leadsQ = _db.Leads.AsNoTracking();
        if (central) leadsQ = leadsQ.IgnoreQueryFilters().Where(l => !l.IsDeleted);
        var leads = await leadsQ.Where(l => leadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.State })
            .ToListAsync(ct);
        var leadById = leads.ToDictionary(l => l.Id);

        var agencyIds = sales.Select(s => s.AgencyId).Distinct().ToList();
        var agencyById = (await _db.Agencies.AsNoTracking().IgnoreQueryFilters()
                .Where(a => agencyIds.Contains(a.Id) && !a.IsDeleted)
                .Select(a => new { a.Id, a.Name }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.Name);

        // Cross-agency queues need names from every agency; scoped queues only their own.
        var users = await _identity.ListUsersAsync(central ? null : _user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);
        string? Name(Guid? id) => id is { } g && byId.TryGetValue(g, out var u) ? u.UserName : null;

        return sales.Select(s =>
        {
            leadById.TryGetValue(s.LeadId, out var l);
            return new ValidatorQueueItem(
                s.Id, s.LeadId,
                s.AgencyId, agencyById.TryGetValue(s.AgencyId, out var an) ? an : "",
                l is null ? "" : $"{l.FirstName} {l.LastName}".Trim(),
                l?.PhoneNumber ?? "", l?.State,
                s.Carrier, s.PolicyNumber, s.MonthlyPremium,
                s.CloserUserId, Name(s.CloserUserId),
                s.ValidatorStatus, s.CarrierApproved, s.CoverageApproved, s.PremiumApproved,
                s.PlanApproved, s.DeclineReason,
                s.ValidatorUserId, Name(s.ValidatorUserId),
                s.LicenseAgentUserId, Name(s.LicenseAgentUserId),
                s.SoldAt, s.ValidatedAt);
        }).ToList();
    }

    public async Task<ValidatorStatusResult> Handle(SetValidatorStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var central = DomainRoles.IsCentralSubmissionAgent(_user.AgencyId, _user.Roles);
        if (!central && _user.AgencyId is null) throw new ForbiddenAccessException();

        var sale = (central
            ? await _db.Sales.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == request.SaleId && !s.IsDeleted, ct)
            : await _db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId && s.AgencyId == _user.AgencyId, ct))
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

            // Assign the License Agent (if one was chosen) and pay their commission.
            if (request.LicenseAgentUserId is { } licenseAgentId)
                await AssignLicenseAgentAsync(sale, licenseAgentId, ct);
        }
        else if (request.Status == ValidatorStatus.Decline ||
                 request.Status == ValidatorStatus.ErrorInApplicationInformation)
        {
            // Both carry a free-text reason in DeclineReason (decline reason, or the
            // application-error sub-reason). The lead stays in the queue (default case below)
            // for an error so the closer can correct and resubmit.
            sale.DeclineReason = request.DeclineReason?.Trim();
        }

        // A negative terminal outcome (declined / client-cancelled → lead goes Lost) voids the
        // sale's still-unpaid commission lines so payroll never pays out for a lost sale.
        // Already-paid entries are left untouched (no claw-back). Mirrors ValidateSaleHandler.
        if (request.Status is ValidatorStatus.Decline or ValidatorStatus.ClientCancelled)
        {
            // Central agents act with an empty tenant (AgencyId = Guid.Empty) and are not SuperAdmin,
            // so the global tenant filter would scope this read to Guid.Empty and match nothing —
            // leaving the real agency's unpaid lines to be paid on a lost sale. Bypass the filter and
            // scope explicitly to the sale's agency (re-adding !IsDeleted, which IgnoreQueryFilters drops).
            // Mirrors AssignLicenseAgentAsync; behaviour-preserving for the agency-scoped path.
            var unpaid = await _db.CommissionEntries.IgnoreQueryFilters()
                .Where(c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId && !c.Paid && !c.IsDeleted)
                .ToListAsync(ct);
            _db.CommissionEntries.RemoveRange(unpaid);
        }

        // A POSITIVE terminal outcome means the sale is now validated (ValidatorUserId is set above),
        // so recompute the full participant set to (a) pay the ValidatorBonus that wasn't computable
        // at sale time (ValidatorUserId was null then) and (b) revive closer/jr/team-lead/kicker lines
        // a prior Decline soft-deleted. IgnoreQueryFilters + explicit sale.AgencyId: central agents have
        // Guid.Empty as their tenant, so the filtered read would never match the real-agency rows.
        // Include soft-deleted rows so they can be revived; skip already-paid rows (no double pay).
        // CalculateForSaleAsync never emits the LicenseAgent line, so this can't collide with the
        // AssignLicenseAgentAsync line created above.
        if (request.Status is ValidatorStatus.Approved or ValidatorStatus.ActivePaid)
        {
            var recomputed = await _commission.CalculateForSaleAsync(sale, ct);
            var existingLines = await _db.CommissionEntries.IgnoreQueryFilters()
                .Where(c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId)
                .ToListAsync(ct);
            foreach (var line in recomputed)
            {
                var row = existingLines.FirstOrDefault(c => c.RuleName == line.RuleName);
                if (row is null)
                {
                    _db.CommissionEntries.Add(new CommissionEntry
                    {
                        AgencyId = sale.AgencyId,
                        CallCenterId = sale.CallCenterId,
                        SaleId = sale.Id,
                        AgentUserId = line.AgentId,
                        RuleName = line.RuleName,
                        Amount = line.Amount,
                        Note = line.Note,
                    });
                }
                else if (!row.Paid)
                {
                    row.IsDeleted = false;      // revive a line a prior decline soft-deleted
                    row.AgentUserId = line.AgentId;
                    row.Amount = line.Amount;
                    row.Note = line.Note;
                }
                // else: already paid — leave the historical entry untouched.
            }
        }

        // Reflect the outcome on the lead's stage so the rest of the pipeline stays consistent.
        // Central agents act cross-agency, so the lead lookup must bypass the tenant filter too.
        var lead = central
            ? await _db.Leads.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == sale.LeadId && !l.IsDeleted, ct)
            : await _db.Leads.FirstOrDefaultAsync(l => l.Id == sale.LeadId && l.AgencyId == sale.AgencyId, ct);
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

    /// <summary>
    /// Assigns an agency-level License Agent to an approved sale and records their commission.
    /// The agent MUST belong to the sale's own agency and hold the LicenseAgent role — otherwise
    /// a central agent could mint commission for an arbitrary/foreign user. Idempotent: the
    /// commission line is created once; a later re-assignment updates it only while still unpaid.
    /// </summary>
    private async Task AssignLicenseAgentAsync(Sale sale, Guid licenseAgentId, CancellationToken ct)
    {
        var agencyUsers = await _identity.ListUsersAsync(sale.AgencyId, ct);
        var agent = agencyUsers.FirstOrDefault(u => u.Id == licenseAgentId
            && u.Roles.Contains(DomainRoles.LicenseAgent, StringComparer.OrdinalIgnoreCase));
        if (agent is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["licenseAgentUserId"] = new[] { "The selected agent is not a License Agent in this sale's agency." }
            });

        sale.LicenseAgentUserId = licenseAgentId;

        var line = (await _commission.CalculateForAgentAsync(sale, licenseAgentId, DomainRoles.LicenseAgent, ct))
            .FirstOrDefault();
        if (line is null) return; // e.g. internal sale — no license-agent commission

        // IgnoreQueryFilters + explicit agency: a central Submission Agent has Guid.Empty as their
        // tenant, so the filtered lookup would never match the sale's real-agency entry and every
        // re-approval would insert a DUPLICATE commission. Scope to the sale's agency instead.
        var existing = await _db.CommissionEntries.IgnoreQueryFilters().FirstOrDefaultAsync(
            c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId && c.RuleName == line.RuleName, ct);
        if (existing is null)
        {
            _db.CommissionEntries.Add(new CommissionEntry
            {
                AgencyId = sale.AgencyId,
                CallCenterId = sale.CallCenterId,
                SaleId = sale.Id,
                AgentUserId = licenseAgentId,
                RuleName = line.RuleName,
                Amount = line.Amount,
                Note = line.Note
            });
        }
        else if (!existing.Paid)
        {
            // A prior unassign SOFT-deletes this line (the audit interceptor turns Remove into
            // IsDeleted=true), and the lookup above uses IgnoreQueryFilters so it finds that row.
            // Re-assigning must REVIVE it — otherwise the new agent's commission stays IsDeleted=true,
            // invisible to lists/MyCommissions/payroll, and is never paid.
            existing.IsDeleted = false;
            existing.AgentUserId = licenseAgentId;
            existing.Amount = line.Amount;
            existing.Note = line.Note;
        }
        // else: already paid out — leave the historical entry untouched.

        // Let the License Agent know a sale is now theirs (bell + live push + email). Best-effort:
        // the SaveChangesAsync happens in the caller, so we only queue the notification write here —
        // the InApp channel persists via the same context and is flushed with the caller's save.
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(
                    sale.AgencyId, licenseAgentId,
                    "A sale was assigned to you",
                    $"Sale #{sale.SaleNumber} ({sale.Carrier}, {sale.MonthlyPremium:C}/mo) is now assigned to you for approval.",
                    $"/sales/{sale.Id}"),
                new[] { NotificationChannelType.InApp, NotificationChannelType.Email }, ct);
        }
        catch { /* advisory only — never fail the approval */ }
    }
}
