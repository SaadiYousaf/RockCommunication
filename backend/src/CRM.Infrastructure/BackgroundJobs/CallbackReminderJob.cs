using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire recurring job (every 1 minute). Looks for callbacks within the next 15 minutes that
/// haven't been reminded yet, dispatches in-app + email notifications, marks them reminded.
/// </summary>
public class CallbackReminderJob
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<CallbackReminderJob> _logger;
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(15);

    public CallbackReminderJob(AppDbContext db, INotificationDispatcher dispatcher, ILogger<CallbackReminderJob> logger)
    {
        _db = Guard.AgainstNull(db);
        _dispatcher = Guard.AgainstNull(dispatcher);
        _logger = Guard.AgainstNull(logger);
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.Add(LeadTime);
        var due = await _db.ScheduledCallbacks
            .Where(c => !c.Completed && !c.Reminded && c.ScheduledFor <= threshold)
            .Take(200)
            .ToListAsync(ct);

        foreach (var cb in due)
        {
            try
            {
                await _dispatcher.DispatchAsync(new NotificationPayload(
                    cb.AgencyId, cb.AssignedUserId,
                    "Upcoming callback",
                    $"You have a callback scheduled at {cb.ScheduledFor:t} ({cb.Reason ?? "no reason given"})"),
                    new[] { NotificationChannelType.InApp, NotificationChannelType.Email }, ct);
                cb.Reminded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remind callback {Id}", cb.Id);
            }
        }
        if (due.Count > 0) await _db.SaveChangesAsync(ct);
    }
}
