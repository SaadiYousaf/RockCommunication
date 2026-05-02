using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class Vertical : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
