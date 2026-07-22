using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Integrations.Dialer;

/// <summary>
/// Zoom Phone dialer using the Call Control API. A server-to-server OAuth token
/// is fetched from <c>AccountId</c>/<c>ClientId</c>/<c>ClientSecret</c> (or a
/// pre-issued <c>ApiToken</c> is used directly), then an outbound call is placed
/// from the agent's <c>FromNumber</c> to the lead.
///
/// Selected when <c>Integrations:Dialer:Provider = "Zoom"</c>. The exact call
/// endpoint/version should be confirmed against the Zoom Phone API tier in use;
/// this follows the documented Call Control "dial" shape and degrades gracefully.
/// </summary>
public class HttpZoomPhoneDialerProvider : IDialerProvider
{
    private readonly HttpClient _http;
    private readonly DialerOptions _opts;
    private readonly ILogger<HttpZoomPhoneDialerProvider> _logger;

    public string Name => "Zoom";

    public HttpZoomPhoneDialerProvider(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpZoomPhoneDialerProvider> logger)
    {
        _http = Guard.AgainstNull(http);
        _opts = Guard.AgainstNull(opts).Value.Dialer;
        _logger = Guard.AgainstNull(logger);
        var baseUrl = string.IsNullOrEmpty(_opts.BaseUrl) ? "https://api.zoom.us" : _opts.BaseUrl;
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<DialResult> DialAsync(Guid agentId, string phoneNumber, Guid leadId, CancellationToken ct = default)
    {
        Guard.AgainstNullOrWhiteSpace(phoneNumber);
        try
        {
            await AuthorizeAsync(ct);
            // Zoom Phone Call Control: initiate an outbound call from the caller (FromNumber) to the callee.
            var payload = new { callee = new { phone_number = phoneNumber }, caller = new { phone_number = _opts.FromNumber } };
            var response = await _http.PostAsJsonAsync("/v2/phone/call_control/dial", payload, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var id = doc.RootElement.TryGetProperty("call_id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");
            return new DialResult(id, "Initiated", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zoom Phone dial failed agent={Agent} phone={Phone}", agentId, phoneNumber);
            throw;
        }
    }

    public async Task HangupAsync(string callId, CancellationToken ct = default)
    {
        Guard.AgainstNullOrWhiteSpace(callId);
        try
        {
            await AuthorizeAsync(ct);
            await _http.PostAsync($"/v2/phone/call_control/{callId}/hangup", content: null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zoom Phone hangup failed call={Call}", callId);
        }
    }

    public Task<string> GetStatusAsync(string callId, CancellationToken ct = default) => Task.FromResult("Unknown");

    /// <summary>Sets the bearer token. Prefers a pre-issued <c>ApiToken</c>; otherwise runs a server-to-server (account_credentials) exchange.</summary>
    private async Task AuthorizeAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_opts.ApiToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
            return;
        }
        if (string.IsNullOrEmpty(_opts.ClientId) || string.IsNullOrEmpty(_opts.ClientSecret) || string.IsNullOrEmpty(_opts.AccountId)) return;

        var tokenUrl = string.IsNullOrEmpty(_opts.TokenUrl)
            ? $"https://zoom.us/oauth/token?grant_type=account_credentials&account_id={Uri.EscapeDataString(_opts.AccountId)}"
            : _opts.TokenUrl;
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetProperty("access_token").GetString();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
