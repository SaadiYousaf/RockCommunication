using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Queries;

public record SaleCommissionLineDto(Guid Id, Guid AgentUserId, string? AgentName,
    string RuleName, decimal Amount, bool Paid, DateTime CreatedAt);

public record SaleDetailDto(
    Guid Id, int SaleNumber, string Status, Guid AgencyId,
    // Lead
    Guid LeadId, string LeadName, string LeadPhone, string? LeadEmail, string? LeadState,
    // People
    Guid CloserUserId, string? CloserName,
    Guid? ValidatorUserId, string? ValidatorName,
    Guid? LicenseAgentUserId, string? LicenseAgentName,
    string? CallCenterName,
    // Policy
    string Carrier, string? PolicyNumber,
    decimal MonthlyPremium, decimal AnnualPremium,
    // Validator review outcome
    string ValidatorStatus,
    string? CarrierApproved, string? PlanApproved, decimal? CoverageApproved, decimal? PremiumApproved,
    string? DeclineReason,
    // Banking (Lyons) — nothing sensitive; full account number is never stored
    int BankingCode, string? BankRoutingNumber, string? BankAccountLast4, string? BankName,
    string? LyonsReference, string? BankingNote, bool HasRecording,
    bool IsInternalSale, string? InternalSaleReason,
    // Money
    decimal TotalCommission,
    IReadOnlyList<SaleCommissionLineDto> Commissions,
    // Timeline
    DateTime SoldAt, DateTime? ValidatedAt, DateTime? FundedAt, DateTime CreatedAt,
    // True when this sale's banking result came from the OFFLINE SIMULATOR rather than the live
    // Lyons service. Derived per-sale from the stored reference, not from current configuration, so
    // sales validated while the simulator was active stay correctly marked forever after the real
    // service is connected. The UI must not present a simulated result as a real bank check.
    bool BankValidationSimulated = false);

public record GetSaleDetailQuery(Guid Id) : IRequest<SaleDetailDto>;

public class GetSaleDetailHandler : IRequestHandler<GetSaleDetailQuery, SaleDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public GetSaleDetailHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _identity = Guard.AgainstNull(identity);
    }

    public async Task<SaleDetailDto> Handle(GetSaleDetailQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);

        // The global tenant filter already scopes non-SuperAdmin callers to their own agency; a
        // SuperAdmin bypasses it and can open any sale by id.
        var sale = await _db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Sale), request.Id);

        // Mirror the ListSales visibility rule EXACTLY (shared helper) so list and detail never drift:
        // managers/validators see any sale in the agency; a closer sees their own; and a License Agent
        // sees the sale ASSIGNED to them (they're never the closer). Report NotFound otherwise so an id
        // can't be probed for existence.
        var canView = SalesVisibility.CanSeeWholeAgency(_user.Roles)
            || sale.CloserUserId == _user.UserId
            || (SalesVisibility.IsLicenseAgent(_user.Roles) && sale.LicenseAgentUserId == _user.UserId);
        if (!canView)
            throw new NotFoundException(nameof(Sale), request.Id);

        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == sale.LeadId, ct);

        var commissionRows = await _db.CommissionEntries.AsNoTracking()
            .Where(c => c.SaleId == sale.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.AgentUserId, c.RuleName, c.Amount, c.Paid, c.CreatedAt })
            .ToListAsync(ct);

        var callCenterName = sale.CallCenterId == Guid.Empty ? null
            : await _db.CallCenters.AsNoTracking()
                .Where(c => c.Id == sale.CallCenterId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        var users = await _identity.ListUsersAsync(sale.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);
        string? Name(Guid? id) => id is { } g && byId.TryGetValue(g, out var u) ? u.UserName : null;

        var commissions = commissionRows
            .Select(c => new SaleCommissionLineDto(c.Id, c.AgentUserId, Name(c.AgentUserId),
                c.RuleName, c.Amount, c.Paid, c.CreatedAt))
            .ToList();

        var status = sale.FundedAt is not null ? "Funded"
            : sale.ValidatedAt is not null ? "Validated"
            : "Pending";

        return new SaleDetailDto(
            sale.Id, sale.SaleNumber, status, sale.AgencyId,
            sale.LeadId,
            lead is null ? "(lead removed)" : $"{lead.FirstName} {lead.LastName}".Trim(),
            lead?.PhoneNumber ?? "", lead?.Email, lead?.State,
            sale.CloserUserId, Name(sale.CloserUserId),
            sale.ValidatorUserId, Name(sale.ValidatorUserId),
            sale.LicenseAgentUserId, Name(sale.LicenseAgentUserId),
            callCenterName,
            sale.Carrier, sale.PolicyNumber,
            sale.MonthlyPremium, sale.AnnualPremium,
            sale.ValidatorStatus.ToString(),
            sale.CarrierApproved, sale.PlanApproved, sale.CoverageApproved, sale.PremiumApproved,
            sale.DeclineReason,
            sale.BankingCode, sale.BankRoutingNumber, sale.BankAccountLast4, sale.BankName,
            sale.LyonsReference, sale.BankingNote, !string.IsNullOrEmpty(sale.RecordingUrl),
            sale.IsInternalSale, sale.InternalSaleReason,
            commissions.Sum(c => c.Amount),
            commissions,
            sale.SoldAt, sale.ValidatedAt, sale.FundedAt, sale.CreatedAt,
            BankValidationSimulated: LyonsReferences.IsSimulated(sale.LyonsReference));
    }
}
