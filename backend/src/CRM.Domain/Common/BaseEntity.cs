namespace CRM.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}

public abstract class TenantEntity : BaseEntity
{
    public Guid AgencyId { get; set; }
}

/// <summary>
/// A tenant entity that additionally belongs to a specific <c>CallCenter</c> within its agency.
/// Reads are isolated to the caller's call center; callers with no call-center context
/// (agency-level roles — Admin, managers, CEO) see every call center in their agency.
/// This is the second, finer isolation dimension layered on top of <see cref="TenantEntity.AgencyId"/>.
/// </summary>
public abstract class CallCenterEntity : TenantEntity
{
    public Guid CallCenterId { get; set; }
}
