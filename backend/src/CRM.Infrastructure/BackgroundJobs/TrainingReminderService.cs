using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Sends a "training coming up" reminder to an agency's HR + admins the day before a candidate's
/// scheduled training. Runs hourly; a per-interview marker (<c>TrainingReminderSentAt</c>) dedupes
/// so each scheduled training is announced at most once (rescheduling re-arms it). Best-effort:
/// a failure for one interview never stops the others or crashes the host.
/// </summary>
public class TrainingReminderService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<TrainingReminderService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    /// <summary>How far ahead of the training we start reminding.</summary>
    private static readonly TimeSpan LeadTime = TimeSpan.FromHours(24);

    public TrainingReminderService(IServiceProvider sp, ILogger<TrainingReminderService> logger)
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
            catch (Exception ex) { _logger.LogError(ex, "Training reminder tick failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var now = DateTime.UtcNow;
        var until = now.Add(LeadTime);

        // No agency context in a background job, so bypass the tenant filter (re-add !IsDeleted by hand).
        // Due = a training within the lead-time window that hasn't been reminded yet.
        var due = await db.Interviews.IgnoreQueryFilters()
            .Where(i => !i.IsDeleted && i.TrainingReminderSentAt == null
                     && i.TrainingScheduledAt != null
                     && i.TrainingScheduledAt >= now && i.TrainingScheduledAt <= until)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        // One recipient lookup per agency (not per interview).
        var recipientsByAgency = new Dictionary<Guid, List<Guid>>();
        foreach (var group in due.GroupBy(i => i.AgencyId))
        {
            try
            {
                var users = await identity.ListUsersAsync(group.Key, ct);
                recipientsByAgency[group.Key] = users
                    .Where(u => u.IsActive && (u.Roles.Contains(DomainRoles.HR) || u.Roles.Contains(DomainRoles.Admin)))
                    .Select(u => u.Id)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resolve training-reminder recipients for agency {AgencyId}", group.Key);
            }
        }

        foreach (var interview in due)
        {
            var recipients = recipientsByAgency.TryGetValue(interview.AgencyId, out var r) ? r : null;
            if (recipients is { Count: > 0 })
            {
                var when = interview.TrainingScheduledAt!.Value;
                foreach (var userId in recipients)
                {
                    try
                    {
                        await dispatcher.DispatchAsync(new NotificationPayload(
                                interview.AgencyId, userId,
                                "🎓 Training coming up",
                                $"{interview.CandidateName}'s training is scheduled for {when:f}" +
                                (string.IsNullOrWhiteSpace(interview.PositionOffered) ? "." : $" ({interview.PositionOffered}).")),
                            new[] { NotificationChannelType.InApp, NotificationChannelType.Email },
                            ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send training reminder for interview {InterviewId}", interview.Id);
                    }
                }
            }
            // Stamp regardless so we never spin on an interview whose agency has no HR/admins.
            interview.TrainingReminderSentAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
