using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Intake;
using CRM.Domain.Common;
using CRM.Domain.Constants;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Commands;

/// <summary>Take an unclaimed lead out of the shared pool and make it yours.</summary>
public record ClaimLeadCommand(Guid LeadId) : IRequest<Unit>;

/// <summary>Put a lead you own back into the pool for its stage, so someone else can pick it up.</summary>
public record ReleaseLeadCommand(Guid LeadId) : IRequest<Unit>;

/// <summary>
/// Claim and release — the two events that were missing entirely.
///
/// Before this, nothing a user could do from the pool screen ever set an owner, so a lead had no way
/// OUT of the pool and no way IN to anyone's personal list. Two closers could work the same lead and
/// only discover the collision at submit, after typing a customer's banking details.
/// </summary>
public class ClaimLeadHandler : IRequestHandler<ClaimLeadCommand, Unit>, IRequestHandler<ReleaseLeadCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIntakeNotifier _notifier;

    public ClaimLeadHandler(IApplicationDbContext db, ICurrentUser user, IIntakeNotifier notifier)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _notifier = Guard.AgainstNull(notifier);
    }

    public async Task<Unit> Handle(ClaimLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is not { } uid || _user.AgencyId is not { } agencyId) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == agencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        // Only a pooled stage can be claimed — claiming a Funded lead would mean nothing.
        if (LeadStagePolicy.QueueOwnerRole(lead.Stage) is null)
            throw new ConflictException("This lead isn't waiting in a queue.");

        // THE RACE GUARD. One UPDATE … WHERE AssignedUserId IS NULL, so the database decides the
        // winner. Read-then-write would let two agents both pass the check and both "win".
        var claimed = await _db.Leads
            .Where(l => l.Id == request.LeadId && l.AgencyId == agencyId && l.AssignedUserId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.AssignedUserId, uid)
                .SetProperty(l => l.UpdatedAt, DateTime.UtcNow), ct);

        // Zero rows means somebody claimed it between the read and the update. Tell the loser now,
        // before they have typed anything, rather than at submit.
        if (claimed == 0) throw new ConflictException("Another agent already claimed this lead.");

        await WriteActivityAsync(lead, uid, "Claimed from the shared queue.", ct);
        return Unit.Value;
    }

    public async Task<Unit> Handle(ReleaseLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is not { } uid || _user.AgencyId is not { } agencyId) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == agencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        if (LeadStagePolicy.QueueOwnerRole(lead.Stage) is not { } ownerRole)
            throw new ConflictException("This lead isn't at a stage that has a shared queue.");

        // Releasing someone else's lead is a reassignment, not a release — that goes through the
        // assign path so the displaced owner is told.
        if (lead.AssignedUserId != uid)
            throw new ForbiddenAccessException("You can only release a lead assigned to you.");

        lead.AssignedUserId = null;
        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteActivityAsync(lead, uid, "Released back to the shared queue.", ct);

        // Releasing creates work for other people, so they are told. (Claiming takes work off a
        // shared pile and costs nobody anything, so it stays silent.)
        await _notifier.NotifyQueueAsync(lead, ownerRole, "Lead back in your queue",
            $"{lead.FirstName} {lead.LastName} — {lead.PhoneNumber} is available to claim.",
            AppConstants.QueueRoutes.Available, ct);

        return Unit.Value;
    }

    /// <summary>
    /// Ownership changes had no trace anywhere — assignment wrote no activity and did not even touch
    /// UpdatedAt, so "who had this and when?" was unanswerable. A same-stage activity row is the
    /// lightest way to record it without inventing a new table.
    /// </summary>
    private async Task WriteActivityAsync(Lead lead, Guid uid, string note, CancellationToken ct)
    {
        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = lead.AgencyId,
            CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            UserId = uid,
            FromStage = lead.Stage,
            ToStage = lead.Stage,
            Disposition = lead.Disposition,
            Notes = note,
        });
        await _db.SaveChangesAsync(ct);
    }
}
