using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record CallSummaryDto(
    Guid Id, Guid LeadId, Guid AgentUserId, string Provider, string ProviderCallId,
    string Status, string Direction,
    DateTime InitiatedAt, DateTime? AnsweredAt, DateTime? EndedAt,
    string? RecordingUrl, string? WrapUpCode, string? Notes);

public record FindCallByProviderQuery(string Provider, string ProviderCallId) : IRequest<CallSummaryDto?>;

public class FindCallByProviderHandler : IRequestHandler<FindCallByProviderQuery, CallSummaryDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public FindCallByProviderHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CallSummaryDto?> Handle(FindCallByProviderQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var c = await _db.CallRecords.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AgencyId == _user.AgencyId
                && c.Provider == request.Provider && c.ProviderCallId == request.ProviderCallId, ct);
        return c is null ? null : new CallSummaryDto(c.Id, c.LeadId, c.AgentUserId, c.Provider,
            c.ProviderCallId, c.Status, c.Direction, c.InitiatedAt, c.AnsweredAt, c.EndedAt,
            c.RecordingUrl, c.WrapUpCode, c.Notes);
    }
}

public record MyRecentCallsQuery(int Take = 50) : IRequest<IReadOnlyList<CallSummaryDto>>;

public class MyRecentCallsHandler : IRequestHandler<MyRecentCallsQuery, IReadOnlyList<CallSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public MyRecentCallsHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<CallSummaryDto>> Handle(MyRecentCallsQuery request, CancellationToken ct)
    {
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.CallRecords
            .Where(c => c.AgencyId == _user.AgencyId && c.AgentUserId == _user.UserId)
            .OrderByDescending(c => c.InitiatedAt)
            .Take(Math.Min(request.Take, 200))
            .Select(c => new CallSummaryDto(c.Id, c.LeadId, c.AgentUserId, c.Provider,
                c.ProviderCallId, c.Status, c.Direction, c.InitiatedAt, c.AnsweredAt, c.EndedAt,
                c.RecordingUrl, c.WrapUpCode, c.Notes))
            .ToListAsync(ct);
    }
}
