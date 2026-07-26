using CRM.Application.Common.Workflow;
using CRM.Domain.Common;
using CRM.Infrastructure.Workflow;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-backed implementation of IBackgroundJobScheduler. Enqueues a job that resolves
/// the IWorkflowEngine and executes the rule asynchronously.
/// </summary>
public class HangfireJobScheduler : IBackgroundJobScheduler
{
    private readonly IBackgroundJobClient _jobs;
    public HangfireJobScheduler(IBackgroundJobClient jobs) => _jobs = Guard.AgainstNull(jobs);

    public void Enqueue(Guid ruleId, string payloadJson)
        => _jobs.Enqueue<WorkflowJobRunner>(r => r.RunRuleAsync(ruleId, payloadJson, CancellationToken.None));

    public void EnqueueIn(TimeSpan delay, Guid ruleId, string payloadJson)
        => _jobs.Schedule<WorkflowJobRunner>(r => r.RunRuleAsync(ruleId, payloadJson, CancellationToken.None), delay);
}

public class WorkflowJobRunner
{
    private readonly IWorkflowEngine _engine;
    public WorkflowJobRunner(IWorkflowEngine engine) => _engine = Guard.AgainstNull(engine);
    public Task RunRuleAsync(Guid ruleId, string payloadJson, CancellationToken ct) =>
        _engine.ExecuteRuleAsync(ruleId, payloadJson, ct);
}

/// <summary>
/// Synchronous scheduler used in tests and when Hangfire is disabled. Runs the rule inline.
/// </summary>
public class InProcessJobScheduler : IBackgroundJobScheduler
{
    private readonly IServiceProvider _sp;
    public InProcessJobScheduler(IServiceProvider sp) => _sp = Guard.AgainstNull(sp);

    public void Enqueue(Guid ruleId, string payloadJson)
    {
        _ = Task.Run(async () =>
        {
            // New scope from the ROOT provider (this scheduler is a singleton), so we get a fresh
            // DbContext — never a captured request-scoped one used after its scope disposed.
            using var scope = _sp.CreateScope();
            try
            {
                var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
                await engine.ExecuteRuleAsync(ruleId, payloadJson, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Fire-and-forget: an escaped exception would be an UNOBSERVED task exception
                // (invisible). Log it instead of letting it vanish.
                scope.ServiceProvider.GetService<ILogger<InProcessJobScheduler>>()?
                    .LogError(ex, "Background workflow rule {RuleId} failed", ruleId);
            }
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
