using CRM.Application.Common.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Web;

namespace CRM.Infrastructure.Integrations.Dialer;

public class HttpViciDialerProvider : IDialerProvider
{
    private readonly HttpClient _http;
    private readonly DialerOptions _opts;
    private readonly ILogger<HttpViciDialerProvider> _logger;

    public string Name => "Vici";

    public HttpViciDialerProvider(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpViciDialerProvider> logger)
    {
        _http = http;
        _opts = opts.Value.Dialer;
        _logger = logger;
        if (!string.IsNullOrEmpty(_opts.BaseUrl)) _http.BaseAddress = new Uri(_opts.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<DialResult> DialAsync(Guid agentId, string phoneNumber, Guid leadId, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["function"] = "external_dial";
        query["agent_user"] = agentId.ToString();
        query["phone_number"] = phoneNumber;
        query["value"] = "YES";
        query["search"] = "NO";
        query["preview"] = "NO";
        query["focus"] = "YES";
        query["source"] = _opts.Source ?? "CRM";
        if (_opts.Username is not null) query["user"] = _opts.Username;
        if (_opts.Password is not null) query["pass"] = _opts.Password;

        try
        {
            var response = await _http.GetAsync($"/agc/api.php?{query}", ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = body.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ? "Initiated" : "Failed";
            return new DialResult(Guid.NewGuid().ToString("N"), status, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vici dial failed agent={Agent} phone={Phone}", agentId, phoneNumber);
            throw;
        }
    }

    public async Task HangupAsync(string callId, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["function"] = "external_hangup";
        query["agent_user"] = callId;
        query["value"] = "1";
        if (_opts.Username is not null) query["user"] = _opts.Username;
        if (_opts.Password is not null) query["pass"] = _opts.Password;
        await _http.GetAsync($"/agc/api.php?{query}", ct);
    }

    public Task<string> GetStatusAsync(string callId, CancellationToken ct = default) => Task.FromResult("Unknown");
}
