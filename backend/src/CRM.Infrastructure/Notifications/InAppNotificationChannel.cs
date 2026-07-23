using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Application.Common.RealTime;
using CRM.Domain.Common;
using CRM.Domain.Entities;

namespace CRM.Infrastructure.Notifications;

public class InAppNotificationChannel : INotificationChannel
{
    private readonly IApplicationDbContext _db;
    private readonly IAgentNotifier _agent;
    public NotificationChannelType ChannelType => NotificationChannelType.InApp;

    public InAppNotificationChannel(IApplicationDbContext db, IAgentNotifier agent)
    {
        _db = Guard.AgainstNull(db);
        _agent = Guard.AgainstNull(agent);
    }

    public async Task SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
        Guard.AgainstNull(payload);

        if (payload.UserId is null) return;
        _db.Notifications.Add(new Notification
        {
            AgencyId = payload.AgencyId,
            UserId = payload.UserId.Value,
            Title = payload.Title,
            Body = payload.Body,
            Url = payload.Url
        });
        await _db.SaveChangesAsync(ct);

        // Live popup — push to the user's AgentHub group so an open tab toasts immediately.
        // Best-effort: a real-time push failure must not fail the notification write.
        try
        {
            await _agent.PushAsync(payload.UserId.Value, AgentEvents.Notification,
                new { title = payload.Title, body = payload.Body, url = payload.Url }, ct);
        }
        catch { /* advisory only */ }
    }
}
