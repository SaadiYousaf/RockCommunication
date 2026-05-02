using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class LeadList : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
    public Guid? LeadSourceId { get; set; }
    public bool IsActive { get; set; } = true;
    public int LeadCount { get; set; }
    public string? Description { get; set; }
}

public class LeadListMembership : TenantEntity
{
    public Guid LeadListId { get; set; }
    public Guid LeadId { get; set; }
}

public class LeadImportBatch : TenantEntity
{
    public Guid LeadListId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int DncScrubbed { get; set; }
    public int Errors { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorDetails { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid InitiatedByUserId { get; set; }
}
