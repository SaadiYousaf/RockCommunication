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

namespace CRM.Application.Retention;

/// <summary>
/// The set of post-submission policy statuses that land a sale in the Retention worklist —
/// problems a retention agent tries to recover. Canonical here; the frontend mirrors it.
/// </summary>
public static class RetentionStatuses
{
    public static readonly ValidatorStatus[] All =
    {
        ValidatorStatus.BadBank,
        ValidatorStatus.Nsf,
        ValidatorStatus.ClientCancelled,
        ValidatorStatus.Decline,
        ValidatorStatus.ErrorInApplicationInformation,
    };

    /// <summary>Statuses a retention agent may set: recover (ActivePaid) or one of the problem states.</summary>
    public static readonly ValidatorStatus[] Targets =
        new[] { ValidatorStatus.ActivePaid }.Concat(All).ToArray();
}

/// <summary>A policy currently sitting in retention (bad post-submission status), in the caller's scope.</summary>
public record RetentionPolicyDto(
    Guid SaleId,
    int SaleNumber,
    Guid LeadId,
    string LeadName,
    string LeadPhone,
    string? State,
    string Carrier,
    string? PolicyNumber,
    decimal MonthlyPremium,
    Guid CloserUserId,
    string? CloserName,
    string Status,
    string? DeclineReason,
    DateTime SoldAt,
    DateTime? ValidatedAt);

/// <summary>Every policy in a problem status within the caller's agency/call-center scope, newest first.</summary>
public record ListRetentionPoliciesQuery(int Take = 200) : IRequest<IReadOnlyList<RetentionPolicyDto>>;

/// <summary>Retention agent works a policy: set its new status and (optionally) leave a note.</summary>
public record ResolveRetentionCommand(Guid SaleId, ValidatorStatus NewStatus, string? Note)
    : IRequest<RetentionResolveResult>;

public record RetentionResolveResult(Guid SaleId, string Status, WorkflowStage LeadStage);

public class ResolveRetentionValidator : AbstractValidator<ResolveRetentionCommand>
{
    public ResolveRetentionValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty();
        RuleFor(x => x.NewStatus)
            .Must(s => RetentionStatuses.Targets.Contains(s))
            .WithMessage("That is not a valid retention outcome.");
        // Declining / flagging an application error must carry the reason (mirrors the validator rule).
        When(x => x.NewStatus is ValidatorStatus.Decline or ValidatorStatus.ErrorInApplicationInformation, () =>
            RuleFor(x => x.Note).NotEmpty().WithMessage("A note explaining the reason is required."));
    }
}

