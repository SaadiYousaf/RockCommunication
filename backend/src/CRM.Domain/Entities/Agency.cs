using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class Agency : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Team> Teams { get; set; } = new List<Team>();
    public ICollection<CallCenter> CallCenters { get; set; } = new List<CallCenter>();
    public ICollection<IpAllowlistEntry> IpAllowlist { get; set; } = new List<IpAllowlistEntry>();
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

public class IpAllowlistEntry : TenantEntity
{
    public string CidrOrIp { get; set; } = string.Empty;
    public string? Note { get; set; }
}
