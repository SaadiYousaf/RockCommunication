using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class VoicemailAsset : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public Guid? CampaignId { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
}

public class VoicemailDrop : TenantEntity
{
    public Guid VoicemailAssetId { get; set; }
    public Guid LeadId { get; set; }
    public Guid AgentUserId { get; set; }
    public Guid? CallRecordId { get; set; }
    public string Status { get; set; } = "Queued";
    public DateTime? CompletedAt { get; set; }
}
