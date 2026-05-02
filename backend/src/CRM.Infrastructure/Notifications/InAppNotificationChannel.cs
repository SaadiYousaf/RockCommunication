using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Entities;

namespace CRM.Infrastructure.Notifications;

public class InAppNotificationChannel : INotificationChannel
{
    private readonly IApplicationDbContext _db;
    public NotificationChannelType ChannelType => NotificationChannelType.InApp;

    public InAppNotificationChannel(IApplicationDbContext db) => _db = db;

    public async Task SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
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
    }
}
