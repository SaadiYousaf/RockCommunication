using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class AgencyCommissionConfig : TenantEntity
{
    public string RuleName { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public decimal? Threshold { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Notes { get; set; }
}
