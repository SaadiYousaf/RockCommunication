using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Admin;

public record AuditEntryDto(
    Guid Id, string EntityName, string EntityId, string Action,
    string? UserId, string? UserName, string? Changes, string? IpAddress,
    DateTime OccurredAt);

public record PagedAuditResult(IReadOnlyList<AuditEntryDto> Items, int Total, int Skip, int Take);

public record ListAuditQuery(
    string? EntityName = null,
    string? EntityId = null,
    string? Action = null,
    string? UserId = null,
    DateTime? After = null,
    DateTime? Before = null,
    string? Search = null,
    int Skip = 0,
    int Take = 50)
    : IRequest<PagedAuditResult>;

public class ListAuditHandler : IRequestHandler<ListAuditQuery, PagedAuditResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public ListAuditHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PagedAuditResult> Handle(ListAuditQuery request, CancellationToken ct)
    {
        EnsureManager();

        var q = _db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.EntityName))
            q = q.Where(a => a.EntityName == request.EntityName);
        if (!string.IsNullOrWhiteSpace(request.EntityId))
            q = q.Where(a => a.EntityId == request.EntityId);
        if (!string.IsNullOrWhiteSpace(request.Action))
            q = q.Where(a => a.Action == request.Action);
        if (!string.IsNullOrWhiteSpace(request.UserId))
            q = q.Where(a => a.UserId == request.UserId);
        if (request.After is { } after) q = q.Where(a => a.OccurredAt >= after);
        if (request.Before is { } before) q = q.Where(a => a.OccurredAt < before);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            q = q.Where(a =>
                a.EntityName.Contains(s) ||
                (a.UserName != null && a.UserName.Contains(s)) ||
                (a.Changes != null && a.Changes.Contains(s)) ||
                (a.IpAddress != null && a.IpAddress.Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);

        var items = await q.OrderByDescending(a => a.OccurredAt)
            .Skip(skip).Take(take)
            .Select(a => new AuditEntryDto(a.Id, a.EntityName, a.EntityId, a.Action,
                a.UserId, a.UserName, a.Changes, a.IpAddress, a.OccurredAt))
            .ToListAsync(ct);

        return new PagedAuditResult(items, total, skip, take);
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager"))
            throw new ForbiddenAccessException();
    }
}

public record DistinctAuditFiltersQuery() : IRequest<DistinctAuditFiltersDto>;

public record DistinctAuditFiltersDto(
    IReadOnlyList<string> EntityNames,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Users);

public class DistinctAuditFiltersHandler : IRequestHandler<DistinctAuditFiltersQuery, DistinctAuditFiltersDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public DistinctAuditFiltersHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<DistinctAuditFiltersDto> Handle(DistinctAuditFiltersQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager"))
            throw new ForbiddenAccessException();

        var entityNames = await _db.AuditEntries.AsNoTracking()
            .Select(a => a.EntityName).Distinct().OrderBy(x => x).ToListAsync(ct);
        var actions = await _db.AuditEntries.AsNoTracking()
            .Select(a => a.Action).Distinct().OrderBy(x => x).ToListAsync(ct);
        var users = await _db.AuditEntries.AsNoTracking()
            .Where(a => a.UserName != null)
            .Select(a => a.UserName!).Distinct().OrderBy(x => x).ToListAsync(ct);

        return new DistinctAuditFiltersDto(entityNames, actions, users);
    }
}
