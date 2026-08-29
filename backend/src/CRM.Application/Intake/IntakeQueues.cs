using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Intake;

/// <summary>A lead as shown in the Verifier / Closer work queues.</summary>
public record IntakeQueueItem(
    Guid Id,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? Email,
    string? State,
    string? City,
    string? MaritalStatus,
    int? AgeYears,
    WorkflowStage Stage,
    VerifierStatus VerifierStatus,
    bool HasApplication,
    DateTime CreatedAt,
    // Lead priority score — lets a worker triage the hottest lead, not just the oldest.
    decimal Score);

/// <summary>Leads awaiting verification (fronted). Shown in the Verifier queue.</summary>
public record VerifierQueueQuery(int Take = 100) : IRequest<IReadOnlyList<IntakeQueueItem>>;

/// <summary>Leads verified and awaiting a closer. Shown in the Closer queue.</summary>
public record CloserQueueQuery(int Take = 100) : IRequest<IReadOnlyList<IntakeQueueItem>>;

/// <summary>
/// Everything waiting to be claimed in the pools THIS caller's roles work — one screen instead of a
/// per-role queue each, so nobody has to know which internal queue their job maps to.
/// </summary>
public record AvailableLeadsQuery(int Take = 100) : IRequest<IReadOnlyList<AvailableLeadItem>>;

/// <summary>A pooled lead, plus which pool it is in so one list can be tabbed by stage.</summary>
public record AvailableLeadItem(IntakeQueueItem Lead, WorkflowStage Stage, string StageLabel);

public class IntakeQueueHandler :
    IRequestHandler<VerifierQueueQuery, IReadOnlyList<IntakeQueueItem>>,
    IRequestHandler<CloserQueueQuery, IReadOnlyList<IntakeQueueItem>>,
    IRequestHandler<AvailableLeadsQuery, IReadOnlyList<AvailableLeadItem>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public IntakeQueueHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public Task<IReadOnlyList<IntakeQueueItem>> Handle(VerifierQueueQuery request, CancellationToken ct)
        => QueueAsync(WorkflowStage.Fronted, request.Take, ct);

    public Task<IReadOnlyList<IntakeQueueItem>> Handle(CloserQueueQuery request, CancellationToken ct)
        => QueueAsync(WorkflowStage.Verified, request.Take, ct);

    /// <summary>
    /// The pools are derived from which role owns each stage rather than from a hardcoded role
    /// check, so a JrCloser — identical permissions to a Closer — finally has a queue at all, and a
    /// caller who works no pool gets an empty list rather than a 403.
    /// </summary>
    public async Task<IReadOnlyList<AvailableLeadItem>> Handle(AvailableLeadsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var pools = Queues.LeadQueuePredicates.PoolsFor(
            _user.Roles, Common.Authorization.AccessScope.SeesAllRecords(_user.Roles));
        if (pools.Count == 0) return Array.Empty<AvailableLeadItem>();

        var result = new List<AvailableLeadItem>();
        foreach (var stage in pools)
        {
            var rows = await QueueAsync(stage, request.Take, ct);
            var label = stage == WorkflowStage.Fronted ? "To verify" : "To close";
            result.AddRange(rows.Select(r => new AvailableLeadItem(r, stage, label)));
        }
        return result;
    }

    private async Task<IReadOnlyList<IntakeQueueItem>> QueueAsync(WorkflowStage stage, int take, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        return await _db.Leads
            // A pooled lead is BY DEFINITION unclaimed. Filtering on stage alone made this list
            // "every verified lead in the agency" — including leads already belonging to a named
            // person, which is why the same lead appeared here and in its owner's My Leads, and why
            // every closer saw everyone else's work as claimable.
            .Where(l => l.AgencyId == _user.AgencyId && l.Stage == stage && l.AssignedUserId == null)
            // Newest first — consistent with the Submission queue, and a just-arrived lead is at the
            // top. The "Waiting" chip still flags the stale ones so they aren't forgotten.
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Min(take, 200))
            .Select(l => new IntakeQueueItem(
                l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.Email, l.State, l.City,
                l.MaritalStatus, l.AgeYears, l.Stage, l.VerifierStatus,
                _db.LeadApplications.Any(a => a.LeadId == l.Id), l.CreatedAt, l.Score))
            .ToListAsync(ct);
    }
}
