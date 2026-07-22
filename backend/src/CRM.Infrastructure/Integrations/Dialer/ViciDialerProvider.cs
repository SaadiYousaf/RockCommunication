using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Integrations.Dialer;

public class ViciDialerProvider : IDialerProvider
{
    private readonly ILogger<ViciDialerProvider> _logger;
    public string Name => "Vici";

    public ViciDialerProvider(ILogger<ViciDialerProvider> logger) => _logger = Guard.AgainstNull(logger);

    public Task<DialResult> DialAsync(Guid agentId, string phoneNumber, Guid leadId, CancellationToken ct = default)
    {
        Guard.AgainstNullOrWhiteSpace(phoneNumber);
        _logger.LogInformation("Vici stub: dial agent={Agent} phone={Phone} lead={Lead}", agentId, phoneNumber, leadId);
        var callId = Guid.NewGuid().ToString("N");
        return Task.FromResult(new DialResult(callId, "Initiated", DateTime.UtcNow));
    }

    public Task HangupAsync(string callId, CancellationToken ct = default)
    {
        Guard.AgainstNullOrWhiteSpace(callId);
        _logger.LogInformation("Vici stub: hangup {CallId}", callId);
        return Task.CompletedTask;
    }

    public Task<string> GetStatusAsync(string callId, CancellationToken ct = default) =>
        Task.FromResult("Connected");
}
