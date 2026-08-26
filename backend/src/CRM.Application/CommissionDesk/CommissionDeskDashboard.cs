using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CommissionDesk;

/// <summary>Expected performance for one agency (or call centre) in the selected month.</summary>
public record CommissionDeskBreakdownRow(
    Guid Id, string Name,
    int SaleCount,
    decimal Premium,
    /// <summary>Sum of the sales' commission entries — what is actually on the ledger.</summary>
    decimal Funded,
    /// <summary>What the carrier advancing rules say these sales should advance.</summary>
    decimal ExpectedAdvance,
    int ChargedBackCount,
    decimal ChargedBackAmount);

public record CommissionDeskDashboardDto(
    int Year, int Month,
    int TotalSales, decimal TotalPremium, decimal TotalFunded, decimal TotalExpectedAdvance,
    int ChargedBackCount, decimal ChargedBackAmount,
    IReadOnlyList<CommissionDeskBreakdownRow> ByAgency,
    IReadOnlyList<CommissionDeskBreakdownRow> ByCallCenter);

/// <summary>Commission-desk dashboard for a month: expected advance + actuals by agency / call centre.</summary>
public record CommissionDeskDashboardQuery(int Year, int Month) : IRequest<CommissionDeskDashboardDto>;

/// <summary>
/// Aggregates the same data the desk's sales list shows, grouped by agency and by call centre for one
/// month. Reuses <see cref="CommissionDeskHandler"/>'s query via MediatR rather than duplicating the
/// join/rule logic, so "expected advance" is computed in exactly one place.
/// </summary>
public class CommissionDeskDashboardHandler : IRequestHandler<CommissionDeskDashboardQuery, CommissionDeskDashboardDto>
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _user;
    private readonly IApplicationDbContext _db;

    public CommissionDeskDashboardHandler(IMediator mediator, ICurrentUser user, IApplicationDbContext db)
    { _mediator = Guard.AgainstNull(mediator); _user = Guard.AgainstNull(user); _db = Guard.AgainstNull(db); }

    public async Task<CommissionDeskDashboardDto> Handle(CommissionDeskDashboardQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var year = request.Year is >= 2000 and <= 2999 ? request.Year : DateTime.UtcNow.Year;
        var month = request.Month is >= 1 and <= 12 ? request.Month : DateTime.UtcNow.Month;
        var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        // How many sales the month holds — so the page size below covers them all.
        var count = await _db.Sales.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(s => !s.IsDeleted && s.SoldAt >= from && s.SoldAt < to, ct);

        var sales = new List<CommissionSaleDto>(count);
        const int page = 200;                       // the desk query's max take
        for (var skip = 0; skip < count; skip += page)
        {
            var chunk = await _mediator.Send(
                new ListCommissionSalesQuery(From: from, To: to, Skip: skip, Take: page), ct);
            if (chunk.Items.Count == 0) break;
            sales.AddRange(chunk.Items);
        }

        // Decimal sums run in memory (SQLite can't SUM a decimal) — the rows are already materialised.
        CommissionDeskBreakdownRow Row(Guid id, string name, IReadOnlyCollection<CommissionSaleDto> rows) =>
            new(id, name,
                rows.Count,
                rows.Sum(r => r.MonthlyPremium),
                rows.Sum(r => r.FundedAmount),
                rows.Sum(r => r.ExpectedAdvance ?? 0m),
                rows.Count(r => r.Status == nameof(ValidatorStatus.ChargedBack)),
                rows.Where(r => r.Status == nameof(ValidatorStatus.ChargedBack)).Sum(r => r.FundedAmount));

        var byAgency = sales.GroupBy(s => new { s.AgencyId, s.AgencyName })
            .Select(g => Row(g.Key.AgencyId, g.Key.AgencyName, g.ToList()))
            .OrderByDescending(r => r.ExpectedAdvance).ToList();

        var byCallCenter = sales.Where(s => s.CallCenterId is not null)
            .GroupBy(s => new { Id = s.CallCenterId!.Value, Name = s.CallCenterName ?? "" })
            .Select(g => Row(g.Key.Id, g.Key.Name, g.ToList()))
            .OrderByDescending(r => r.ExpectedAdvance).ToList();

        var chargedBack = sales.Where(s => s.Status == nameof(ValidatorStatus.ChargedBack)).ToList();

        return new CommissionDeskDashboardDto(
            year, month,
            sales.Count,
            sales.Sum(s => s.MonthlyPremium),
            sales.Sum(s => s.FundedAmount),
            sales.Sum(s => s.ExpectedAdvance ?? 0m),
            chargedBack.Count,
            chargedBack.Sum(s => s.FundedAmount),
            byAgency, byCallCenter);
    }
}
