using CRM.Application.Common.Integrations;
using CRM.Application.Common.Notifications;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Notifications;

public class EmailNotificationChannel : INotificationChannel
{
    private readonly IEmailProvider _email;
    private readonly AppDbContext _db;
    public NotificationChannelType ChannelType => NotificationChannelType.Email;

    public EmailNotificationChannel(IEmailProvider email, AppDbContext db)
    {
        _email = email;
        _db = db;
    }

    public async Task SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
        if (payload.UserId is null) return;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);
        if (user?.Email is null) return;
        await _email.SendAsync(new EmailMessage(user.Email, payload.Title, payload.Body), ct);
    }
}

public class SmsNotificationChannel : INotificationChannel
{
    private readonly ISmsProvider _sms;
    private readonly AppDbContext _db;
    public NotificationChannelType ChannelType => NotificationChannelType.Sms;

    public SmsNotificationChannel(ISmsProvider sms, AppDbContext db)
    {
        _sms = sms;
        _db = db;
    }

    public async Task SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
        if (payload.UserId is null) return;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);
        if (string.IsNullOrWhiteSpace(user?.PhoneNumber)) return;
        await _sms.SendAsync(new SmsMessage(user.PhoneNumber, $"{payload.Title}: {payload.Body}"), ct);
    }
}
