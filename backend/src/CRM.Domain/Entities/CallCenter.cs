using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class AgentSession : TenantEntity
{
    public Guid UserId { get; set; }
    public DateTime ClockInAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClockOutAt { get; set; }
    public TimeSpan TotalAvailable { get; set; }
    public TimeSpan TotalOnCall { get; set; }
    public TimeSpan TotalBreak { get; set; }
    public TimeSpan TotalWrapUp { get; set; }
}

public class AgentStatusLog : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid? SessionId { get; set; }
    public AgentStatus Status { get; set; }
    public string? Reason { get; set; }
    public DateTime FromAt { get; set; }
    public DateTime? UntilAt { get; set; }
    public Guid? RelatedCallRecordId { get; set; }
    public TimeSpan Duration => (UntilAt ?? DateTime.UtcNow) - FromAt;
}

public class WrapUpCode : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSale { get; set; }
    public bool IsContact { get; set; }
    public bool IsRetry { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DncEntry : TenantEntity
{
    public string PhoneNormalized { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Source { get; set; } = "Internal";
    public DateTime? ExpiresAt { get; set; }
}

public class TcpaConsent : TenantEntity
{
    public Guid LeadId { get; set; }
    public string ConsentText { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

public class Campaign : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? VerticalId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? Notes { get; set; }
    public string DialMode { get; set; } = "Manual";
    public decimal PacingRatio { get; set; } = 1.0m;
    public int MaxRetries { get; set; } = 3;
    public int RetryWaitMinutes { get; set; } = 60;
}

public class LeadSource : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
    public decimal CostPerLead { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Skill : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class AgentSkill : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid SkillId { get; set; }
    public int Proficiency { get; set; } = 1;
}

public class Script : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public WorkflowStage? Stage { get; set; }
    public string? Role { get; set; }
    public Guid? CampaignId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public bool IsBranching { get; set; }
    public string? BranchesJson { get; set; }
}
