using CRM.Application.Common.Integrations;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Integrations.Email;

public class StubEmailProvider : IEmailProvider
{
    private readonly ILogger<StubEmailProvider> _logger;
    public string Name => "SmtpStub";

    public StubEmailProvider(ILogger<StubEmailProvider> logger) => _logger = logger;

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("Email to {To} subject={Subject}", message.To, message.Subject);
        return Task.FromResult(new EmailResult(true, Guid.NewGuid().ToString("N"), null));
    }
}
