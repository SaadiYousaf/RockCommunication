using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace CRM.Infrastructure.Integrations.Bla;

public class BlaOptions
{
    public string Provider { get; set; } = "Stub";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

public class StubBlaProvider : IBlaProvider
{
    private readonly ILogger<StubBlaProvider> _logger;
    public StubBlaProvider(ILogger<StubBlaProvider> logger) => _logger = Guard.AgainstNull(logger);
    public Task<BlaQuoteResult> GetQuoteAsync(BlaQuoteRequest request, CancellationToken ct = default)
    {
        Guard.AgainstNull(request);
        _logger.LogInformation("BLA stub: quote for {First} {Last} state={State}", request.FirstName, request.LastName, request.State);
        var age = DateTime.UtcNow.Year - request.DateOfBirth.Year;
        var premium = age < 65 ? 200m + age : 300m + (age - 65) * 5m;
        return Task.FromResult(new BlaQuoteResult(true, premium, "AETNA", null));
    }
}

public class HttpBlaProvider : IBlaProvider
{
    private readonly HttpClient _http;
    private readonly BlaOptions _opts;
    private readonly ILogger<HttpBlaProvider> _logger;

    public HttpBlaProvider(HttpClient http, IOptions<BlaOptions> opts, ILogger<HttpBlaProvider> logger)
    {
        _http = Guard.AgainstNull(http);
        _opts = Guard.AgainstNull(opts).Value;
        _logger = Guard.AgainstNull(logger);
        if (!string.IsNullOrEmpty(_opts.BaseUrl)) _http.BaseAddress = new Uri(_opts.BaseUrl);
        if (!string.IsNullOrEmpty(_opts.ApiKey))
            _http.DefaultRequestHeaders.Add("X-Api-Key", _opts.ApiKey);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<BlaQuoteResult> GetQuoteAsync(BlaQuoteRequest request, CancellationToken ct = default)
    {
        Guard.AgainstNull(request);
        try
        {
            var resp = await _http.PostAsJsonAsync("/quotes", request, ct);
            if (!resp.IsSuccessStatusCode)
                return new BlaQuoteResult(false, null, null, $"HTTP {(int)resp.StatusCode}");
            return await resp.Content.ReadFromJsonAsync<BlaQuoteResult>(cancellationToken: ct)
                ?? new BlaQuoteResult(false, null, null, "Empty response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BLA quote failed");
            throw;
        }
    }
}
