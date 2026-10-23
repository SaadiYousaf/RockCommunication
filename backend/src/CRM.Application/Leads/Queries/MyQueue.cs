using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

/// <summary>
/// A closer's own workload.
///
/// <paramref name="Take"/> was 50 against a ceiling of 200, and the order is OLDEST FIRST — so a
/// closer holding more than fifty leads never saw the ones most recently assigned to them. A fresh
/// lead needs ringing today; it sat invisible behind a backlog instead, with nothing on screen to
/// say the list had been cut.
///
/// <paramref name="From"/> inclusive and <paramref name="To"/> exclusive filter on when the lead
/// was created. Both null — the default — means the whole workload, which is deliberate: this is a
/// list of work still owed, not a monthly ledger, and defaulting it to one month would hide a lead
/// from three weeks ago that someone is still waiting on a call about.
/// </summary>
public record MyQueueQuery(
    WorkflowStage? Stage = null, DateTime? From = null, DateTime? To = null, int Take = 2000)
    : IRequest<IReadOnlyList<LeadDto>>;

public class MyQueueHandler : IRequestHandler<MyQueueQuery, IReadOnlyList<LeadDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public MyQueueHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<LeadDto>> Handle(MyQueueQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var q = _db.Leads.Where(l => l.AgencyId == _user.AgencyId && l.AssignedUserId == _user.UserId);
        if (request.Stage is { } s) q = q.Where(l => l.Stage == s);

        // By when the lead arrived. Optional — see the query's own note for why this does not
        // default to a month the way the submission queue does.
        if (request.From is { } from) q = q.Where(l => l.CreatedAt >= from);
        if (request.To is { } to) q = q.Where(l => l.CreatedAt < to);

        // Oldest first: in a queue of work still owed, the longest-waiting lead is the one that
        // needs ringing. The ceiling is now a backstop rather than the thing deciding what a closer
        // can see — the page pages through the result.
        return await q.OrderBy(l => l.CreatedAt)
            .Take(Math.Clamp(request.Take, 1, 5000))
            .Select(l => new LeadDto(l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.Email, l.State,
                l.Stage, l.Disposition, l.AssignedUserId, l.TeamId, l.JornayaVerified, l.CreatedAt))
            .ToListAsync(ct);
    }
}
