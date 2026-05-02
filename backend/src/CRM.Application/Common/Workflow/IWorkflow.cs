namespace CRM.Application.Common.Workflow;

/// <summary>
/// Marker interface — every domain event the workflow engine can react to.
/// Implementations supply named facts the engine can match on.
/// </summary>
public interface IWorkflowEvent
{
    string EventType { get; }
    Guid AgencyId { get; }
    IReadOnlyDictionary<string, object?> Facts { get; }
}

/// <summary>
/// Strategy: every action the workflow engine can run.
/// </summary>
public interface IWorkflowAction
{
    string ActionType { get; }
    Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default);
}

public interface IWorkflowActionRegistry
{
    IWorkflowAction Get(string actionType);
    IReadOnlyList<string> AvailableActions { get; }
}

public interface IWorkflowEngine
{
    Task PublishAsync(IWorkflowEvent ev, CancellationToken ct = default);
    Task ExecuteRuleAsync(Guid ruleId, string payloadJson, CancellationToken ct = default);
}
