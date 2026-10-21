using CRM.Application.Common.Authorization;
using CRM.Application.Common.Commission;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Application.Sales.Notifications;
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
    /// <summary>The submission agent's own note about this sale. Optional.</summary>
    string? SubmissionComment,
    Guid? ValidatorUserId,
    string? ValidatorName,
    Guid? LicenseAgentUserId,
    string? LicenseAgentName,
    /// <summary>
    /// The agency the assigned licence agent belongs to, which is NOT always the agency that sold
    /// the policy — the agent of record is whoever holds the carrier appointment.
    ///
    /// The approval screen needs it to reopen on the right agency: its Agency picker drives the
    /// list of agents, so defaulting to the sale's agency loaded a list the assigned agent was not
    /// in, and the Licence Agent field came back empty on a sale that plainly had one.
    /// </summary>
    Guid? LicenseAgentAgencyId,
    /// <summary>That agency's name, so the picker never has to show a bare id.</summary>
    string? LicenseAgentAgencyName,
    DateTime SoldAt,
    DateTime? ValidatedAt);

/// <summary>
/// Every sale in the agency, newest first — a sale lands here as <see cref="ValidatorStatus.Completed"/>
/// the moment the closer submits "Complete and Sold".
/// </summary>
/// <summary>
/// <paramref name="Take"/> bounds the response. It was 200, which was ample while a submission
/// agent saw one agency — and became a silent data-loss bug the moment a central agent began
/// pooling EVERY agency into one queue. Ordered newest-first, the 201st sale simply stopped being
/// returned: each new sale pushed an older one off the bottom, with nothing on screen to say the
/// list had been cut, so sales appeared to vanish while people were working.
///
/// Raised rather than removed: the page renders every row it is given, so this still has to be a
/// number. It is now far above any plausible pending volume, and the real answer — paging, with
/// the count of what is not shown — is still owed.
/// </summary>
public record ValidatorQueueQuery(int Take = 1000) : IRequest<IReadOnlyList<ValidatorQueueItem>>;

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

/// <summary>
/// Set (or clear) the submission agent's note on a sale.
///
/// Separate from SetValidatorStatusCommand on purpose: a comment is optional and independent of the
/// outcome, and making someone re-pick a status just to leave a note would mean the note never got
/// written. Passing null or blank clears it.
/// </summary>
public record SetSubmissionCommentCommand(Guid SaleId, string? Comment) : IRequest<Unit>;

public class SetSubmissionCommentValidator : AbstractValidator<SetSubmissionCommentCommand>
{
    public SetSubmissionCommentValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty();
        RuleFor(x => x.Comment!).MaximumLength(2000).When(x => x.Comment is not null);
    }
}

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

        // "Bad customer" requires a note. The whole point of the status is the warning it carries
        // to whoever meets this customer next; without the note it says nothing.
        When(x => x.Status == ValidatorStatus.BadCustomer, () =>
            RuleFor(x => x.DeclineReason).NotEmpty().WithMessage("Say what happened — this note is the warning."));

        // "Error in application information" requires the specific sub-reason.
        When(x => x.Status == ValidatorStatus.ErrorInApplicationInformation, () =>
            RuleFor(x => x.DeclineReason).NotEmpty().WithMessage("Select the application error (banking/payor or identity)."));
    }
}

