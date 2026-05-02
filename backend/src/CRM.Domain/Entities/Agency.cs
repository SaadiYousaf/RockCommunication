using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class Agency : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Team> Teams { get; set; } = new List<Team>();
    public ICollection<IpAllowlistEntry> IpAllowlist { get; set; } = new List<IpAllowlistEntry>();
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
