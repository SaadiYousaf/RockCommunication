using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Integrations.Jornaya;

public class StubJornayaProvider : IJornayaProvider
{
    private readonly ILogger<StubJornayaProvider> _logger;

    public StubJornayaProvider(ILogger<StubJornayaProvider> logger) => _logger = Guard.AgainstNull(logger);

    public Task<JornayaVerificationResult> VerifyAsync(string leadId, string? jornayaLeadId, CancellationToken ct = default)
    {
        Guard.AgainstNullOrWhiteSpace(leadId);
        _logger.LogInformation("Stub Jornaya verify lead={LeadId} token={Token}", leadId, jornayaLeadId);
        var verified = !string.IsNullOrWhiteSpace(jornayaLeadId);
        return Task.FromResult(new JornayaVerificationResult(verified, jornayaLeadId, DateTime.UtcNow));
    }
}
