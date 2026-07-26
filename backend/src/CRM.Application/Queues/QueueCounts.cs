using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using DomainRoles = CRM.Domain.Enums.Roles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Queues;

/// <summary>
/// Live "how much work is waiting" counts for the signed-in user, so the sidebar can show an
/// "N new" badge on each queue. Per-user queues (My Queue, Callbacks) filter by the caller;
/// the shared role pools (Verifier/Closer/Submission) are tenant-scoped by the global filter.
/// </summary>
public record QueueCountsDto(int MyQueue, int Callbacks, int VerifierQueue, int CloserQueue, int SubmissionQueue);

public record QueueCountsQuery() : IRequest<QueueCountsDto>;

public class QueueCountsHandler : IRequestHandler<QueueCountsQuery, QueueCountsDto>
{
    private static readonly WorkflowStage[] ActiveMine =
    {
        WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified,
        WorkflowStage.JrClosed, WorkflowStage.Closed, WorkflowStage.Followup,
    };

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public QueueCountsHandler(IApplicationDbContext db, ICurrentUser user)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<QueueCountsDto> Handle(QueueCountsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is not { } uid) return new QueueCountsDto(0, 0, 0, 0, 0);

        // Personal queues — scoped to the caller (works for any role, no tenant edge cases).
        var myQueue = await _db.Leads.CountAsync(l => l.AssignedUserId == uid && ActiveMine.Contains(l.Stage), ct);
        var callbacks = await _db.ScheduledCallbacks.CountAsync(c => c.AssignedUserId == uid && !c.Completed, ct);

        // Shared role pools — the global tenant filter already scopes these to the caller's
        // agency/call-center. A central Submission Agent reads cross-agency; count all pending sales.
        var verifierQueue = await _db.Leads.CountAsync(l => l.Stage == WorkflowStage.Fronted, ct);
        var closerQueue = await _db.Leads.CountAsync(l => l.Stage == WorkflowStage.Verified, ct);

        var isCentral = DomainRoles.IsCentralSubmissionAgent(_user.AgencyId, _user.Roles);
        var pendingSales = _db.Sales.AsQueryable();
        if (isCentral) pendingSales = pendingSales.IgnoreQueryFilters().Where(s => !s.IsDeleted);
        var submissionQueue = await pendingSales.CountAsync(s => s.ValidatedAt == null && s.FundedAt == null, ct);

        return new QueueCountsDto(myQueue, callbacks, verifierQueue, closerQueue, submissionQueue);
    }
}
