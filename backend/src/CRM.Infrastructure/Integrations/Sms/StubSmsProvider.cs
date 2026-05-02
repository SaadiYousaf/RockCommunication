using CRM.Application.Common.Integrations;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Integrations.Sms;

public class StubSmsProvider : ISmsProvider
{
    private readonly ILogger<StubSmsProvider> _logger;
    public string Name => "GHL";

    public StubSmsProvider(ILogger<StubSmsProvider> logger) => _logger = logger;

    public Task<SmsResult> SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("SMS to {To}: {Body}", message.To, message.Body);
        return Task.FromResult(new SmsResult(true, Guid.NewGuid().ToString("N"), null));
    }
}
