using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class WorkflowRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? ConditionJson { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public bool ContinueOnError { get; set; } = true;
    public string? Description { get; set; }
    public ICollection<WorkflowAction> Actions { get; set; } = new List<WorkflowAction>();
}

public class WorkflowAction : TenantEntity
{
    public Guid RuleId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? ParametersJson { get; set; }
    public int Order { get; set; }
}

public class WorkflowExecution : TenantEntity
{
    public Guid RuleId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
}
