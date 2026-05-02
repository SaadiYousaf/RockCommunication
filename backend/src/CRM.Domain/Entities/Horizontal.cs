using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// A cross-cutting business dimension that spans verticals (e.g. a region,
/// product horizontal, or shared function). Modelled as a peer of
/// <see cref="Vertical"/> so agencies can organise teams/campaigns along two
/// independent axes.
/// </summary>
public class Horizontal : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
