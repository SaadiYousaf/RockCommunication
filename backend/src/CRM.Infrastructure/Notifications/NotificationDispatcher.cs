using CRM.Application.Common.Notifications;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Notifications;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly IDictionary<NotificationChannelType, INotificationChannel> _channels;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IEnumerable<INotificationChannel> channels, ILogger<NotificationDispatcher> logger)
    {
        _channels = channels.ToDictionary(c => c.ChannelType);
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationPayload payload, IEnumerable<NotificationChannelType> channels, CancellationToken ct = default)
    {
        foreach (var ch in channels.Distinct())
        {
            if (!_channels.TryGetValue(ch, out var channel))
            {
                _logger.LogWarning("No channel registered for {Channel}", ch);
                continue;
            }

            try
            {
                await channel.SendAsync(payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch via {Channel}", ch);
            }
        }
    }
}
