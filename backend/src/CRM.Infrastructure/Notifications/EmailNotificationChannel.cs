using CRM.Application.Common.Integrations;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Notifications;

public class EmailNotificationChannel : INotificationChannel
{
    private readonly IEmailProvider _email;
    private readonly AppDbContext _db;
    private readonly ILogger<EmailNotificationChannel> _logger;
    public NotificationChannelType ChannelType => NotificationChannelType.Email;

    public EmailNotificationChannel(IEmailProvider email, AppDbContext db, ILogger<EmailNotificationChannel> logger)
    {
        _email = Guard.AgainstNull(email);
        _db = Guard.AgainstNull(db);
        _logger = Guard.AgainstNull(logger);
    }

    public async Task SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
        Guard.AgainstNull(payload);

        if (payload.UserId is null) return;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);
        if (user?.Email is null) return;
        // The provider swallows send failures and returns Sent=false; surface that on the dispatch
        // path (with user context) so a lost reminder isn't silently counted as delivered.
        var r = await _email.SendAsync(new EmailMessage(user.Email, payload.Title, payload.Body), ct);
        if (!r.Sent)
            _logger.LogWarning("Email notification to user {UserId} not delivered: {Reason}", payload.UserId, r.Reason);
    }
}

public class SmsNotificationChannel : INotificationChannel
{
    private readonly ISmsProvider _sms;
    private readonly AppDbContext _db;
    private readonly ILogger<SmsNotificationChannel> _logger;
    public NotificationChannelType ChannelType => NotificationChannelType.Sms;

    public SmsNotificationChannel(ISmsProvider sms, AppDbContext db, ILogger<SmsNotificationChannel> logger)
    {
        _sms = Guard.AgainstNull(sms);
        _db = Guard.AgainstNull(db);
        _logger = Guard.AgainstNull(logger);
    }

    public async Task SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
        Guard.AgainstNull(payload);

        if (payload.UserId is null) return;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);
        if (string.IsNullOrWhiteSpace(user?.PhoneNumber)) return;
        var r = await _sms.SendAsync(new SmsMessage(user.PhoneNumber, $"{payload.Title}: {payload.Body}"), ct);
        if (!r.Sent)
            _logger.LogWarning("SMS notification to user {UserId} not delivered: {Reason}", payload.UserId, r.Reason);
    }
}