public class ValidatorQueueHandler :
    IRequestHandler<ValidatorQueueQuery, IReadOnlyList<ValidatorQueueItem>>,
    IRequestHandler<SetValidatorStatusCommand, ValidatorStatusResult>,
    IRequestHandler<SetSubmissionCommentCommand, Unit>
{
    // User-facing notification copy. Kept as constants next to the sender and free of internal
    // status names — the closer is told what happened in plain language.
    private const string DeclinedTitle = "Your sale was declined";
    private const string NeedsCorrectionTitle = "Your sale needs correcting";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly ICommissionEngine _commission;
    private readonly INotificationDispatcher _notify;
    private readonly IPolicyWelcomeEmailSender _welcome;

    public ValidatorQueueHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity,
        ICommissionEngine commission, INotificationDispatcher notify, IPolicyWelcomeEmailSender welcome)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _commission = Guard.AgainstNull(commission);
        _notify = Guard.AgainstNull(notify);
        _welcome = Guard.AgainstNull(welcome);
    }

    public async Task<IReadOnlyList<ValidatorQueueItem>> Handle(ValidatorQueueQuery request, CancellationToken ct)
    {
        // A "central" (SMH-level) Submission Agent validates sales across ALL agencies; an
        // agency-scoped validator only sees their own. Central reads bypass the tenant query
        // filter, so we must re-add the soft-delete predicate by hand (IgnoreQueryFilters drops it too).
        // Everyone who works the whole platform, not just the central Submission Agent.
        //
        // This asked only "are you a Validator with no agency?", which a SuperAdmin is not — they
        // hold no Validator role. So they fell to the agency-scoped branch and were filtered to an
        // agency id of all zeroes, which matches no sale: the submission queue showed a SuperAdmin
        // "0 of 0 sales" on a platform full of them. Exactly the trap TenantScope exists to close.
        var central = TenantScope.ReadsEveryAgency(_user);
        if (!central && _user.AgencyId is null) throw new ForbiddenAccessException();

        // Ceiling raised with the default above: a cross-agency queue pools every agency, and the
        // old ceiling of 500 would have capped it again just past the new default.
        var take = Math.Clamp(request.Take, 1, 5000);

        var salesQ = _db.Sales.AsNoTracking();
        salesQ = central
            ? salesQ.IgnoreQueryFilters().Where(s => !s.IsDeleted)
            : salesQ.Where(s => s.AgencyId == _user.AgencyId);

        // A sale flagged BadCustomer leaves this queue. There is no submission work left on it, and
        // leaving it in means a submission agent re-reads the same dead row every morning. It is not
        // deleted and it still appears in the sales list — the sale happened and the figures have to
        // reconcile; it is only this working queue it drops out of.
        salesQ = salesQ.Where(s => s.ValidatorStatus != ValidatorStatus.BadCustomer);

        var sales = await salesQ.OrderByDescending(s => s.SoldAt).Take(take).ToListAsync(ct);

        var leadIds = sales.Select(s => s.LeadId).Distinct().ToList();
        var leadsQ = _db.Leads.AsNoTracking();
        if (central) leadsQ = leadsQ.IgnoreQueryFilters().Where(l => !l.IsDeleted);
        var leads = await leadsQ.Where(l => leadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.State })
            .ToListAsync(ct);
        var leadById = leads.ToDictionary(l => l.Id);

        // Cross-agency queues need names from every agency; scoped queues only their own.
        var users = await _identity.ListUsersAsync(central ? null : _user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);

        // A licence agent may sit OUTSIDE the caller's agency — a sale is routinely written under
        // the agent holding the carrier appointment, who works for someone else. For an
        // agency-scoped validator those people are missing from the roster above, so the column
        // read "—" on a sale that plainly had an agent assigned. Fetch just the handful referenced.
        var missingAgentIds = sales
            .Select(s => s.LicenseAgentUserId)
            .Where(id => id is { } g && !byId.ContainsKey(g))
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (missingAgentIds.Count > 0)
        {
            foreach (var u in (await _identity.ListUsersAsync(null, ct)).Where(u => missingAgentIds.Contains(u.Id)))
                byId[u.Id] = u;
        }

        string? Name(Guid? id) => id is { } g && byId.TryGetValue(g, out var u) ? u.UserName : null;
        Guid? AgencyOf(Guid? id) => id is { } g && byId.TryGetValue(g, out var u) && u.AgencyId != Guid.Empty
            ? u.AgencyId : null;

        // Agency names for both the selling agency and whichever agency each licence agent belongs
        // to — resolved in one pass so the approval screen never has to show a bare id.
        var agencyIds = sales.Select(s => s.AgencyId)
            .Concat(sales.Select(s => AgencyOf(s.LicenseAgentUserId)).Where(id => id is not null).Select(id => id!.Value))
            .Distinct().ToList();
        var agencyById = (await _db.Agencies.AsNoTracking().IgnoreQueryFilters()
                .Where(a => agencyIds.Contains(a.Id) && !a.IsDeleted)
                .Select(a => new { a.Id, a.Name }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.Name);

        string? AgencyNameOf(Guid? id) => id is { } g && agencyById.TryGetValue(g, out var n) ? n : null;

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
                s.PlanApproved, s.DeclineReason, s.SubmissionComment,
                s.ValidatorUserId, Name(s.ValidatorUserId),
                s.LicenseAgentUserId, Name(s.LicenseAgentUserId),
                AgencyOf(s.LicenseAgentUserId), AgencyNameOf(AgencyOf(s.LicenseAgentUserId)),
                s.SoldAt, s.ValidatedAt);
        }).ToList();
    }

    public async Task<Unit> Handle(SetSubmissionCommentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        // A central Submission Agent carries no agency, so the global tenant filter would scope this
        // read to Guid.Empty and match nothing — every note on every sale came back as "not found",
        // on the one role whose whole job is working other agencies' sales. Mirrors the status path.
        // Everyone who works the whole platform, not just the central Submission Agent.
        //
        // This asked only "are you a Validator with no agency?", which a SuperAdmin is not — they
        // hold no Validator role. So they fell to the agency-scoped branch and were filtered to an
        // agency id of all zeroes, which matches no sale: the submission queue showed a SuperAdmin
        // "0 of 0 sales" on a platform full of them. Exactly the trap TenantScope exists to close.
        var central = TenantScope.ReadsEveryAgency(_user);
        var sale = (central
            ? await _db.Sales.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == request.SaleId && !s.IsDeleted, ct)
            : await _db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId, ct))
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment == sale.SubmissionComment) return Unit.Value;

        sale.SubmissionComment = comment;
        await _db.SaveChangesAsync(ct);

        // The closer is told, because a note on their sale is usually a question for them — "carrier
        // wants a second signature", "bank details bounced". Left silent it is a message nobody
        // receives. Cleared comments say nothing; there is nothing to read.
        if (comment is not null && sale.CloserUserId != _user.UserId)
        {
            try
            {
                await _notify.DispatchAsync(
                    new NotificationPayload(
                        sale.AgencyId, sale.CloserUserId,
                        $"Note on sale #{sale.SaleNumber}",
                        comment.Length <= 140 ? comment : comment[..140].TrimEnd() + "…",
                        "/my-sales"),
                    new[] { NotificationChannelType.InApp }, ct);
            }
            catch { /* graceful — the note is saved either way */ }
        }

        return Unit.Value;
    }

    public async Task<ValidatorStatusResult> Handle(SetValidatorStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        // Everyone who works the whole platform, not just the central Submission Agent.
        //
        // This asked only "are you a Validator with no agency?", which a SuperAdmin is not — they
        // hold no Validator role. So they fell to the agency-scoped branch and were filtered to an
        // agency id of all zeroes, which matches no sale: the submission queue showed a SuperAdmin
        // "0 of 0 sales" on a platform full of them. Exactly the trap TenantScope exists to close.
        var central = TenantScope.ReadsEveryAgency(_user);
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
                    // A sale that is good again is no longer charged back (mirrors the commission desk).
                    sale.ChargedBackAt = null;
                    break;
                case ValidatorStatus.ActivePaid:
                    lead.Stage = WorkflowStage.Funded;
                    sale.ValidatedAt ??= DateTime.UtcNow;
                    sale.FundedAt ??= DateTime.UtcNow;
                    sale.ChargedBackAt = null;
                    break;
                case ValidatorStatus.Decline:
                case ValidatorStatus.ClientCancelled:
                    lead.Stage = WorkflowStage.Lost;
                    // Clear the funded stamp: ListSales and the dashboard derive "Funded" purely from
                    // FundedAt != null, so leaving it set kept counting a dead policy as revenue.
                    sale.FundedAt = null;
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

        // Onboard the customer once the policy is approved: a branded welcome email carrying the
        // approved carrier + coverage, sent as the agency (reply-to their inbox, logo inline).
        // Best-effort and idempotent (WelcomeEmailSentAt), so it never blocks or double-sends.
        await MaybeSendWelcomeEmailAsync(sale, lead, request.Status, ct);

        // A negative outcome is the closer's problem to act on — a decline kills their sale and its
        // commission, an application error needs them to correct and resubmit. Tell them, with the
        // reason the validator recorded.
        await NotifyCloserOfProblemAsync(sale, lead, request.Status, ct);

        return new ValidatorStatusResult(sale.Id, sale.ValidatorStatus, lead?.Stage ?? WorkflowStage.Closed);
    }

    /// <summary>
    /// Best-effort in-app notice to the closer when the validator sets an outcome that needs their
    /// attention, carrying the recorded reason. Never notifies the validator about their own action.
    /// </summary>
    private async Task NotifyCloserOfProblemAsync(Sale sale, Lead? lead, ValidatorStatus status, CancellationToken ct)
    {
        if (sale.CloserUserId == _user.UserId || sale.CloserUserId == Guid.Empty) return;

        var customer = lead is null ? "a customer" : $"{lead.FirstName} {lead.LastName}".Trim();
        if (customer.Length == 0) customer = "a customer";
        var reason = sale.DeclineReason?.Trim();
        var (title, body) = status switch
        {
            ValidatorStatus.Decline =>
                (DeclinedTitle,
                 $"Your sale for {customer} was declined, and the commission still owed on it was cancelled."),
            ValidatorStatus.ErrorInApplicationInformation =>
                (NeedsCorrectionTitle,
                 $"There is an error in the application information on your sale for {customer}. Please correct it and resubmit."),
            _ => (string.Empty, string.Empty),
        };
        if (title.Length == 0) return;   // every other outcome needs nothing from the closer
        if (!string.IsNullOrWhiteSpace(reason)) body = $"{body} Reason: {reason}";

        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(sale.AgencyId, sale.CloserUserId, title, body, $"/sales/{sale.Id}"),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* advisory only — never fail the status change */ }
    }

    private async Task MaybeSendWelcomeEmailAsync(Sale sale, Lead? lead, ValidatorStatus status, CancellationToken ct)
    {
        if (status is not (ValidatorStatus.Approved or ValidatorStatus.ActivePaid)) return;
        if (sale.WelcomeEmailSentAt is not null) return;                 // already onboarded
        if (string.IsNullOrWhiteSpace(sale.CarrierApproved)) return;     // no approved carrier to announce

        // Customer email: the lead's, falling back to the application's. IgnoreQueryFilters so a
        // central Submission Agent (empty tenant) still resolves the real agency's application row.
        var customerEmail = lead?.Email;
        if (string.IsNullOrWhiteSpace(customerEmail))
            customerEmail = await _db.LeadApplications.IgnoreQueryFilters()
                .Where(a => a.LeadId == sale.LeadId && a.AgencyId == sale.AgencyId)
                .Select(a => a.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(customerEmail)) return;

        var customerName = lead is null ? "" : $"{lead.FirstName} {lead.LastName}".Trim();
        var sent = await _welcome.SendAsync(new PolicyWelcomeEmailRequest(
            sale.AgencyId, customerEmail!, customerName, sale.CarrierApproved!,
            sale.CoverageApproved, sale.PremiumApproved, sale.PlanApproved), ct);

        if (sent)
        {
            sale.WelcomeEmailSentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Assigns an agency-level License Agent to an approved sale and records their commission.
    /// The agent MUST belong to the sale's own agency and hold the LicenseAgent role — otherwise
    /// a central agent could mint commission for an arbitrary/foreign user. Idempotent: the
    /// commission line is created once; a later re-assignment updates it only while still unpaid.
    /// </summary>
    private async Task AssignLicenseAgentAsync(Sale sale, Guid licenseAgentId, CancellationToken ct)
    {
        // The sale's own agency first — the ordinary case, and the only one an agency-scoped
        // validator is allowed.
        var agent = (await _identity.ListUsersAsync(sale.AgencyId, ct))
            .FirstOrDefault(u => u.Id == licenseAgentId
                && u.Roles.Contains(DomainRoles.LicenseAgent, StringComparer.OrdinalIgnoreCase));

        // Then anywhere, but only for a caller who legitimately works across agencies.
        //
        // A sale closed by one company is routinely written under a licensed agent at another — the
        // agent of record is whoever holds the carrier appointment, not whoever took the call. The
        // approval screen has always offered those agents in its Agency picker; it was the save that
        // refused them, so the picker listed names that could never be assigned.
        //
        // An agency-scoped validator is deliberately NOT given this: letting one agency's staff
        // attribute commission to another agency's people would put money across a tenant boundary.
        // Leaving the existing agent exactly as they are is always allowed, whoever is saving.
        //
        // Without this, an agency-scoped validator could not touch a sale that a central submission
        // agent had assigned to an outside agent: re-saving sends the agent already on the sale,
        // the cross-agency lookup is not theirs to use, and they would be refused — unable to change
        // the status of their own agency's sale because of an assignment somebody else made.
        var unchanged = sale.LicenseAgentUserId == licenseAgentId;

        var crossAgency = false;
        if (agent is null
            && (unchanged || TenantScope.ReadsEveryAgency(_user)))
        {
            agent = (await _identity.ListUsersAsync(null, ct))
                .FirstOrDefault(u => u.Id == licenseAgentId
                    && u.Roles.Contains(DomainRoles.LicenseAgent, StringComparer.OrdinalIgnoreCase));
            crossAgency = agent is not null;
        }

        if (agent is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["licenseAgentUserId"] = new[] { "That person isn't a licence agent you can assign this sale to." }
            });

        sale.LicenseAgentUserId = licenseAgentId;

        var line = (await _commission.CalculateForAgentAsync(sale, licenseAgentId, DomainRoles.LicenseAgent, ct))
            .FirstOrDefault();
        if (line is null) return; // e.g. internal sale — no license-agent commission

        // The commission follows the AGENT, not the sale.
        //
        // Commission rows are read back scoped to the reader's own agency (MyCommissions, payroll),
        // so a row filed under the selling company would be invisible to an agent at another one —
        // earned, owed, and never shown or paid. The sale itself stays with the company that sold
        // it; only the licence agent's line moves.
        // Guid.Empty would mean an agent belonging to no agency at all — not a thing a licence agent
        // should be, and a commission filed there would be unreachable. Fall back to the sale's.
        var commissionAgency = crossAgency && agent.AgencyId != Guid.Empty ? agent.AgencyId : sale.AgencyId;
        // The sale's call centre belongs to the selling company and means nothing in another one.
        // Licence agents are agency-level anyway (no call centre), and an agency-level reader sees
        // every call centre in their agency, so an empty one is both honest and visible.
        var commissionCallCenter = crossAgency ? Guid.Empty : sale.CallCenterId;

        // Keyed on the sale and the rule ONLY — no agency predicate.
        //
        // A sale id already identifies one sale, so the agency added nothing; worse, re-assigning
        // from an agent at one company to an agent at another would miss the existing row and
        // strand it, leaving the first agent still owed commission on a sale that is no longer
        // theirs. IgnoreQueryFilters because a central Submission Agent's own tenant is empty and
        // a soft-deleted row from a previous unassign has to be found to be revived.
        var existing = await _db.CommissionEntries.IgnoreQueryFilters().FirstOrDefaultAsync(
            c => c.SaleId == sale.Id && c.RuleName == line.RuleName, ct);
        if (existing is null)
        {
            _db.CommissionEntries.Add(new CommissionEntry
            {
                AgencyId = commissionAgency,
                CallCenterId = commissionCallCenter,
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
            // Moved too, so the row lands with whoever is now owed it rather than staying with the
            // company of the agent it was taken from.
            existing.AgencyId = commissionAgency;
            existing.CallCenterId = commissionCallCenter;
            existing.Amount = line.Amount;
            existing.Note = line.Note;
        }
        // else: already paid out — leave the historical entry untouched.

        // Only when the agent actually CHANGED.
        //
        // Two reasons. It is the honest one — re-saving an approved sale to correct a premium is
        // not news to an agent who was told days ago, and "a sale was assigned to you" arriving
        // again every time somebody edits it is noise. And it is the safe one: the notice is filed
        // in the agent's agency, which for an agency-scoped validator is somebody else's tenant —
        // the write is refused at save time, and because that happens in the CALLER's
        // SaveChangesAsync rather than here, the try/catch below would not have caught it. A
        // validator re-saving their own agency's sale was getting "Access denied" because of an
        // assignment another person had made.
        if (unchanged) return;

        // Let the License Agent know a sale is now theirs (bell + live push + email). Best-effort:
        // the SaveChangesAsync happens in the caller, so we only queue the notification write here —
        // the InApp channel persists via the same context and is flushed with the caller's save.
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(
                    // The agent's own agency, for the same reason the commission goes there: a
                    // notice filed under the selling company never reaches someone outside it.
                    commissionAgency, licenseAgentId,
                    "A sale was assigned to you",
                    $"Sale #{sale.SaleNumber} ({sale.Carrier}, {sale.MonthlyPremium:C}/mo) is now assigned to you for approval.",
                    // The sale record itself belongs to the selling company and stays readable only
                    // inside it, so sending an agent at another company to /sales/{id} would land
                    // them on a page they cannot open. Their commission is the part that IS theirs.
                    crossAgency ? "/commissions" : $"/sales/{sale.Id}"),
                new[] { NotificationChannelType.InApp, NotificationChannelType.Email }, ct);
        }
        catch { /* advisory only — never fail the approval */ }
    }
}
