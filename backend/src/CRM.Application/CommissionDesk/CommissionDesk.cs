using CRM.Application.Common.Commission;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CommissionDesk;

/// <summary>
/// The financial statuses a Commission Agent may set on a submitted sale. Mirrors the enum so the
/// frontend and the validator share one source of truth.
/// </summary>
public static class CommissionDeskStatuses
{
    public static readonly ValidatorStatus[] Settable =
    {
        ValidatorStatus.Approved,
        ValidatorStatus.ActivePaid,
        ValidatorStatus.ChargedBack,
        ValidatorStatus.Nsf,
        ValidatorStatus.Decline,
        ValidatorStatus.ClientCancelled,
        ValidatorStatus.BadBank,
    };

    /// <summary>
    /// Outcomes that hand the policy to the Retention desk. Retention lists sales by exactly these
    /// statuses (see <c>RetentionStatuses.All</c>), so setting one here IS the transfer — there is no
    /// second queue to write to.
    /// </summary>
    public static readonly ValidatorStatus[] MovesToRetention =
    {
        ValidatorStatus.Nsf,
        ValidatorStatus.BadBank,
        ValidatorStatus.ClientCancelled,
        ValidatorStatus.Decline,
    };

    public static bool GoesToRetention(ValidatorStatus s) => MovesToRetention.Contains(s);
}

/// <summary>One money line on a sale (a <see cref="CommissionEntry"/>), editable after a chargeback.</summary>
public record CommissionAmountDto(Guid Id, string RuleName, Guid AgentUserId, string? AgentName,
    decimal Amount, bool Paid, string? Note);

/// <summary>A sale as shown on the cross-agency commission desk.</summary>
public record CommissionSaleDto(
    Guid SaleId, int SaleNumber, Guid LeadId,
    string CustomerName, string PhoneNumber,
    Guid AgencyId, string AgencyName,
    Guid? CallCenterId, string? CallCenterName,
    string Carrier, string? CarrierApproved, string? PolicyNumber,
    decimal MonthlyPremium, decimal? CoverageApproved, decimal? PremiumApproved, string? PlanApproved,
    string Status,
    /// <summary>Sum of this sale's commission entries — negative once charged back.</summary>
    decimal FundedAmount,
    IReadOnlyList<CommissionAmountDto> Amounts,
    // ---- Carrier advancing rule, joined read-only from the global rules ----
    decimal? AdvanceRate, int? AdvancedMonths,
    /// <summary>Expected advance = monthly premium x advanced months x rate. Null with no rule.</summary>
    decimal? ExpectedAdvance,
    DateTime SoldAt, DateTime? ValidatedAt, DateTime? FundedAt, DateTime? ChargedBackAt);

public record CommissionDeskResult(IReadOnlyList<CommissionSaleDto> Items, int Total,
    decimal TotalFunded, decimal TotalPremium);

/// <summary>Cross-agency sales for the commission desk, newest first, with the desk's filters.</summary>
public record ListCommissionSalesQuery(
    Guid? AgencyId = null,
    Guid? CallCenterId = null,
    string? Carrier = null,
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Search = null,
    int Skip = 0,
    int Take = 50) : IRequest<CommissionDeskResult>;

/// <summary>Commission Agent sets a sale's financial status. Chargeback negates its amounts.</summary>
public record SetCommissionStatusCommand(Guid SaleId, ValidatorStatus Status, string? Note)
    : IRequest<CommissionSaleDto>;

/// <summary>Edit one commission amount on a charged-back sale (unpaid lines only).</summary>
public record UpdateCommissionAmountCommand(Guid SaleId, Guid EntryId, decimal Amount, string? Note)
    : IRequest<CommissionSaleDto>;

