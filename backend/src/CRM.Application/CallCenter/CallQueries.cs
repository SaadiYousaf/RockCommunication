using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record CallSummaryDto(
    Guid Id, Guid LeadId, Guid AgentUserId, string Provider, string ProviderCallId,
    string Status, string Direction,
    DateTime InitiatedAt, DateTime? AnsweredAt, DateTime? EndedAt,
    string? RecordingUrl, string? WrapUpCode, string? Notes,
    string? LeadName = null, string? LeadPhone = null);

public record FindCallByProviderQuery(string Provider, string ProviderCallId) : IRequest<CallSummaryDto?>;

public class FindCallByProviderHandler : IRequestHandler<FindCallByProviderQuery, CallSummaryDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public FindCallByProviderHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<CallSummaryDto?> Handle(FindCallByProviderQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var c = await _db.CallRecords.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AgencyId == _user.AgencyId
                && c.Provider == request.Provider && c.ProviderCallId == request.ProviderCallId, ct);
        if (c is null) return null;
        var lead = await _db.Leads.AsNoTracking()
            .Where(l => l.Id == c.LeadId)
            .Select(l => new { l.FirstName, l.LastName, l.PhoneNumber })
            .FirstOrDefaultAsync(ct);
        return new CallSummaryDto(c.Id, c.LeadId, c.AgentUserId, c.Provider,
            c.ProviderCallId, c.Status, c.Direction, c.InitiatedAt, c.AnsweredAt, c.EndedAt,
            c.RecordingUrl, c.WrapUpCode, c.Notes,
            lead is null ? null : $"{lead.FirstName} {lead.LastName}".Trim(),
            lead?.PhoneNumber);
    }
}

public record MyRecentCallsQuery(int Take = 50) : IRequest<IReadOnlyList<CallSummaryDto>>;

public class MyRecentCallsHandler : IRequestHandler<MyRecentCallsQuery, IReadOnlyList<CallSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public MyRecentCallsHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<CallSummaryDto>> Handle(MyRecentCallsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var calls = await _db.CallRecords.AsNoTracking()
            .Where(c => c.AgencyId == _user.AgencyId && c.AgentUserId == _user.UserId)
            .OrderByDescending(c => c.InitiatedAt)
            .Take(Math.Min(request.Take, 200))
            .Select(c => new CallSummaryDto(c.Id, c.LeadId, c.AgentUserId, c.Provider,
                c.ProviderCallId, c.Status, c.Direction, c.InitiatedAt, c.AnsweredAt, c.EndedAt,
                c.RecordingUrl, c.WrapUpCode, c.Notes))
            .ToListAsync(ct);

        // Resolve human-readable lead name/phone in one batched query (no N+1)
        // so the UI can show "Outbound call to Jane Doe" instead of a raw call id.
        var leadIds = calls.Select(c => c.LeadId).Distinct().ToList();
        var leads = await _db.Leads.AsNoTracking()
            .Where(l => leadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.FirstName, l.LastName, l.PhoneNumber })
            .ToDictionaryAsync(l => l.Id, ct);

        return calls.Select(c => leads.TryGetValue(c.LeadId, out var l)
            ? c with { LeadName = $"{l.FirstName} {l.LastName}".Trim(), LeadPhone = l.PhoneNumber }
            : c).ToList();
    }
}
