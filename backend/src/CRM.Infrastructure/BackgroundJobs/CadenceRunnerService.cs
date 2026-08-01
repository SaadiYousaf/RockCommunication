using CRM.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// InProcess (non-Hangfire) driver for the cadence drip engine. Without this, cadences only run
/// under the Hangfire provider — under the default InProcess provider CadenceRunnerJob is never
/// invoked, so enrollments sit Active forever and no SMS/email/call step is ever dispatched.
/// Mirrors <see cref="CallbackReminderService"/>: one DI scope per tick, catch+log, waits one
/// interval before the first tick so it never contends with startup/seeding.
/// </summary>
public class CadenceRunnerService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<CadenceRunnerService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public CadenceRunnerService(IServiceProvider sp, ILogger<CadenceRunnerService> logger)
    {
        _sp = Guard.AgainstNull(sp);
        _logger = Guard.AgainstNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = _sp.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<CadenceRunnerJob>();
                await job.RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cadence runner tick failed");
            }
        }
    }
}
