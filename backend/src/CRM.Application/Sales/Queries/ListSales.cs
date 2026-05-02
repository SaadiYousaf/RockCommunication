using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Sales.Commands;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Queries;

public record SaleListItemDto(
    Guid Id, Guid LeadId, string LeadName, string LeadPhone,
    Guid CloserUserId, string? CloserName,
    Guid? ValidatorUserId, string? ValidatorName,
    string Carrier, string? PolicyNumber,
    decimal MonthlyPremium, decimal AnnualPremium,
    DateTime SoldAt, DateTime? ValidatedAt, DateTime? FundedAt,
    bool IsInternalSale, string? InternalSaleReason,
    string Status);

public record PagedSalesResult(IReadOnlyList<SaleListItemDto> Items, int Total, int Skip, int Take,
    decimal TotalPremium, int FundedCount, int ValidatedCount, int PendingCount, int InternalCount);

public record ListSalesQuery(
    Guid? CloserUserId = null,
    string? Carrier = null,
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    string Sort = "soldAt-desc",
    int Skip = 0,
    int Take = 50)
    : IRequest<PagedSalesResult>;

public class ListSalesHandler : IRequestHandler<ListSalesQuery, PagedSalesResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public ListSalesHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = db; _user = user; _identity = identity;
    }

    public async Task<PagedSalesResult> Handle(ListSalesQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var q = _db.Sales.AsNoTracking().Where(s => s.AgencyId == _user.AgencyId);

        // Restrict closers/jr-closers to their own sales
        if (!_user.Roles.Contains("Admin") &&
            !_user.Roles.Contains("ProgramManager") &&
            !_user.Roles.Contains("TeamLead") &&
            !_user.Roles.Contains("Validator"))
        {
            q = q.Where(s => s.CloserUserId == _user.UserId);
        }

        if (request.CloserUserId is { } uid) q = q.Where(s => s.CloserUserId == uid);
        if (!string.IsNullOrEmpty(request.Carrier)) q = q.Where(s => s.Carrier == request.Carrier);
        if (request.From is { } f) q = q.Where(s => s.SoldAt >= f);
        if (request.To is { } t) q = q.Where(s => s.SoldAt < t);
        if (!string.IsNullOrEmpty(request.Status))
        {
            switch (request.Status.ToLowerInvariant())
            {
                case "funded": q = q.Where(s => s.FundedAt != null); break;
                case "validated": q = q.Where(s => s.ValidatedAt != null && s.FundedAt == null); break;
                case "pending": q = q.Where(s => s.ValidatedAt == null); break;
                case "internal": q = q.Where(s => s.IsInternalSale); break;
            }
        }

        var total = await q.CountAsync(ct);
        var totalPremium = await q.SumAsync(s => (decimal?)s.MonthlyPremium, ct) ?? 0m;
        var fundedCount = await q.CountAsync(s => s.FundedAt != null, ct);
        var validatedCount = await q.CountAsync(s => s.ValidatedAt != null && s.FundedAt == null, ct);
        var pendingCount = await q.CountAsync(s => s.ValidatedAt == null, ct);
        var internalCount = await q.CountAsync(s => s.IsInternalSale, ct);

        q = request.Sort switch
        {
            "soldAt-asc" => q.OrderBy(s => s.SoldAt),
            "premium-desc" => q.OrderByDescending(s => s.MonthlyPremium).ThenByDescending(s => s.SoldAt),
            "premium-asc" => q.OrderBy(s => s.MonthlyPremium).ThenByDescending(s => s.SoldAt),
            "carrier-asc" => q.OrderBy(s => s.Carrier).ThenByDescending(s => s.SoldAt),
            _ => q.OrderByDescending(s => s.SoldAt),
        };

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);

        var rawItems = await q.Skip(skip).Take(take)
            .Join(_db.Leads.AsNoTracking(),
                s => s.LeadId, l => l.Id,
                (s, l) => new
                {
                    s.Id, s.LeadId, l.FirstName, l.LastName, l.PhoneNumber,
                    s.CloserUserId, s.ValidatorUserId,
                    s.Carrier, s.PolicyNumber,
                    s.MonthlyPremium, s.AnnualPremium,
                    s.SoldAt, s.ValidatedAt, s.FundedAt,
                    s.IsInternalSale, s.InternalSaleReason
                })
            .ToListAsync(ct);

        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);

        var items = rawItems.Select(r => new SaleListItemDto(
            r.Id, r.LeadId,
            $"{r.FirstName} {r.LastName}".Trim(), r.PhoneNumber,
            r.CloserUserId, byId.TryGetValue(r.CloserUserId, out var c) ? c.UserName : null,
            r.ValidatorUserId, r.ValidatorUserId is { } vid && byId.TryGetValue(vid, out var v) ? v.UserName : null,
            r.Carrier, r.PolicyNumber,
            r.MonthlyPremium, r.AnnualPremium,
            r.SoldAt, r.ValidatedAt, r.FundedAt,
            r.IsInternalSale, r.InternalSaleReason,
            r.FundedAt is not null ? "Funded"
                : r.ValidatedAt is not null ? "Validated"
                : "Pending"))
            .ToList();

        return new PagedSalesResult(items, total, skip, take,
            totalPremium, fundedCount, validatedCount, pendingCount, internalCount);
    }
}