public class RetentionHandler :
    IRequestHandler<ListRetentionPoliciesQuery, IReadOnlyList<RetentionPolicyDto>>,
    IRequestHandler<ResolveRetentionCommand, RetentionResolveResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly INotificationDispatcher _notify;

    public RetentionHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _notify = Guard.AgainstNull(notify);
    }

    public async Task<IReadOnlyList<RetentionPolicyDto>> Handle(ListRetentionPoliciesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var take = Math.Clamp(request.Take, 1, 500);

        // The global tenant filter already scopes to the caller's agency + call center (Sale is a
        // CallCenterEntity), so a call-center-pinned retention agent only sees their own centre.
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => RetentionStatuses.All.Contains(s.ValidatorStatus))
            .OrderByDescending(s => s.SoldAt)
            .Take(take)
            .ToListAsync(ct);
        if (sales.Count == 0) return Array.Empty<RetentionPolicyDto>();

        var leadIds = sales.Select(s => s.LeadId).Distinct().ToList();
        var leadById = (await _db.Leads.AsNoTracking()
                .Where(l => leadIds.Contains(l.Id))
                .Select(l => new { l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.State })
                .ToListAsync(ct))
            .ToDictionary(l => l.Id);

        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var nameById = users.ToDictionary(u => u.Id, u => u.UserName);
        string? Name(Guid id) => nameById.TryGetValue(id, out var n) ? n : null;

        return sales.Select(s =>
        {
            leadById.TryGetValue(s.LeadId, out var l);
            return new RetentionPolicyDto(
                s.Id, s.SaleNumber, s.LeadId,
                l is null ? "" : $"{l.FirstName} {l.LastName}".Trim(),
                l?.PhoneNumber ?? "", l?.State,
                s.Carrier, s.PolicyNumber, s.MonthlyPremium,
                s.CloserUserId, Name(s.CloserUserId),
                s.ValidatorStatus.ToString(), s.DeclineReason,
                s.SoldAt, s.ValidatedAt);
        }).ToList();
    }

    public async Task<RetentionResolveResult> Handle(ResolveRetentionCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var sale = await _db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId && s.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        // A retention agent may only work a policy that is actually IN retention (a problem status);
        // they can't reach into healthy/validated policies.
        if (!RetentionStatuses.All.Contains(sale.ValidatorStatus))
            throw new ConflictException("This policy is not in retention.");

        var previous = sale.ValidatorStatus;
        sale.ValidatorStatus = request.NewStatus;
        var note = request.Note?.Trim();
        if (request.NewStatus is ValidatorStatus.Decline or ValidatorStatus.ErrorInApplicationInformation)
            sale.DeclineReason = note;

        // Keep the lead's pipeline stage consistent with the new outcome. Retention deliberately does
        // NOT re-run the commission engine: that would re-stamp the sale's ValidatorUserId to the
        // retention agent and mis-pay the validator bonus. Commission adjustments stay a finance action.
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == sale.LeadId && l.AgencyId == sale.AgencyId, ct);
        var leadStage = WorkflowStage.Closed;
        if (lead is not null)
        {
            var from = lead.Stage;
            switch (request.NewStatus)
            {
                case ValidatorStatus.ActivePaid:
                    lead.Stage = WorkflowStage.Funded;
                    sale.ValidatedAt ??= DateTime.UtcNow;
                    sale.FundedAt ??= DateTime.UtcNow;
                    // The outcome that sent this policy to retention voided its unpaid commission.
                    // Recovering it must bring that money back, or the closer gets a "Policy
                    // recovered" notification for a sale they are still paid nothing on.
                    await CommissionLedger.ReviveUnpaidAsync(_db, sale, ct);
                    sale.DeclineReason = null;   // the old failure reason no longer applies
                    break;
                case ValidatorStatus.Decline:
                case ValidatorStatus.ClientCancelled:
                    lead.Stage = WorkflowStage.Lost;
                    break;
                default:
                    lead.Stage = WorkflowStage.Closed;   // still a problem — stays in the worklist
                    break;
            }
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
                    ? $"Retention: {previous} → {request.NewStatus}."
                    : $"Retention: {previous} → {request.NewStatus}. {note}"
            });
            leadStage = lead.Stage;
        }

        await _db.SaveChangesAsync(ct);

        // Tell the closer their policy's outcome changed — a recovery or a close affects them (and
        // their commission). Best-effort, code-free copy, and never notify yourself.
        await NotifyCloserAsync(sale, lead, request.NewStatus, ct);

        return new RetentionResolveResult(sale.Id, sale.ValidatorStatus.ToString(), leadStage);
    }

    private async Task NotifyCloserAsync(Sale sale, Lead? lead, ValidatorStatus newStatus, CancellationToken ct)
    {
        if (sale.CloserUserId == _user.UserId || sale.CloserUserId == Guid.Empty) return;
        var (title, body) = newStatus switch
        {
            ValidatorStatus.ActivePaid =>
                ("Policy recovered", $"Your sale for {LeadName(lead)} is active again — recovered by retention."),
            ValidatorStatus.Decline or ValidatorStatus.ClientCancelled =>
                ("Policy closed in retention", $"Your sale for {LeadName(lead)} was closed by retention."),
            _ => (string.Empty, string.Empty),
        };
        if (title.Length == 0) return;   // an interim status change isn't worth pinging the closer
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(sale.AgencyId, sale.CloserUserId, title, body, $"/sales/{sale.Id}"),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* graceful — the resolution already succeeded */ }
    }

    private static string LeadName(Lead? lead)
        => lead is null ? "a customer" : $"{lead.FirstName} {lead.LastName}".Trim();
}
