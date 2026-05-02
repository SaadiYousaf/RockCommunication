using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class Cadence : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public ICollection<CadenceStep> Steps { get; set; } = new List<CadenceStep>();
}

/// <summary>
/// A step in a cadence — runs after `DelayMinutes` from the previous step's completion.
/// StepKind = Call / Sms / Email / Wait / WorkflowAction
/// </summary>
public class CadenceStep : TenantEntity
{
    public Guid CadenceId { get; set; }
    public int Order { get; set; }
    public string StepKind { get; set; } = string.Empty;
    public int DelayMinutes { get; set; }
    public string? ParametersJson { get; set; }
    public bool StopIfContacted { get; set; }
}

/// <summary>
/// One enrolled lead per cadence. Tracks current step + when next step fires.
/// </summary>
public class CadenceEnrollment : TenantEntity
{
    public Guid CadenceId { get; set; }
    public Guid LeadId { get; set; }
    public int CurrentStepOrder { get; set; }
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime NextRunAt { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime? CompletedAt { get; set; }
    public string? StopReason { get; set; }
}
