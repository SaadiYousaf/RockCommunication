using CRM.Application.Common.Authorization;
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
/// <summary>
/// One badge per meaningful workload. MyLeads and Available can never count the same lead: the first
/// requires an owner, the second requires none. Previously myQueue overlapped both role pools and
/// the submission queue, so a single sold deal incremented two badges.
/// </summary>
public record QueueCountsDto(
    int MyLeads,
    int Callbacks,
    /// <summary>Total waiting in the pools this caller's roles actually work. 0 when they own none.</summary>
    int Available,
    /// <summary>Split of Available, so the screen can label its tabs without a second call.</summary>
    int AvailableToVerify,
    int AvailableToClose,
    int SubmissionQueue);

public record QueueCountsQuery() : IRequest<QueueCountsDto>;

public class QueueCountsHandler : IRequestHandler<QueueCountsQuery, QueueCountsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public QueueCountsHandler(IApplicationDbContext db, ICurrentUser user)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<QueueCountsDto> Handle(QueueCountsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is not { } uid) return new QueueCountsDto(0, 0, 0, 0, 0, 0);

        // Personal workload — leads I own that still need something from me.
        var myLeads = await _db.Leads.CountAsync(LeadQueuePredicates.Mine(uid), ct);
        var callbacks = await _db.ScheduledCallbacks.CountAsync(c => c.AssignedUserId == uid && !c.Completed, ct);

        // Shared pools, counted ONLY for the roles this caller actually works — a badge should be a
        // number they can act on, not a tenant-wide statistic. The global tenant filter already
        // scopes these to their agency/call-centre.
        var myPools = LeadQueuePredicates.PoolsFor(_user.Roles, AccessScope.SeesAllRecords(_user.Roles));
        var availableToVerify = myPools.Contains(WorkflowStage.Fronted)
            ? await _db.Leads.CountAsync(LeadQueuePredicates.Pool(WorkflowStage.Fronted), ct) : 0;
        var availableToClose = myPools.Contains(WorkflowStage.Verified)
            ? await _db.Leads.CountAsync(LeadQueuePredicates.Pool(WorkflowStage.Verified), ct) : 0;

        var isCentral = DomainRoles.IsCentralSubmissionAgent(_user.AgencyId, _user.Roles);
        var pendingSales = _db.Sales.AsQueryable();
        if (isCentral) pendingSales = pendingSales.IgnoreQueryFilters().Where(s => !s.IsDeleted);
        // Exclude terminal-lost outcomes (declined / client-cancelled) — they keep ValidatedAt /
        // FundedAt null but are done, so without this the badge counts them as pending forever.
        var submissionQueue = await pendingSales.CountAsync(s =>
            s.ValidatedAt == null && s.FundedAt == null
            && s.ValidatorStatus != ValidatorStatus.Decline
            && s.ValidatorStatus != ValidatorStatus.ClientCancelled, ct);

        return new QueueCountsDto(myLeads, callbacks,
            availableToVerify + availableToClose, availableToVerify, availableToClose,
            submissionQueue);
    }
}
