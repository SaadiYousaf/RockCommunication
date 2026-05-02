using CRM.Application.Common.Workflow;
using CRM.Domain.Entities;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CRM.Infrastructure.Workflow;

public class WorkflowActionRegistry : IWorkflowActionRegistry
{
    private readonly IReadOnlyDictionary<string, IWorkflowAction> _byType;
    public WorkflowActionRegistry(IEnumerable<IWorkflowAction> actions)
    {
        _byType = actions.ToDictionary(a => a.ActionType, StringComparer.OrdinalIgnoreCase);
    }
    public IWorkflowAction Get(string actionType) =>
        _byType.TryGetValue(actionType, out var a) ? a
            : throw new InvalidOperationException($"Unknown workflow action '{actionType}'.");
    public IReadOnlyList<string> AvailableActions => _byType.Keys.ToList();
}

public class WorkflowEngine : IWorkflowEngine
{
    private readonly AppDbContext _db;
    private readonly IWorkflowActionRegistry _registry;
    private readonly ILogger<WorkflowEngine> _logger;
    private readonly IBackgroundJobScheduler _scheduler;

    public WorkflowEngine(AppDbContext db, IWorkflowActionRegistry registry,
        ILogger<WorkflowEngine> logger, IBackgroundJobScheduler scheduler)
    {
        _db = db;
        _registry = registry;
        _logger = logger;
        _scheduler = scheduler;
    }

    public async Task PublishAsync(IWorkflowEvent ev, CancellationToken ct = default)
    {
        var rules = await _db.WorkflowRules
            .Where(r => r.AgencyId == ev.AgencyId && r.IsActive && r.EventType == ev.EventType)
            .Include(r => r.Actions)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        if (rules.Count == 0) return;
        var payload = JsonSerializer.Serialize(new
        {
            ev.EventType, ev.AgencyId, facts = ev.Facts
        });

        foreach (var rule in rules)
        {
            try
            {
                if (!RuleConditionEvaluator.Evaluate(rule.ConditionJson, ev.Facts))
                    continue;

                _scheduler.Enqueue(rule.Id, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow rule {RuleId} match failed", rule.Id);
                if (!rule.ContinueOnError) throw;
            }
        }
    }

    public async Task ExecuteRuleAsync(Guid ruleId, string payloadJson, CancellationToken ct = default)
    {
        var rule = await _db.WorkflowRules.Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null || !rule.IsActive) return;

        var execution = new WorkflowExecution
        {
            AgencyId = rule.AgencyId,
            RuleId = rule.Id,
            EventType = rule.EventType,
            PayloadJson = payloadJson,
            Status = "Running"
        };
        _db.WorkflowExecutions.Add(execution);
        await _db.SaveChangesAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var facts = ParseFacts(doc.RootElement);
            var ev = new ReplayedEvent(rule.EventType, rule.AgencyId, facts);

            foreach (var action in rule.Actions.OrderBy(a => a.Order))
            {
                var impl = _registry.Get(action.ActionType);
                var prms = string.IsNullOrWhiteSpace(action.ParametersJson)
                    ? new Dictionary<string, object?>()
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(action.ParametersJson)
                        ?? new Dictionary<string, object?>();
                await impl.ExecuteAsync(ev, prms, ct);
            }
            execution.Status = "Completed";
            execution.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            execution.Status = "Failed";
            execution.CompletedAt = DateTime.UtcNow;
            execution.Error = ex.ToString();
            await _db.SaveChangesAsync(ct);
            if (!rule.ContinueOnError) throw;
        }
    }

    private static IReadOnlyDictionary<string, object?> ParseFacts(JsonElement root)
    {
        if (!root.TryGetProperty("facts", out var facts) || facts.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>();

        var dict = new Dictionary<string, object?>();
        foreach (var prop in facts.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetDecimal(out var d) ? (object)d : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText()
            };
        }
        return dict;
    }

    private sealed class ReplayedEvent : IWorkflowEvent
    {
        public string EventType { get; }
        public Guid AgencyId { get; }
        public IReadOnlyDictionary<string, object?> Facts { get; }
        public ReplayedEvent(string et, Guid aid, IReadOnlyDictionary<string, object?> f)
        {
            EventType = et; AgencyId = aid; Facts = f;
        }
    }
}

/// <summary>
/// Abstraction so engine doesn't depend on Hangfire directly — production wires Hangfire impl,
/// dev/tests can use synchronous in-process impl.
/// </summary>
public interface IBackgroundJobScheduler
{
    void Enqueue(Guid ruleId, string payloadJson);
    void EnqueueIn(TimeSpan delay, Guid ruleId, string payloadJson);
}
