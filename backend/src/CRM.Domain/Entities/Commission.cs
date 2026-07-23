using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class CommissionEntry : CallCenterEntity
{
    public Guid SaleId { get; set; }
    public Guid AgentUserId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;
    public bool Paid { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? PayrollRunId { get; set; }
}

public class PayrollRun : TenantEntity
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? ProcessedAt { get; set; }
    public Guid? ProcessedByUserId { get; set; }
}
