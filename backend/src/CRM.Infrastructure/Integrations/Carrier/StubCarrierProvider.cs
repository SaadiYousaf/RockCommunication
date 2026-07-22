using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Integrations.Carrier;

public abstract class CarrierProviderBase : ICarrierProvider
{
    private readonly ILogger _logger;
    protected CarrierProviderBase(ILogger logger) => _logger = Guard.AgainstNull(logger);

    public abstract string CarrierCode { get; }

    public virtual Task<CarrierApplicationResult> SubmitApplicationAsync(CarrierApplicationRequest request, CancellationToken ct = default)
    {
        Guard.AgainstNull(request);
        _logger.LogInformation("{Carrier} submit app for {First} {Last}", CarrierCode, request.FirstName, request.LastName);
        return Task.FromResult(new CarrierApplicationResult(
            Accepted: true,
            PolicyNumber: $"{CarrierCode}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}",
            CarrierReferenceId: Guid.NewGuid().ToString("N"),
            Status: "Submitted",
            Reason: null,
            SubmittedAt: DateTime.UtcNow));
    }

    public virtual Task<CarrierApplicationResult> GetStatusAsync(string carrierReferenceId, CancellationToken ct = default) =>
        Task.FromResult(new CarrierApplicationResult(true, null, carrierReferenceId, "InReview", null, DateTime.UtcNow));
}

public class StubCarrierAetna : CarrierProviderBase
{
    public StubCarrierAetna(ILogger<StubCarrierAetna> logger) : base(logger) { }
    public override string CarrierCode => "AETNA";
}

public class StubCarrierUnitedHealth : CarrierProviderBase
{
    public StubCarrierUnitedHealth(ILogger<StubCarrierUnitedHealth> logger) : base(logger) { }
    public override string CarrierCode => "UHC";
}

public class CarrierRegistry : ICarrierRegistry
{
    private readonly Dictionary<string, ICarrierProvider> _byCode;

    public CarrierRegistry(IEnumerable<ICarrierProvider> providers)
    {
        _byCode = Guard.AgainstNull(providers).ToDictionary(p => p.CarrierCode, StringComparer.OrdinalIgnoreCase);
    }

    public ICarrierProvider Get(string carrierCode) =>
        _byCode.TryGetValue(carrierCode, out var p) ? p
            : throw new InvalidOperationException($"Unknown carrier '{carrierCode}'.");

    public IReadOnlyList<string> AvailableCarriers => _byCode.Keys.ToList();
}
