using CRM.Application.Common.Exceptions;
using DomainRoles = CRM.Domain.Enums.Roles;
using CRM.Application.Common.Interfaces;
using CRM.Application.Sales.Commands;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Queries;

public record SaleListItemDto(
    Guid Id, int SaleNumber, Guid LeadId, string LeadName, string LeadPhone,
    Guid CloserUserId, string? CloserName,
    Guid? ValidatorUserId, string? ValidatorName,
    Guid? LicenseAgentUserId, string? LicenseAgentName,
    string Carrier, string? PolicyNumber,
    decimal MonthlyPremium, decimal AnnualPremium,
    string? CarrierApproved, decimal? CoverageApproved, decimal? PremiumApproved, string? PlanApproved,
    // Commission the assigned License Agent earned on this sale (their own line, not the sale total).
    decimal? CommissionEarned, string? CallCenterName,
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
    int Take = 50,
    // SuperAdmin only: target a specific agency (the Agency Panel always passes one).
    // Ignored for tenant-scoped callers, who are always pinned to their own agency.
    Guid? AgencyId = null)
    : IRequest<PagedSalesResult>;

public class ListSalesHandler : IRequestHandler<ListSalesQuery, PagedSalesResult>
{
    private const string LicenseAgentRule = "license-agent-approval";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public ListSalesHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _identity = Guard.AgainstNull(identity);
    }

    public async Task<PagedSalesResult> Handle(ListSalesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);

        // Resolve which agency's sales to list. SuperAdmin (who has no agency) must target one
        // explicitly, mirroring the OrgTree/ListUsers "?agencyId honored only for SuperAdmin" rule.
        var isSuperAdmin = _user.Roles.Contains("SuperAdmin");
        Guid agencyId;
        if (isSuperAdmin)
        {
            if (request.AgencyId is not { } aid || aid == Guid.Empty)
                throw new ForbiddenAccessException("An agency must be specified.");
            agencyId = aid;
        }
        else
        {
            if (_user.AgencyId is not { } uaid || uaid == Guid.Empty) throw new ForbiddenAccessException();
            if (request.AgencyId is { } reqAid && reqAid != uaid) throw new ForbiddenAccessException();
            agencyId = uaid;
        }

        var q = _db.Sales.AsNoTracking().Where(s => s.AgencyId == agencyId);

        // Managers/validators/SuperAdmin see the whole agency; everyone else is scoped to the
        // sales that are "theirs". A License Agent is never the closer — their queue is the sales
        // ASSIGNED to them at approval — so scope them by LicenseAgentUserId, not CloserUserId
        // (otherwise their list is always empty even after a sale is assigned to them).
        if (!SalesVisibility.CanSeeWholeAgency(_user.Roles))
        {
            q = SalesVisibility.IsLicenseAgent(_user.Roles)
                ? q.Where(s => s.LicenseAgentUserId == _user.UserId)
                : q.Where(s => s.CloserUserId == _user.UserId);
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
        // SQLite stores decimal as TEXT; a SQL-side SUM materialises back to decimal and throws.
        // Sum in memory over the (already agency/role-scoped) rows instead — same guard as the dashboard.
        var totalPremium = (await q.Select(s => s.MonthlyPremium).ToListAsync(ct)).Sum();
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
                    s.Id, s.SaleNumber, s.LeadId, l.FirstName, l.LastName, l.PhoneNumber,
                    s.CloserUserId, s.ValidatorUserId, s.LicenseAgentUserId, s.CallCenterId,
                    s.Carrier, s.PolicyNumber,
                    s.MonthlyPremium, s.AnnualPremium,
                    s.CarrierApproved, s.CoverageApproved, s.PremiumApproved, s.PlanApproved,
                    s.SoldAt, s.ValidatedAt, s.FundedAt,
                    s.IsInternalSale, s.InternalSaleReason
                })
            .ToListAsync(ct);

        var users = await _identity.ListUsersAsync(agencyId, ct);
        var byId = users.ToDictionary(u => u.Id);
        string? Name(Guid? id) => id is { } g && byId.TryGetValue(g, out var u) ? u.UserName : null;

        // Call-center names — dictionary lookup with fallback (never an inner join, so legacy
        // rows with an empty CallCenterId aren't silently dropped).
        var ccIds = rawItems.Select(r => r.CallCenterId).Distinct().ToList();
        var ccById = (await _db.CallCenters.AsNoTracking()
                .Where(c => ccIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.Name);

        // License-agent commission per sale — sum in memory (EF/SQLite doesn't translate a
        // server-side decimal SUM/GroupBy reliably).
        var saleIds = rawItems.Select(r => r.Id).ToList();
        var commissionBySale = (await _db.CommissionEntries.AsNoTracking()
                .Where(c => saleIds.Contains(c.SaleId) && c.RuleName == LicenseAgentRule)
                .Select(c => new { c.SaleId, c.Amount }).ToListAsync(ct))
            .GroupBy(x => x.SaleId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var items = rawItems.Select(r => new SaleListItemDto(
            r.Id, r.SaleNumber, r.LeadId,
            $"{r.FirstName} {r.LastName}".Trim(), r.PhoneNumber,
            r.CloserUserId, Name(r.CloserUserId),
            r.ValidatorUserId, Name(r.ValidatorUserId),
            r.LicenseAgentUserId, Name(r.LicenseAgentUserId),
            r.Carrier, r.PolicyNumber,
            r.MonthlyPremium, r.AnnualPremium,
            r.CarrierApproved, r.CoverageApproved, r.PremiumApproved, r.PlanApproved,
            commissionBySale.TryGetValue(r.Id, out var comm) ? comm : (decimal?)null,
            ccById.TryGetValue(r.CallCenterId, out var cn) ? cn : null,
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
