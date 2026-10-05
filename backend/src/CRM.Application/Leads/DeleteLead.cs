using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Application.Leads;

/// <summary>
/// Remove a closed or sold lead from the working set, with a reason on the record.
///
/// Soft, never hard: a sold lead is the origin of a sale, a commission and a submission, and
/// destroying the row would leave those pointing at nothing. The global IsDeleted filter takes it
/// out of every list, queue, search and dashboard, while the row itself stays intact and auditable.
/// </summary>
public record DeleteLeadCommand(Guid LeadId, string Reason) : IRequest<Unit>;

public class DeleteLeadValidator : AbstractValidator<DeleteLeadCommand>
{
    public DeleteLeadValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        // A reason is mandatory. Removing someone's sale without saying why is the thing that turns
        // a deletion into an argument three weeks later, when the commission does not add up.
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Say why this lead is being removed.")
            .MinimumLength(5).WithMessage("Give a little more detail than that.")
            .MaximumLength(500);
    }
}

public class DeleteLeadHandler : IRequestHandler<DeleteLeadCommand, Unit>
{
    // A lead may be removed from ANY stage (owner's instruction, 6 Oct 2026). It was originally
    // restricted to the closed end of the pipeline, on the reasoning that this is for retiring
    // finished work rather than clearing leads nobody wants to call — but a duplicate or a junk
    // import is most obviously junk long before anyone closes it, and refusing to remove it just
    // left it in everyone's queue.
    //
    // What makes that safe is not the stage restriction, which only moved the problem. It is that
    // nothing is destroyed, a reason is required and kept on the record, the removal is audited with
    // the administrator's name, the people whose work it was are told, and only an administrator can
    // do it at all. Those are unchanged.

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly INotificationDispatcher _notify;

    public DeleteLeadHandler(IApplicationDbContext db, ICurrentUser user, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _notify = Guard.AgainstNull(notify);
    }

    public async Task<Unit> Handle(DeleteLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);

        var actingUserId = _user.UserId ?? throw new ForbiddenAccessException();
        var actingName = _user.UserName ?? "An administrator";

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var reason = request.Reason.Trim();
        var now = DateTime.UtcNow;

        lead.DeletedAt = now;
        lead.DeletedByUserId = actingUserId;
        lead.DeletedByName = actingName;
        lead.DeletionReason = reason;

        // The trail stays ON the lead rather than only in the audit log, because the audit log is an
        // administrator's tool and this question gets asked by the closer whose sale disappeared.
        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = lead.AgencyId,
            CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            UserId = actingUserId,
            FromStage = lead.Stage,
            ToStage = lead.Stage,
            Disposition = lead.Disposition,
            Notes = $"Deleted by {actingName}. Reason: {reason}",
            OccurredAt = now,
        });

        // The sale and its commissions go with it. Leaving them behind would keep a deleted lead's
        // revenue in every dashboard and keep paying commission on a policy that has been retired —
        // the figures would silently stop reconciling against the leads they came from.
        var sales = await _db.Sales.Where(s => s.LeadId == lead.Id).ToListAsync(ct);
        var saleIds = sales.Select(s => s.Id).ToList();
        var commissions = saleIds.Count == 0
            ? new List<CommissionEntry>()
            : await _db.CommissionEntries.Where(c => saleIds.Contains(c.SaleId)).ToListAsync(ct);

        var affectedClosers = sales.Select(s => s.CloserUserId).Distinct().ToList();

        _db.CommissionEntries.RemoveRange(commissions);   // → soft delete, via AuditInterceptor
        _db.Sales.RemoveRange(sales);
        _db.Leads.Remove(lead);

        // An explicit entry: the interceptor's automatic "Deleted" row records that it happened, but
        // not WHY, and not that a sale and its commissions went with it. That is the whole substance
        // of this action.
        _db.AuditEntries.Add(new AuditEntry
        {
            AgencyId = lead.AgencyId,
            EntityName = nameof(Lead),
            EntityId = lead.Id.ToString(),
            Action = "LeadDeleted",
            UserId = actingUserId.ToString(),
            UserName = actingName,
            Changes = JsonSerializer.Serialize(new
            {
                reason,
                stage = lead.Stage.ToString(),
                salesRemoved = sales.Count,
                commissionsRemoved = commissions.Count,
                leadName = $"{lead.FirstName} {lead.LastName}".Trim(),
            }),
            IpAddress = _user.IpAddress,
        });

        await _db.SaveChangesAsync(ct);

        await NotifyAffectedAsync(lead, affectedClosers, actingUserId, actingName, reason, ct);
        return Unit.Value;
    }

    /// <summary>
    /// Tell the people whose work this was — the closer who sold it, and whoever owned the lead.
    ///
    /// This is not a courtesy. Their sale and commission have just been withdrawn, and finding that
    /// out from a shortfall in a payroll run is how an administrator's correction becomes a dispute.
    /// Best-effort: a notification failure must not undo a deletion that is already committed.
    /// </summary>
    private async Task NotifyAffectedAsync(
        Lead lead, IReadOnlyList<Guid> closers, Guid actingUserId, string actingName,
        string reason, CancellationToken ct)
    {
        var recipients = closers.ToList();
        if (lead.AssignedUserId is { } owner) recipients.Add(owner);

        var name = $"{lead.FirstName} {lead.LastName}".Trim();
        var title = "A lead you worked was removed";
        var body = $"{actingName} removed {name} and any sale recorded against it. Reason: {reason}";

        foreach (var userId in recipients.Distinct().Where(id => id != actingUserId))
        {
            try
            {
                await _notify.DispatchAsync(
                    new NotificationPayload(lead.AgencyId, userId, title, body, "/leads"),
                    new[] { NotificationChannelType.InApp }, ct);
            }
            catch { /* graceful — the deletion stands either way */ }
        }
    }
}
