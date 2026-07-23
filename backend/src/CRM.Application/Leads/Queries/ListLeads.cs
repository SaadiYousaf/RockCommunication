using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

public record PagedLeadsResult(IReadOnlyList<LeadDto> Items, int Total, int Skip, int Take);

public record ListLeadsQuery(
    WorkflowStage? Stage,
    Guid? AssignedUserId,
    LeadDisposition? Disposition = null,
    string? State = null,
    Guid? CampaignId = null,
    Guid? LeadSourceId = null,
    int? MinScore = null,
    DateTime? CreatedAfter = null,
    DateTime? CreatedBefore = null,
    string Sort = "createdAt-desc",
    int Skip = 0,
    int Take = 50)
    : IRequest<PagedLeadsResult>;

public class ListLeadsHandler : IRequestHandler<ListLeadsQuery, PagedLeadsResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public ListLeadsHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<PagedLeadsResult> Handle(ListLeadsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var q = _db.Leads.AsNoTracking().Where(l => l.AgencyId == _user.AgencyId);
        // Front-line agents only browse their own assigned leads; managers see the whole
        // call center. (The global filter already limits rows to the caller's call center.)
        if (!AccessScope.SeesAllRecords(_user.Roles))
            q = q.Where(l => l.AssignedUserId == _user.UserId);
        if (request.Stage is { } stage) q = q.Where(l => l.Stage == stage);
        if (request.AssignedUserId is { } uid) q = q.Where(l => l.AssignedUserId == uid);
        if (request.Disposition is { } d) q = q.Where(l => l.Disposition == d);
        if (!string.IsNullOrWhiteSpace(request.State)) q = q.Where(l => l.State == request.State);
        if (request.CampaignId is { } cid) q = q.Where(l => l.CampaignId == cid);
        if (request.LeadSourceId is { } lsid) q = q.Where(l => l.LeadSourceId == lsid);
        if (request.MinScore is { } ms) q = q.Where(l => l.Score >= ms);
        if (request.CreatedAfter is { } ca) q = q.Where(l => l.CreatedAt >= ca);
        if (request.CreatedBefore is { } cb) q = q.Where(l => l.CreatedAt < cb);

        q = request.Sort switch
        {
            "createdAt-asc" => q.OrderBy(l => l.CreatedAt),
            "score-desc" => q.OrderByDescending(l => l.Score).ThenByDescending(l => l.CreatedAt),
            "score-asc" => q.OrderBy(l => l.Score).ThenByDescending(l => l.CreatedAt),
            "name-asc" => q.OrderBy(l => l.LastName).ThenBy(l => l.FirstName),
            "name-desc" => q.OrderByDescending(l => l.LastName).ThenByDescending(l => l.FirstName),
            "stage-asc" => q.OrderBy(l => l.Stage).ThenByDescending(l => l.CreatedAt),
            _ => q.OrderByDescending(l => l.CreatedAt),
        };

        var total = await q.CountAsync(ct);
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);

        var items = await q.Skip(skip).Take(take)
            .Select(l => new LeadDto(l.Id, l.FirstName, l.LastName, l.PhoneNumber,
                l.Email, l.State, l.Stage, l.Disposition, l.AssignedUserId, l.TeamId,
                l.JornayaVerified, l.CreatedAt))
            .ToListAsync(ct);

        return new PagedLeadsResult(items, total, skip, take);
    }
}
