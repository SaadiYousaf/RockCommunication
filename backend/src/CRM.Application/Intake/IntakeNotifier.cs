using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;

namespace CRM.Application.Intake;

/// <summary>
/// Notifies the users of the role that just received a lead/sale in its queue. Call-center
/// aware: only agents in the lead's call center (plus agency-level users who see everything)
/// are notified. Best-effort — a notification failure never blocks the pipeline transition.
/// </summary>
public interface IIntakeNotifier
{
    /// <summary>Notify every user of a role who owns the lead's queue (call-center aware).</summary>
    Task NotifyQueueAsync(Lead lead, string role, string title, string body, string? url, CancellationToken ct = default);

    /// <summary>Notify ONE specific user — e.g. the assignee of a lead or callback. Best-effort.</summary>
    Task NotifyUserAsync(Guid agencyId, Guid userId, string title, string body, string? url, CancellationToken ct = default);
}

public class IntakeNotifier : IIntakeNotifier
{
    private readonly IIdentityService _identity;
    private readonly INotificationDispatcher _dispatcher;

    public IntakeNotifier(IIdentityService identity, INotificationDispatcher dispatcher)
    {
        _identity = Guard.AgainstNull(identity);
        _dispatcher = Guard.AgainstNull(dispatcher);
    }

    public async Task NotifyQueueAsync(Lead lead, string role, string title, string body, string? url, CancellationToken ct = default)
    {
        Guard.AgainstNull(lead);
        try
        {
            var users = await _identity.ListUsersAsync(lead.AgencyId, ct);
            var targets = users.Where(u =>
                (u.IsActive) &&
                u.Roles.Contains(role) &&
                // A lead with NO specific call center (Guid.Empty) is agency-wide, so notify EVERY
                // user of the role — otherwise, if the closers are pinned to a call center, the alert
                // is silently dropped and nobody is told. Agency-level users (null) always see all;
                // call-center agents otherwise get only their own call center's leads.
                (lead.CallCenterId == Guid.Empty || u.CallCenterId is null || u.CallCenterId == lead.CallCenterId));

            foreach (var u in targets)
                await _dispatcher.DispatchAsync(
                    new NotificationPayload(lead.AgencyId, u.Id, title, body, url),
                    new[] { NotificationChannelType.InApp }, ct);
        }
        catch
        {
            // Swallow — notifications are advisory and must not fail the stage transition.
        }
    }

    public async Task NotifyUserAsync(Guid agencyId, Guid userId, string title, string body, string? url, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) return;
        try
        {
            await _dispatcher.DispatchAsync(
                new NotificationPayload(agencyId, userId, title, body, url),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch
        {
            // Advisory only — never fail the assignment because a notification couldn't be sent.
        }
    }
}
