using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.QA;

public record QaReviewSummaryDto(
    Guid Id, Guid LeadId, Guid AgentUserId, Guid ReviewerUserId, Guid RubricId,
    decimal TotalScore, decimal MaxScore, decimal Percentage, string? Notes, DateTime ReviewedAt);

public record ListQaReviewsQuery(Guid? AgentUserId, DateTime? From, DateTime? To, int Take = 50)
    : IRequest<IReadOnlyList<QaReviewSummaryDto>>;

public class ListQaReviewsHandler : IRequestHandler<ListQaReviewsQuery, IReadOnlyList<QaReviewSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public ListQaReviewsHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<QaReviewSummaryDto>> Handle(ListQaReviewsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var q = _db.QaReviews.Where(r => r.AgencyId == _user.AgencyId);
        if (request.AgentUserId is { } a) q = q.Where(r => r.AgentUserId == a);
        if (request.From is { } f) q = q.Where(r => r.ReviewedAt >= f);
        if (request.To is { } t) q = q.Where(r => r.ReviewedAt < t);

        // SQLite stores decimal as TEXT and can't evaluate the % divide in SQL — pull the rows first,
        // then compute the percentage in memory (same guard the AgentScorecard/dashboard/sales use).
        var rows = await q.OrderByDescending(r => r.ReviewedAt)
            .Take(Math.Clamp(request.Take, 1, 200))   // Clamp both ends: a negative Take becomes LIMIT -1 (unbounded) on SQLite.
            .Select(r => new { r.Id, r.LeadId, r.AgentUserId, r.ReviewerUserId, r.RubricId, r.TotalScore, r.MaxScore, r.Notes, r.ReviewedAt })
            .ToListAsync(ct);
        return rows.Select(r => new QaReviewSummaryDto(r.Id, r.LeadId, r.AgentUserId, r.ReviewerUserId,
            r.RubricId, r.TotalScore, r.MaxScore,
            r.MaxScore == 0 ? 0 : Math.Round(r.TotalScore / r.MaxScore * 100m, 2),
            r.Notes, r.ReviewedAt)).ToList();
    }
}

public record AgentScorecardDto(Guid AgentUserId, int ReviewCount, decimal AvgPercentage, decimal AvgScore);

public record AgentScorecardQuery(DateTime From, DateTime To)
    : IRequest<IReadOnlyList<AgentScorecardDto>>;

public class AgentScorecardHandler : IRequestHandler<AgentScorecardQuery, IReadOnlyList<AgentScorecardDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public AgentScorecardHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<AgentScorecardDto>> Handle(AgentScorecardQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        // SQLite can't AVG the TEXT-stored decimal scores — group + average in memory.
        var rows = (await _db.QaReviews
            .Where(r => r.AgencyId == _user.AgencyId && r.ReviewedAt >= request.From && r.ReviewedAt < request.To)
            .Select(r => new { r.AgentUserId, r.MaxScore, r.TotalScore })
            .ToListAsync(ct))
            .GroupBy(r => r.AgentUserId)
            .Select(g => new
            {
                AgentUserId = g.Key,
                Count = g.Count(),
                AvgPct = g.Average(r => r.MaxScore == 0 ? 0 : r.TotalScore / r.MaxScore * 100m),
                AvgScore = g.Average(r => r.TotalScore)
            }).ToList();
        return rows.Select(r => new AgentScorecardDto(r.AgentUserId, r.Count,
            Math.Round(r.AvgPct, 2), Math.Round(r.AvgScore, 2))).ToList();
    }
}
