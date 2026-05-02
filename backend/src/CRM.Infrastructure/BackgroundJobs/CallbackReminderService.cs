using CRM.Application.Common.Notifications;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

public class CallbackReminderService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<CallbackReminderService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(15);

    public CallbackReminderService(IServiceProvider sp, ILogger<CallbackReminderService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait one interval before the first tick so reminders don't contend with
        // application startup / seeding for the database connection.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Callback reminder tick failed");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var threshold = DateTime.UtcNow.Add(LeadTime);
        var due = await db.ScheduledCallbacks
            .Where(c => !c.Completed && !c.Reminded && c.ScheduledFor <= threshold)
            .Take(100)
            .ToListAsync(ct);

        foreach (var cb in due)
        {
            await dispatcher.DispatchAsync(new NotificationPayload(
                cb.AgencyId, cb.AssignedUserId,
                "Upcoming callback",
                $"You have a callback scheduled at {cb.ScheduledFor:t} ({cb.Reason ?? "no reason given"})"),
                new[] { NotificationChannelType.InApp, NotificationChannelType.Email },
                ct);
            cb.Reminded = true;
        }
        if (due.Count > 0) await db.SaveChangesAsync(ct);
    }
}
