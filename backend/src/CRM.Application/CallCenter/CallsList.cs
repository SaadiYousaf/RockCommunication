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
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var q = _db.CallRecords.AsNoTracking().Where(c => c.AgencyId == _user.AgencyId);

        // Non-managers see only their own calls
        if (!_user.Roles.Contains("Admin") &&
            !_user.Roles.Contains("ProgramManager") &&
            !_user.Roles.Contains("TeamLead"))
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
        var voicemail = await q.CountAsync(c => c.Status == "voicemail", ct);
        var abandoned = await q.CountAsync(c => c.Status == "abandoned", ct);

        var talkData = await q.Where(c => c.AnsweredAt != null && c.EndedAt != null)
            .Select(c => new { c.AnsweredAt, c.EndedAt }).ToListAsync(ct);
        var avgTalk = talkData.Count == 0 ? 0
            : talkData.Average(c => (c.EndedAt!.Value - c.AnsweredAt!.Value).TotalSeconds);

        q = request.Sort switch
        {
            "initiatedAt-asc" => q.OrderBy(c => c.InitiatedAt),
            "talkTime-desc" => q.OrderByDescending(c => c.EndedAt != null && c.AnsweredAt != null
                ? (long)(c.EndedAt!.Value - c.AnsweredAt!.Value).TotalSeconds : 0L)
                .ThenByDescending(c => c.InitiatedAt),
            _ => q.OrderByDescending(c => c.InitiatedAt),
        };

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);

        var rawItems = await q.Skip(skip).Take(take)
            .Join(_db.Leads.AsNoTracking(),
                c => c.LeadId, l => l.Id,
                (c, l) => new
                {
                    c.Id, c.LeadId, l.FirstName, l.LastName, l.PhoneNumber,
                    c.AgentUserId, c.Provider, c.ProviderCallId,
                    c.Status, c.Direction,
                    c.InitiatedAt, c.AnsweredAt, c.EndedAt,
                    c.RecordingUrl, c.WrapUpCode
                })
            .ToListAsync(ct);

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
