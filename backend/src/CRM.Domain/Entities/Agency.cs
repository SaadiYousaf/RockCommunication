using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class Agency : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Highest sale serial issued for this agency. Incremented atomically when a sale is created
    /// so each agency's sales are numbered 1, 2, 3… independently. See Sale.SaleNumber.
    /// </summary>
    public int LastSaleNumber { get; set; }

    public ICollection<Team> Teams { get; set; } = new List<Team>();
    public ICollection<CallCenter> CallCenters { get; set; } = new List<CallCenter>();
}

/// <summary>
/// A call center is an operational unit within an <see cref="Agency"/>. It is the finer
/// data-isolation boundary: customer-pipeline data (leads, sales, applications, calls,
/// commissions) belongs to exactly one call center, and call-center-scoped users only see
/// their own. It is agency-scoped itself (a <see cref="TenantEntity"/>), so agency admins
/// manage only their agency's call centers.
/// </summary>
public class CallCenter : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Team : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Vertical { get; set; }
    public Guid? TeamLeadUserId { get; set; }
}

/// <summary>
/// A platform-level source-IP allowlist entry. Enforced globally by
/// <c>IpAllowlistMiddleware</c> for every tenant, so it is deliberately NOT a
/// <see cref="TenantEntity"/> (no agency FK, no tenant query filter) and is managed by
/// SuperAdmin only. <see cref="AgencyId"/> is optional provenance — which SuperAdmin/agency
/// added it — and never scopes enforcement.
/// </summary>
public class IpAllowlistEntry : BaseEntity
{
    public Guid? AgencyId { get; set; }
    public string CidrOrIp { get; set; } = string.Empty;
    public string? Note { get; set; }
}
