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
    Task NotifyQueueAsync(Lead lead, string role, string title, string body, string? url, CancellationToken ct = default);
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
                // Agency-level users (no call center) see every queue; call-center agents
                // only get notified for leads in their own call center.
                (u.CallCenterId is null || u.CallCenterId == lead.CallCenterId));

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
}