public class SetCommissionStatusValidator : AbstractValidator<SetCommissionStatusCommand>
{
    public SetCommissionStatusValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty();
        RuleFor(x => x.Status).Must(s => CommissionDeskStatuses.Settable.Contains(s))
            .WithMessage("That status can't be set from the commission desk.");
        // A negative outcome must carry a reason — it moves the policy to Retention.
        When(x => CommissionDeskStatuses.GoesToRetention(x.Status), () =>
            RuleFor(x => x.Note).NotEmpty().WithMessage("A note explaining the reason is required."));
    }
}

public class UpdateCommissionAmountValidator : AbstractValidator<UpdateCommissionAmountCommand>
{
    public UpdateCommissionAmountValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(-1_000_000m).LessThan(1_000_000m);
    }
}

/// <summary>
/// The commission desk: a CROSS-AGENCY view of submitted sales. Reads bypass the tenant query filter
/// (re-adding !IsDeleted by hand, which IgnoreQueryFilters drops) exactly like the central Submission
/// Agent path — the controller's permission gate is what restricts this to the Commission Agent.
/// </summary>
public class CommissionDeskHandler :
    IRequestHandler<ListCommissionSalesQuery, CommissionDeskResult>,
    IRequestHandler<SetCommissionStatusCommand, CommissionSaleDto>,
    IRequestHandler<UpdateCommissionAmountCommand, CommissionSaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly ICommissionEngine _commission;

    public CommissionDeskHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity,
        ICommissionEngine commission)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _commission = Guard.AgainstNull(commission);
    }

    public async Task<CommissionDeskResult> Handle(ListCommissionSalesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var q = _db.Sales.AsNoTracking().IgnoreQueryFilters().Where(s => !s.IsDeleted);

        if (request.AgencyId is { } aid) q = q.Where(s => s.AgencyId == aid);
        if (request.CallCenterId is { } ccid) q = q.Where(s => s.CallCenterId == ccid);
        if (!string.IsNullOrWhiteSpace(request.Carrier))
        {
            var carrier = request.Carrier.Trim();
            q = q.Where(s => s.Carrier == carrier || s.CarrierApproved == carrier);
        }
        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<ValidatorStatus>(request.Status, ignoreCase: true, out var status))
            q = q.Where(s => s.ValidatorStatus == status);
        if (request.From is { } from) q = q.Where(s => s.SoldAt >= from);
        if (request.To is { } to) q = q.Where(s => s.SoldAt < to);

        var total = await q.CountAsync(ct);
        // Money is summed IN MEMORY — SQLite cannot SUM a decimal (it throws once rows exist).
        var allPremiums = await q.Select(s => s.MonthlyPremium).ToListAsync(ct);
        var totalPremium = allPremiums.Sum();

        var take = Math.Clamp(request.Take, 1, 200);
        var sales = await q.OrderByDescending(s => s.SoldAt)
            .Skip(Math.Max(0, request.Skip)).Take(take).ToListAsync(ct);

        var items = await BuildAsync(sales, ct);

        // Free-text search runs after the join so it can match the customer's name/phone.
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var needle = request.Search.Trim().ToLowerInvariant();
            items = items.Where(i =>
                i.CustomerName.ToLowerInvariant().Contains(needle) ||
                i.PhoneNumber.Contains(needle) ||
                (i.PolicyNumber ?? "").ToLowerInvariant().Contains(needle) ||
                i.Carrier.ToLowerInvariant().Contains(needle)).ToList();
        }

        var totalFunded = items.Sum(i => i.FundedAmount);
        return new CommissionDeskResult(items, total, totalFunded, totalPremium);
    }

    public async Task<CommissionSaleDto> Handle(SetCommissionStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var sale = await _db.Sales.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == request.SaleId && !s.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        var previous = sale.ValidatorStatus;
        sale.ValidatorStatus = request.Status;
        var note = request.Note?.Trim();
        if (request.Status is ValidatorStatus.Decline or ValidatorStatus.ClientCancelled
            or ValidatorStatus.ChargedBack && !string.IsNullOrWhiteSpace(note))
            sale.DeclineReason = note;

        switch (request.Status)
        {
            case ValidatorStatus.ChargedBack:
                sale.ChargedBackAt ??= DateTime.UtcNow;
                await NegateCommissionsAsync(sale, ct);
                break;
            case ValidatorStatus.ActivePaid:
                sale.ValidatedAt ??= DateTime.UtcNow;
                sale.FundedAt ??= DateTime.UtcNow;
                sale.ChargedBackAt = null;      // recovered — no longer charged back
                break;
            case ValidatorStatus.Approved:
                sale.ValidatedAt ??= DateTime.UtcNow;
                break;
        }

        // Keep the lead's pipeline stage consistent. Negative outcomes go Lost, which is also what
        // puts the policy in front of Retention (Retention lists by status, see CommissionDeskStatuses).
        var lead = await _db.Leads.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == sale.LeadId && !l.IsDeleted, ct);
        if (lead is not null)
        {
            var from = lead.Stage;
            lead.Stage = request.Status switch
            {
                ValidatorStatus.ActivePaid => WorkflowStage.Funded,
                ValidatorStatus.Approved => WorkflowStage.Validated,
                ValidatorStatus.Decline or ValidatorStatus.ClientCancelled => WorkflowStage.Lost,
                _ => WorkflowStage.Closed,
            };
            lead.UpdatedAt = DateTime.UtcNow;
            _db.LeadActivities.Add(new LeadActivity
            {
                AgencyId = lead.AgencyId,
                CallCenterId = lead.CallCenterId,
                LeadId = lead.Id,
                UserId = _user.UserId.Value,
                FromStage = from,
                ToStage = lead.Stage,
                Disposition = lead.Disposition,
                Notes = string.IsNullOrWhiteSpace(note)
                    ? $"Commission desk: {previous} → {request.Status}."
                    : $"Commission desk: {previous} → {request.Status}. {note}",
            });
        }

        await _db.SaveChangesAsync(ct);
        return (await BuildAsync(new[] { sale }, ct)).Single();
    }

    public async Task<CommissionSaleDto> Handle(UpdateCommissionAmountCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var sale = await _db.Sales.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == request.SaleId && !s.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        // Amounts are only editable once the sale is charged back — otherwise the commission engine
        // owns them and a hand-edit would silently diverge from the calculated ledger.
        if (sale.ValidatorStatus != ValidatorStatus.ChargedBack)
            throw new ConflictException("Amounts can only be edited on a charged-back sale.");

        var entry = await _db.CommissionEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.EntryId && c.SaleId == sale.Id && !c.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(CommissionEntry), request.EntryId);

        // A paid line is history — payroll already paid it out; never rewrite it.
        if (entry.Paid) throw new ConflictException("This amount was already paid out and can't be edited.");

        // The audit interceptor records the before/after of this change automatically.
        entry.Amount = request.Amount;
        if (!string.IsNullOrWhiteSpace(request.Note)) entry.Note = request.Note.Trim();

        await _db.SaveChangesAsync(ct);
        return (await BuildAsync(new[] { sale }, ct)).Single();
    }

    /// <summary>
    /// Flip every UNPAID commission line on the sale negative (a clawback). Idempotent: a line that
    /// is already negative stays as-is, so re-marking a chargeback never doubles it. Paid lines are
    /// untouched history — the desk edits those by hand if the carrier reverses them.
    /// </summary>
    private async Task NegateCommissionsAsync(Sale sale, CancellationToken ct)
    {
        var lines = await _db.CommissionEntries.IgnoreQueryFilters()
            .Where(c => c.SaleId == sale.Id && !c.IsDeleted && !c.Paid)
            .ToListAsync(ct);
        foreach (var line in lines)
            if (line.Amount > 0) line.Amount = -line.Amount;
    }

    /// <summary>Maps sales onto the desk DTO, joining lead, agency, call centre, amounts and carrier rule.</summary>
    private async Task<List<CommissionSaleDto>> BuildAsync(IReadOnlyList<Sale> sales, CancellationToken ct)
    {
        if (sales.Count == 0) return new List<CommissionSaleDto>();

        var saleIds = sales.Select(s => s.Id).ToList();
        var leadIds = sales.Select(s => s.LeadId).Distinct().ToList();
        var agencyIds = sales.Select(s => s.AgencyId).Distinct().ToList();
        // CallCenterId is non-nullable on CallCenterEntity; Guid.Empty means agency-level (no centre).
        var ccIds = sales.Where(s => s.CallCenterId != Guid.Empty).Select(s => s.CallCenterId).Distinct().ToList();

        var leadById = (await _db.Leads.AsNoTracking().IgnoreQueryFilters()
                .Where(l => leadIds.Contains(l.Id) && !l.IsDeleted)
                .Select(l => new { l.Id, l.FirstName, l.LastName, l.PhoneNumber })
                .ToListAsync(ct))
            .ToDictionary(l => l.Id);

        var agencyById = (await _db.Agencies.AsNoTracking().IgnoreQueryFilters()
                .Where(a => agencyIds.Contains(a.Id) && !a.IsDeleted)
                .Select(a => new { a.Id, a.Name }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.Name);

        var ccById = (await _db.CallCenters.AsNoTracking().IgnoreQueryFilters()
                .Where(c => ccIds.Contains(c.Id) && !c.IsDeleted)
                .Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.Name);

        var entries = await _db.CommissionEntries.AsNoTracking().IgnoreQueryFilters()
            .Where(c => saleIds.Contains(c.SaleId) && !c.IsDeleted)
            .ToListAsync(ct);
        var entriesBySale = entries.GroupBy(c => c.SaleId).ToDictionary(g => g.Key, g => g.ToList());

        // Names for the agents on the commission lines (cross-agency, so pull the full directory).
        var nameById = await _identity.ListUserNamesAsync(null, ct);

        // Global carrier advancing rules, matched case-insensitively on the (approved) carrier.
        var rules = await _db.CarrierAdvancingRules.AsNoTracking()
            .Where(r => r.IsActive && !r.IsDeleted).ToListAsync(ct);
        var ruleByCarrier = rules
            .GroupBy(r => r.Carrier.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<CommissionSaleDto>(sales.Count);
        foreach (var s in sales)
        {
            leadById.TryGetValue(s.LeadId, out var lead);
            var lines = entriesBySale.TryGetValue(s.Id, out var l) ? l : new List<CommissionEntry>();

            var carrierKey = (s.CarrierApproved ?? s.Carrier ?? "").Trim().ToLowerInvariant();
            ruleByCarrier.TryGetValue(carrierKey, out var rule);
            decimal? expectedAdvance = rule is null
                ? null
                : Math.Round(s.MonthlyPremium * rule.AdvancedMonths * (rule.CommissionRate / 100m), 2);

            result.Add(new CommissionSaleDto(
                s.Id, s.SaleNumber, s.LeadId,
                lead is null ? "" : $"{lead.FirstName} {lead.LastName}".Trim(),
                lead?.PhoneNumber ?? "",
                s.AgencyId, agencyById.TryGetValue(s.AgencyId, out var an) ? an : "",
                s.CallCenterId == Guid.Empty ? null : s.CallCenterId,
                ccById.TryGetValue(s.CallCenterId, out var ccName) ? ccName : null,
                s.Carrier, s.CarrierApproved, s.PolicyNumber,
                s.MonthlyPremium, s.CoverageApproved, s.PremiumApproved, s.PlanApproved,
                s.ValidatorStatus.ToString(),
                lines.Sum(c => c.Amount),
                lines.Select(c => new CommissionAmountDto(
                        c.Id, c.RuleName, c.AgentUserId,
                        nameById.TryGetValue(c.AgentUserId, out var nm) ? nm : null,
                        c.Amount, c.Paid, c.Note))
                    .OrderBy(a => a.RuleName).ToList(),
                rule?.CommissionRate, rule?.AdvancedMonths, expectedAdvance,
                s.SoldAt, s.ValidatedAt, s.FundedAt, s.ChargedBackAt));
        }
        return result;
    }
}
