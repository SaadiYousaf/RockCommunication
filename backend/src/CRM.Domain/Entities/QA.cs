using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class QaRubric : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<QaRubricItem> Items { get; set; } = new List<QaRubricItem>();
}

public class QaRubricItem : TenantEntity
{
    public Guid RubricId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int MaxScore { get; set; }
    public int Order { get; set; }
}

public class QaReview : TenantEntity
{
    public Guid LeadId { get; set; }
    public Guid? SaleId { get; set; }
    public Guid AgentUserId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public Guid RubricId { get; set; }
    public decimal TotalScore { get; set; }
    public decimal MaxScore { get; set; }
    public string? Notes { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    public ICollection<QaReviewItem> Items { get; set; } = new List<QaReviewItem>();
}

public class QaReviewItem : TenantEntity
{
    public Guid ReviewId { get; set; }
    public Guid RubricItemId { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
}
