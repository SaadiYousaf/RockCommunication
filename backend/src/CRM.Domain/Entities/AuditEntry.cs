using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class AuditEntry : BaseEntity
{
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class Notification : TenantEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Url { get; set; }
    public bool IsRead { get; set; }
}
