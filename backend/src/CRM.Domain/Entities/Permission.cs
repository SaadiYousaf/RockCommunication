using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public class LeadScoringRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string FactKey { get; set; } = string.Empty;
    public string ComparisonOp { get; set; } = "eq";
    public string? CompareValue { get; set; }
    public int Points { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 100;
}
