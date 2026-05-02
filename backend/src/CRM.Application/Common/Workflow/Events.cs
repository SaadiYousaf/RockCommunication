namespace CRM.Application.Common.Workflow;

public static class WorkflowEventTypes
{
    public const string LeadCreated = "lead.created";
    public const string LeadStageChanged = "lead.stage-changed";
    public const string CallCompleted = "call.completed";
    public const string SaleClosed = "sale.closed";
    public const string SaleValidated = "sale.validated";
    public const string SaleFunded = "sale.funded";
    public const string CallbackDue = "callback.due";
}

public class LeadCreatedEvent : IWorkflowEvent
{
    public string EventType => WorkflowEventTypes.LeadCreated;
    public Guid AgencyId { get; init; }
    public Guid LeadId { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string? State { get; init; }
    public string? Source { get; init; }
    public Guid? CampaignId { get; init; }
    public int Score { get; init; }

    public IReadOnlyDictionary<string, object?> Facts => new Dictionary<string, object?>
    {
        ["leadId"] = LeadId, ["phone"] = Phone, ["state"] = State,
        ["source"] = Source, ["campaignId"] = CampaignId, ["score"] = Score
    };
}

public class CallCompletedEvent : IWorkflowEvent
{
    public string EventType => WorkflowEventTypes.CallCompleted;
    public Guid AgencyId { get; init; }
    public Guid CallId { get; init; }
    public Guid LeadId { get; init; }
    public Guid AgentUserId { get; init; }
    public string? WrapUpCode { get; init; }
    public TimeSpan? TalkTime { get; init; }
    public string Direction { get; init; } = "Outbound";

    public IReadOnlyDictionary<string, object?> Facts => new Dictionary<string, object?>
    {
        ["callId"] = CallId, ["leadId"] = LeadId, ["agentId"] = AgentUserId,
        ["wrapUpCode"] = WrapUpCode, ["talkTimeSeconds"] = TalkTime?.TotalSeconds,
        ["direction"] = Direction
    };
}

public class SaleClosedEvent : IWorkflowEvent
{
    public string EventType => WorkflowEventTypes.SaleClosed;
    public Guid AgencyId { get; init; }
    public Guid SaleId { get; init; }
    public Guid LeadId { get; init; }
    public Guid CloserUserId { get; init; }
    public string Carrier { get; init; } = string.Empty;
    public decimal MonthlyPremium { get; init; }
    public bool IsInternalSale { get; init; }

    public IReadOnlyDictionary<string, object?> Facts => new Dictionary<string, object?>
    {
        ["saleId"] = SaleId, ["leadId"] = LeadId, ["closerId"] = CloserUserId,
        ["carrier"] = Carrier, ["monthlyPremium"] = MonthlyPremium,
        ["isInternal"] = IsInternalSale
    };
}

public class LeadStageChangedEvent : IWorkflowEvent
{
    public string EventType => WorkflowEventTypes.LeadStageChanged;
    public Guid AgencyId { get; init; }
    public Guid LeadId { get; init; }
    public string FromStage { get; init; } = string.Empty;
    public string ToStage { get; init; } = string.Empty;
    public Guid AgentUserId { get; init; }

    public IReadOnlyDictionary<string, object?> Facts => new Dictionary<string, object?>
    {
        ["leadId"] = LeadId, ["fromStage"] = FromStage, ["toStage"] = ToStage,
        ["agentId"] = AgentUserId
    };
}
