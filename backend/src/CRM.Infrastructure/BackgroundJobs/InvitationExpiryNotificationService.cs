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
/// Closes the loop on <see cref="InvitationPolicy"/>: when an onboarding invitation crosses its
/// expiry window without ever being accepted, the invited user can no longer sign in — so this
/// job automatically alerts that agency's admins to resend a fresh link ("new link should be sent
/// by admin"). Runs periodically; only invites that crossed the threshold within the last tick are
/// notified, so each expiry is announced roughly once (a resend re-arms the clock and re-qualifies).
/// </summary>
public class InvitationExpiryNotificationService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<InvitationExpiryNotificationService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public InvitationExpiryNotificationService(IServiceProvider sp, ILogger<InvitationExpiryNotificationService> logger)
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
            catch (Exception ex) { _logger.LogError(ex, "Invitation-expiry notification tick failed"); }

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

        // Pending, never-accepted invites still on their forced first-login password.
        var pending = await db.Users
            .Where(u => u.MustChangePassword && u.InvitationAcceptedAt == null
                     && u.InvitationSentAt != null && u.IsActive)
            .Select(u => new { u.Id, u.UserName, u.Email, u.AgencyId, u.InvitationSentAt })
            .ToListAsync(ct);

        // Just-expired = crossed the window within the last tick, so we announce each expiry ~once.
        var justExpired = pending
            .Where(u => now - u.InvitationSentAt!.Value >= InvitationPolicy.ExpiryWindow
                     && now - u.InvitationSentAt!.Value < InvitationPolicy.ExpiryWindow + Interval)
            .ToList();
        if (justExpired.Count == 0) return;

        // One admin lookup per agency (not per invite).
        foreach (var group in justExpired.GroupBy(u => u.AgencyId))
        {
            List<Guid> adminIds;
            try
            {
                var agencyUsers = await identity.ListUsersAsync(group.Key, ct);
                adminIds = agencyUsers
                    .Where(u => u.IsActive && u.Roles.Contains(DomainRoles.Admin))
                    .Select(u => u.Id)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resolve admins for agency {AgencyId} on invite expiry", group.Key);
                continue;
            }
            if (adminIds.Count == 0) continue;

            foreach (var invite in group)
            {
                foreach (var adminId in adminIds)
                {
                    try
                    {
                        await dispatcher.DispatchAsync(new NotificationPayload(
                                group.Key, adminId,
                                "Invitation expired",
                                $"{invite.UserName} ({invite.Email}) never signed in, so their invitation has expired after " +
                                $"{InvitationPolicy.ExpiryDays} days. Resend a fresh link from User Management."),
                            new[] { NotificationChannelType.InApp, NotificationChannelType.Email },
                            ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to notify admin {AdminId} about expired invite {UserId}", adminId, invite.Id);
                    }
                }
            }
        }
    }
}
