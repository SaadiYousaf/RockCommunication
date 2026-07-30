using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Sends a birthday notification to each employee (via their linked login) on their birthday.
/// Runs hourly; a per-year marker on the employee dedupes so it fires at most once per birthday.
/// </summary>
public class BirthdayNotificationService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<BirthdayNotificationService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public BirthdayNotificationService(IServiceProvider sp, ILogger<BirthdayNotificationService> logger)
    {
        _sp = Guard.AgainstNull(sp);
        _logger = Guard.AgainstNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Birthday notification tick failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var today = DateTime.UtcNow.Date;
        // No agency context in a background job, so bypass the tenant filter (re-add !IsDeleted by hand).
        var candidates = await db.Employees.IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.DateOfBirth != null && e.UserId != null
                && (e.LastBirthdayNotifiedYear == null || e.LastBirthdayNotifiedYear != today.Year))
            .ToListAsync(ct);

        var due = candidates
            .Where(e => e.DateOfBirth!.Value.Month == today.Month && e.DateOfBirth!.Value.Day == today.Day)
            .ToList();

        foreach (var e in due)
        {
            await dispatcher.DispatchAsync(new NotificationPayload(
                    e.AgencyId, e.UserId!.Value,
                    "🎉 Happy Birthday!",
                    $"Wishing you a wonderful birthday, {e.FullName} — from the whole team!"),
                new[] { NotificationChannelType.InApp },
                ct);
            e.LastBirthdayNotifiedYear = today.Year;
        }
        if (due.Count > 0) await db.SaveChangesAsync(ct);
    }
}
