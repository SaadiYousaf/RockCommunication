using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record CallListItemDto(
    Guid Id, Guid LeadId, string LeadName, string LeadPhone,
    Guid AgentUserId, string? AgentName,
    string Provider, string ProviderCallId,
    string Status, string Direction,
    DateTime InitiatedAt, DateTime? AnsweredAt, DateTime? EndedAt,
    int? TalkSeconds, int? WaitSeconds,
    string? RecordingUrl, string? WrapUpCode);

public record PagedCallsResult(IReadOnlyList<CallListItemDto> Items, int Total, int Skip, int Take,
    int AnsweredCount, int VoicemailCount, int AbandonedCount, double AvgTalkSeconds);

public record ListCallsQuery(
    Guid? AgentUserId = null,
    string? Direction = null,
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    string Sort = "initiatedAt-desc",
    int Skip = 0,
    int Take = 50)
    : IRequest<PagedCallsResult>;

public class ListCallsHandler : IRequestHandler<ListCallsQuery, PagedCallsResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public ListCallsHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _identity = Guard.AgainstNull(identity);
    }

    public async Task<PagedCallsResult> Handle(ListCallsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (!_user.IsSuperAdmin && _user.AgencyId is null) throw new ForbiddenAccessException();

        // SuperAdmin (platform operator) sees calls across every agency; tenant users are pinned.
        var q = _db.CallRecords.AsNoTracking().AsQueryable();
        if (!_user.IsSuperAdmin) q = q.Where(c => c.AgencyId == _user.AgencyId);

        // Non-managers see only their own calls; oversight roles (Admin, CEO, Program/Project Manager,
        // TechLead, QAManager, TeamLead, SuperAdmin) see the whole scope. Use the shared AccessScope
        // policy so this can't drift from the leads/records visibility rule and omit a role (a CEO/QA
        // reviewer opening Call History previously saw an empty log).
        if (!_user.IsSuperAdmin && !AccessScope.SeesAllRecords(_user.Roles))
        {
            q = q.Where(c => c.AgentUserId == _user.UserId);
        }

        if (request.AgentUserId is { } uid) q = q.Where(c => c.AgentUserId == uid);
        if (!string.IsNullOrEmpty(request.Direction)) q = q.Where(c => c.Direction == request.Direction);
        if (!string.IsNullOrEmpty(request.Status)) q = q.Where(c => c.Status == request.Status);
        if (request.From is { } f) q = q.Where(c => c.InitiatedAt >= f);
        if (request.To is { } t) q = q.Where(c => c.InitiatedAt < t);

        var total = await q.CountAsync(ct);
        var answered = await q.CountAsync(c => c.AnsweredAt != null, ct);
        // Case-insensitive: statuses are stored lowercase at runtime but PascalCase by the seeder /
        // some dialer webhooks — a case-sensitive match silently under-counts those rows.
        var voicemail = await q.CountAsync(c => c.Status != null && c.Status.ToLower() == "voicemail", ct);
        var abandoned = await q.CountAsync(c => c.Status != null && c.Status.ToLower() == "abandoned", ct);

        var talkData = await q.Where(c => c.AnsweredAt != null && c.EndedAt != null)
            .Select(c => new { c.AnsweredAt, c.EndedAt }).ToListAsync(ct);
        var avgTalk = talkData.Count == 0 ? 0
            : talkData.Average(c => (c.EndedAt!.Value - c.AnsweredAt!.Value).TotalSeconds);

        // SQLite can't translate (EndedAt - AnsweredAt) TimeSpan math, so talkTime-desc is sorted
        // and paged in memory (like avgTalk above); every other sort stays SQL-paged unchanged.
        q = request.Sort switch
        {
            "initiatedAt-asc" => q.OrderBy(c => c.InitiatedAt),
            _ => q.OrderByDescending(c => c.InitiatedAt), // talkTime-desc handled in memory below
        };

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);

        var rawItems = request.Sort == "talkTime-desc"
            ? (await q.Join(_db.Leads.AsNoTracking(),
                    c => c.LeadId, l => l.Id,
                    (c, l) => new
                    {
                        c.Id, c.LeadId, l.FirstName, l.LastName, l.PhoneNumber,
                        c.AgentUserId, c.Provider, c.ProviderCallId,
                        c.Status, c.Direction,
                        c.InitiatedAt, c.AnsweredAt, c.EndedAt,
                        c.RecordingUrl, c.WrapUpCode
                    }).ToListAsync(ct))
                .OrderByDescending(r => r.AnsweredAt is { } a && r.EndedAt is { } e ? (e - a).TotalSeconds : double.MinValue)
                .ThenByDescending(r => r.InitiatedAt)
                .Skip(skip).Take(take).ToList()
            : await q.Skip(skip).Take(take)
                .Join(_db.Leads.AsNoTracking(),
                    c => c.LeadId, l => l.Id,
                    (c, l) => new
                    {
                        c.Id, c.LeadId, l.FirstName, l.LastName, l.PhoneNumber,
                        c.AgentUserId, c.Provider, c.ProviderCallId,
                        c.Status, c.Direction,
                        c.InitiatedAt, c.AnsweredAt, c.EndedAt,
                        c.RecordingUrl, c.WrapUpCode
                    }).ToListAsync(ct);

        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);

        var items = rawItems.Select(r => new CallListItemDto(
            r.Id, r.LeadId,
            $"{r.FirstName} {r.LastName}".Trim(), r.PhoneNumber,
            r.AgentUserId, byId.TryGetValue(r.AgentUserId, out var u) ? u.UserName : null,
            r.Provider, r.ProviderCallId,
            r.Status, r.Direction,
            r.InitiatedAt, r.AnsweredAt, r.EndedAt,
            r.AnsweredAt is { } a && r.EndedAt is { } e ? (int)(e - a).TotalSeconds : (int?)null,
            r.AnsweredAt is { } aw ? (int)(aw - r.InitiatedAt).TotalSeconds : (int?)null,
            r.RecordingUrl, r.WrapUpCode))
            .ToList();

        return new PagedCallsResult(items, total, skip, take,
            answered, voicemail, abandoned, Math.Round(avgTalk, 1));
    }
}
