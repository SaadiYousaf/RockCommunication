namespace CRM.Application.Common.Notifications;

public enum NotificationChannelType { InApp, Email, Sms, Push }

public record NotificationPayload(
    Guid AgencyId,
    Guid? UserId,
    string Title,
    string Body,
    string? Url = null,
    IDictionary<string, string>? Metadata = null);

public interface INotificationChannel
{
    NotificationChannelType ChannelType { get; }
    Task SendAsync(NotificationPayload payload, CancellationToken ct = default);
}

public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationPayload payload, IEnumerable<NotificationChannelType> channels, CancellationToken ct = default);
}
