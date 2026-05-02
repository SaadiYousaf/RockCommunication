using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// ACD inbound queue. Calls enter via webhook → routed to first available agent matching skill set.
/// </summary>
public class InboundQueue : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? RequiredSkillCode { get; set; }
    public Guid? CampaignId { get; set; }
    public string Strategy { get; set; } = "longest-idle";
    public int MaxWaitSeconds { get; set; } = 120;
    public Guid? OverflowQueueId { get; set; }
    public Guid? VoicemailAssetId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Tracks calls waiting in a queue.
/// </summary>
public class QueuedCall : TenantEntity
{
    public Guid InboundQueueId { get; set; }
    public string FromPhone { get; set; } = string.Empty;
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAt { get; set; }
    public DateTime? AbandonedAt { get; set; }
    public Guid? AnsweredByUserId { get; set; }
    public string Status { get; set; } = "Waiting";
    public string Provider { get; set; } = string.Empty;
    public string ProviderCallId { get; set; } = string.Empty;
}

public class IvrMenu : TenantEntity
{
    public Guid InboundQueueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Greeting { get; set; } = string.Empty;
    public string? GreetingAudioUrl { get; set; }
    public ICollection<IvrOption> Options { get; set; } = new List<IvrOption>();
}

public class IvrOption : TenantEntity
{
    public Guid IvrMenuId { get; set; }
    public string DigitOrSpeech { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ActionType { get; set; } = "RouteToQueue";
    public string? ActionTargetId { get; set; }
    public int Order { get; set; }
}
