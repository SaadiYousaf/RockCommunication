using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class AppModule : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RoutePath { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
}

public class RoleModule : BaseEntity
{
    public Guid RoleId { get; set; }
    public Guid ModuleId { get; set; }
}
