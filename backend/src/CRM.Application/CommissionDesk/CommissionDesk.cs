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

    /// <summary>
    /// The amount an unpaid commission line should hold for an outcome. Delegates to the shared
    /// <see cref="CommissionLedger"/> so the rule lives in exactly one place; kept here as the name
    /// the desk's tests and callers already use.
    /// </summary>
    public static decimal SignedAmount(decimal amount, bool clawedBack) =>
        CommissionLedger.SignedAmount(amount, clawedBack);
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
    // User-facing notification copy. Kept as constants next to the sender and deliberately free of
    // internal status names — the agent is told what happened to their money in plain language.
    private const string ChargedBackTitle = "Commission charged back";
    private const string PolicyClosedTitle = "Policy closed";
    private const string AmountChangedTitle = "Commission amount changed";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly ICommissionEngine _commission;
    private readonly INotificationDispatcher _notify;

    public CommissionDeskHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity,
        ICommissionEngine commission, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _commission = Guard.AgainstNull(commission);
        _notify = Guard.AgainstNull(notify);
    }

    /// <summary>
    /// True only for a CROSS-AGENCY commission agent (holds the role, bound to no agency). Everyone
    /// else — including an agency-scoped commission agent — stays inside their own tenant. Mirrors
    /// the central Submission Agent rule; without it the filter bypass below let any holder of the
    /// permission read and rewrite another agency's sales and money by id.
    /// </summary>
    private bool IsCentral =>
        DomainRoles.IsCentralCommissionAgent(_user.AgencyId, _user.Roles) || _user.IsSuperAdmin;

    /// <summary>The agency a non-central caller is confined to. Throws if they have none.</summary>
    private Guid OwnAgency =>
        _user.AgencyId is { } a && a != Guid.Empty ? a : throw new ForbiddenAccessException();

    public async Task<CommissionDeskResult> Handle(ListCommissionSalesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var q = _db.Sales.AsNoTracking().IgnoreQueryFilters().Where(s => !s.IsDeleted);
        // Confine a non-central caller to their own agency BEFORE any caller-supplied filter.
        if (!IsCentral) { var own = OwnAgency; q = q.Where(s => s.AgencyId == own); }

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

        // Scope by agency unless the caller is a genuine cross-agency commission agent — an id
        // alone would otherwise let one tenant rewrite another tenant's sale and its commission.
        var sale = await _db.Sales.IgnoreQueryFilters()
            .Where(s => s.Id == request.SaleId && !s.IsDeleted)
            .Where(s => IsCentral || s.AgencyId == _user.AgencyId)
            .FirstOrDefaultAsync(ct)
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
                await FlipCommissionSignAsync(sale, negative: true, ct);
                break;
            case ValidatorStatus.ActivePaid:
                sale.ValidatedAt ??= DateTime.UtcNow;
                sale.FundedAt ??= DateTime.UtcNow;
                sale.ChargedBackAt = null;      // recovered — no longer charged back
                sale.DeclineReason = null;      // and the old rejection note no longer applies
                // Revive BEFORE flipping: a prior decline soft-deleted these lines, and the sign
                // flip only touches live rows — so without this the money never comes back.
                await CommissionLedger.ReviveUnpaidAsync(_db, sale, ct);
                await FlipCommissionSignAsync(sale, negative: false, ct);
                break;
            case ValidatorStatus.Approved:
                sale.ValidatedAt ??= DateTime.UtcNow;
                sale.ChargedBackAt = null;
                sale.DeclineReason = null;
                await CommissionLedger.ReviveUnpaidAsync(_db, sale, ct);
                await FlipCommissionSignAsync(sale, negative: false, ct);
                break;
            case ValidatorStatus.Decline:
            case ValidatorStatus.ClientCancelled:
                // A terminal negative outcome: void the still-unpaid commission so payroll never
                // pays out on a dead policy, and stop it counting as funded revenue (the sales list
                // and dashboard derive "Funded" purely from FundedAt). Mirrors ValidatorQueue.
                await CommissionLedger.VoidUnpaidAsync(_db, sale, ct);
                sale.FundedAt = null;
                break;
        }

        // Keep the lead's pipeline stage consistent. Negative outcomes go Lost, which is also what
        // puts the policy in front of Retention (Retention lists by status, see CommissionDeskStatuses).
        var lead = await _db.Leads.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == sale.LeadId && l.AgencyId == sale.AgencyId && !l.IsDeleted, ct);
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

        // The desk just moved someone else's money: a charge-back turns their commission negative and
        // a decline / client cancellation voids it outright. Tell the closer — best-effort, plain
        // language, and never a notice to the agent about their own action.
        await NotifyCloserAsync(sale, lead, request.Status, ct);

        return (await BuildAsync(new[] { sale }, ct)).Single();
    }

    public async Task<CommissionSaleDto> Handle(UpdateCommissionAmountCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var sale = await _db.Sales.IgnoreQueryFilters()
            .Where(s => s.Id == request.SaleId && !s.IsDeleted)
            .Where(s => IsCentral || s.AgencyId == _user.AgencyId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        // Amounts are only editable once the sale is charged back — otherwise the commission engine
        // owns them and a hand-edit would silently diverge from the calculated ledger.
        if (sale.ValidatorStatus != ValidatorStatus.ChargedBack)
            throw new ConflictException("Amounts can only be edited on a charged-back sale.");

        var entry = await _db.CommissionEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.EntryId && c.SaleId == sale.Id
                                   && c.AgencyId == sale.AgencyId && !c.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(CommissionEntry), request.EntryId);

        // A paid line is history — payroll already paid it out; never rewrite it.
        if (entry.Paid) throw new ConflictException("This amount was already paid out and can't be edited.");

        // The audit interceptor records the before/after of this change automatically.
        entry.Amount = request.Amount;
        if (!string.IsNullOrWhiteSpace(request.Note)) entry.Note = request.Note.Trim();

        await _db.SaveChangesAsync(ct);

        // The edited line is somebody's pay — tell the agent it holds a different amount now.
        await NotifyAmountChangedAsync(sale, entry, ct);

        return (await BuildAsync(new[] { sale }, ct)).Single();
    }

    /// <summary>
    /// Best-effort in-app notice to the sale's closer when the desk's outcome hits their commission.
    /// Only the outcomes that actually cost them money are worth a ping; an approval or recovery is
    /// already announced elsewhere. Never notifies the acting agent about their own action.
    /// </summary>
    private async Task NotifyCloserAsync(Sale sale, Lead? lead, ValidatorStatus status, CancellationToken ct)
    {
        if (sale.CloserUserId == _user.UserId || sale.CloserUserId == Guid.Empty) return;
        var (title, body) = status switch
        {
            ValidatorStatus.ChargedBack =>
                (ChargedBackTitle,
                 $"The carrier clawed back the advance on your sale for {LeadName(lead)}. Your commission on it is now owed back."),
            ValidatorStatus.Decline or ValidatorStatus.ClientCancelled =>
                (PolicyClosedTitle,
                 $"Your sale for {LeadName(lead)} was closed on the commission desk, and the commission still owed on it was cancelled."),
            _ => (string.Empty, string.Empty),
        };
        if (title.Length == 0) return;   // any other outcome doesn't cost the closer anything
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(sale.AgencyId, sale.CloserUserId, title, body, $"/sales/{sale.Id}"),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* graceful — the status change already succeeded */ }
    }

    /// <summary>
    /// Best-effort in-app notice to the agent whose commission line was hand-edited. The whole body
    /// build is inside the try: the customer-name lookup must never be able to fail the edit.
    /// </summary>
    private async Task NotifyAmountChangedAsync(Sale sale, CommissionEntry entry, CancellationToken ct)
    {
        if (entry.AgentUserId == _user.UserId || entry.AgentUserId == Guid.Empty) return;
        try
        {
            var body = $"Your commission on the sale for {await LeadNameAsync(sale, ct)} was changed to {entry.Amount:C}.";
            await _notify.DispatchAsync(
                new NotificationPayload(sale.AgencyId, entry.AgentUserId, AmountChangedTitle, body, "/commissions"),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* graceful — the amount change already succeeded */ }
    }

    private static string LeadName(Lead? lead)
        => lead is null ? "a customer" : $"{lead.FirstName} {lead.LastName}".Trim();

    /// <summary>The sale's customer name for notification copy. Filter-bypassing: a central desk
    /// agent has an empty tenant, so the scoped read would never find the real agency's lead.</summary>
    private async Task<string> LeadNameAsync(Sale sale, CancellationToken ct)
    {
        var lead = await _db.Leads.AsNoTracking().IgnoreQueryFilters()
            .Where(l => l.Id == sale.LeadId && l.AgencyId == sale.AgencyId && !l.IsDeleted)
            .Select(l => new { l.FirstName, l.LastName })
            .FirstOrDefaultAsync(ct);
        return lead is null ? "a customer" : $"{lead.FirstName} {lead.LastName}".Trim();
    }

    /// <summary>
    /// Drive every UNPAID commission line on the sale to the sign the outcome implies: negative for a
    /// charge-back (the carrier clawed the advance back), positive again when the policy recovers.
    /// Both directions are IDEMPOTENT — a line already on the right sign is left alone — so
    /// re-saving a status never doubles or re-flips an amount.
    ///
    /// The restore direction matters as much as the clawback: without it a sale taken
    /// ChargedBack -> Active/Paid kept its negative amounts forever, so a healthy policy still read
    /// as money owed on the desk, the dashboard and payroll.
    ///
    /// Paid lines are untouched history — payroll already paid them out; the desk reconciles those
    /// by hand through the amounts editor.
    /// </summary>
    private Task FlipCommissionSignAsync(Sale sale, bool negative, CancellationToken ct) =>
        // Delegates to the shared ledger, which scopes by the sale's agency — the private copy
        // this replaced filtered on SaleId alone.
        CommissionLedger.FlipSignAsync(_db, sale, negative, ct);

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
