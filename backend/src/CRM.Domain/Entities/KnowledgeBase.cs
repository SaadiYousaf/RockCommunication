using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class KnowledgeArticle : TenantEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string? Category { get; set; }
    public bool IsPublished { get; set; }
    public int ViewCount { get; set; }
    public Guid AuthorUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class PublicLeadCaptureEndpoint : TenantEntity
{
    public string Slug { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
    public Guid? LeadSourceId { get; set; }
    public Guid? CadenceId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? AllowedOrigins { get; set; }
    public int LeadCount { get; set; }
}
