using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// A social-media handling report — how many posts were made and how many customer queries were
/// answered on a given day (optionally by a specific employee / platform). Agency-scoped.
/// </summary>
public class SocialMediaReport : TenantEntity
{
    public DateTime Date { get; set; }
    /// <summary>Who handled it (optional link to an employee record).</summary>
    public Guid? EmployeeId { get; set; }
    /// <summary>e.g. "Facebook", "Instagram", "WhatsApp" — free text.</summary>
    public string? Platform { get; set; }
    public int PostsMade { get; set; }
    public int QueriesAnswered { get; set; }
    /// <summary>Optional link to the actual post / campaign (proof of the reported posting).</summary>
    public string? Link { get; set; }
    public string? Notes { get; set; }
}
