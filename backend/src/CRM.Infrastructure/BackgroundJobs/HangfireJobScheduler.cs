using CRM.Application.Common.Workflow;
using CRM.Infrastructure.Workflow;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-backed implementation of IBackgroundJobScheduler. Enqueues a job that resolves
/// the IWorkflowEngine and executes the rule asynchronously.
/// </summary>
public class HangfireJobScheduler : IBackgroundJobScheduler
{
    private readonly IBackgroundJobClient _jobs;
    public HangfireJobScheduler(IBackgroundJobClient jobs) => _jobs = jobs;

    public void Enqueue(Guid ruleId, string payloadJson)
        => _jobs.Enqueue<WorkflowJobRunner>(r => r.RunRuleAsync(ruleId, payloadJson, CancellationToken.None));

    public void EnqueueIn(TimeSpan delay, Guid ruleId, string payloadJson)
        => _jobs.Schedule<WorkflowJobRunner>(r => r.RunRuleAsync(ruleId, payloadJson, CancellationToken.None), delay);
}

public class WorkflowJobRunner
{
    private readonly IWorkflowEngine _engine;
    public WorkflowJobRunner(IWorkflowEngine engine) => _engine = engine;
    public Task RunRuleAsync(Guid ruleId, string payloadJson, CancellationToken ct) =>
        _engine.ExecuteRuleAsync(ruleId, payloadJson, ct);
}

/// <summary>
/// Synchronous scheduler used in tests and when Hangfire is disabled. Runs the rule inline.
/// </summary>
public class InProcessJobScheduler : IBackgroundJobScheduler
{
    private readonly IServiceProvider _sp;
    public InProcessJobScheduler(IServiceProvider sp) => _sp = sp;

    public void Enqueue(Guid ruleId, string payloadJson)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _sp.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
            await engine.ExecuteRuleAsync(ruleId, payloadJson, CancellationToken.None);
        });
    }

    public void EnqueueIn(TimeSpan delay, Guid ruleId, string payloadJson)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            Enqueue(ruleId, payloadJson);
        });
    }
}
