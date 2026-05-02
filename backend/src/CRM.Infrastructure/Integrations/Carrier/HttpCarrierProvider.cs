using CRM.Application.Common.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace CRM.Infrastructure.Integrations.Carrier;

public abstract class HttpCarrierProvider : ICarrierProvider
{
    protected readonly HttpClient Http;
    protected readonly CarrierEndpoint Endpoint;
    protected readonly ILogger Logger;

    public abstract string CarrierCode { get; }

    protected HttpCarrierProvider(HttpClient http, IOptions<IntegrationOptions> opts, ILogger logger)
    {
        Http = http;
        Logger = logger;
        Endpoint = opts.Value.Carriers.Endpoints.TryGetValue(CarrierCode, out var ep) ? ep : new CarrierEndpoint();
        if (!string.IsNullOrEmpty(Endpoint.BaseUrl)) Http.BaseAddress = new Uri(Endpoint.BaseUrl);
        if (!string.IsNullOrEmpty(Endpoint.ApiKey))
            Http.DefaultRequestHeaders.Add("X-Api-Key", Endpoint.ApiKey);
        Http.Timeout = TimeSpan.FromSeconds(Endpoint.TimeoutSeconds);
    }

    public virtual async Task<CarrierApplicationResult> SubmitApplicationAsync(CarrierApplicationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Endpoint.BaseUrl))
        {
            Logger.LogInformation("{Carrier} endpoint not configured; returning stub", CarrierCode);
            return new CarrierApplicationResult(true,
                $"{CarrierCode}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                Guid.NewGuid().ToString("N"), "Submitted", null, DateTime.UtcNow);
        }

        var response = await Http.PostAsJsonAsync("/applications", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var reason = await response.Content.ReadAsStringAsync(ct);
            return new CarrierApplicationResult(false, null, null, "Rejected", reason, DateTime.UtcNow);
        }
        var body = await response.Content.ReadFromJsonAsync<CarrierResponseBody>(cancellationToken: ct);
        return new CarrierApplicationResult(true, body?.PolicyNumber, body?.ReferenceId, body?.Status ?? "Submitted", null, DateTime.UtcNow);
    }

    public virtual async Task<CarrierApplicationResult> GetStatusAsync(string carrierReferenceId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Endpoint.BaseUrl))
            return new CarrierApplicationResult(true, null, carrierReferenceId, "InReview", null, DateTime.UtcNow);

        var response = await Http.GetAsync($"/applications/{carrierReferenceId}", ct);
        var body = await response.Content.ReadFromJsonAsync<CarrierResponseBody>(cancellationToken: ct);
        return new CarrierApplicationResult(response.IsSuccessStatusCode,
            body?.PolicyNumber, carrierReferenceId, body?.Status ?? "Unknown", null, DateTime.UtcNow);
    }

    private record CarrierResponseBody(string? PolicyNumber, string? ReferenceId, string? Status);
}

public class HttpCarrierAetna : HttpCarrierProvider
{
    public HttpCarrierAetna(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpCarrierAetna> logger)
        : base(http, opts, logger) { }
    public override string CarrierCode => "AETNA";
}

public class HttpCarrierUhc : HttpCarrierProvider
{
    public HttpCarrierUhc(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpCarrierUhc> logger)
        : base(http, opts, logger) { }
    public override string CarrierCode => "UHC";
}
