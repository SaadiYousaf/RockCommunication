using CRM.Application.Common.Integrations;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Integrations.Funding;

public class StubFundingProvider : IFundingProvider
{
    private readonly ILogger<StubFundingProvider> _logger;
    public StubFundingProvider(ILogger<StubFundingProvider> logger) => _logger = logger;

    public Task<FundingResult> SubmitAsync(FundingRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Funding submit policy={Policy} amount={Amount}", request.PolicyNumber, request.Amount);
        return Task.FromResult(new FundingResult(true, Guid.NewGuid().ToString("N"), "Pending", null));
    }

    public Task<FundingResult> GetStatusAsync(string fundingReferenceId, CancellationToken ct = default) =>
        Task.FromResult(new FundingResult(true, fundingReferenceId, "Funded", null));
}
