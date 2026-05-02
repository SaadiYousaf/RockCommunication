using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CRM.Application.Common.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Integrations.Dialer;

/// <summary>
/// RingCentral dialer using the RingOut API
/// (POST /restapi/v1.0/account/~/extension/~/ring-out). RingCentral rings the
/// agent's <c>FromNumber</c> first, then bridges the lead once answered.
///
/// Auth: a pre-issued JWT/OAuth bearer token (<c>ApiToken</c>) is used directly;
/// if only client credentials are configured a password-less OAuth exchange is
/// attempted against <c>TokenUrl</c>. Selected when
/// <c>Integrations:Dialer:Provider = "RingCentral"</c>.
/// </summary>
public class HttpRingCentralDialerProvider : IDialerProvider
{
    private readonly HttpClient _http;
    private readonly DialerOptions _opts;
    private readonly ILogger<HttpRingCentralDialerProvider> _logger;

    public string Name => "RingCentral";

    public HttpRingCentralDialerProvider(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpRingCentralDialerProvider> logger)
    {
        _http = http;
        _opts = opts.Value.Dialer;
        _logger = logger;
        var baseUrl = string.IsNullOrEmpty(_opts.BaseUrl) ? "https://platform.ringcentral.com" : _opts.BaseUrl;
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<DialResult> DialAsync(Guid agentId, string phoneNumber, Guid leadId, CancellationToken ct = default)
    {
        try
        {
            await AuthorizeAsync(ct);
            var payload = new
            {
                from = new { phoneNumber = _opts.FromNumber },
                to = new { phoneNumber = phoneNumber },
                playPrompt = false,
            };
            var response = await _http.PostAsJsonAsync("/restapi/v1.0/account/~/extension/~/ring-out", payload, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.ToString() : Guid.NewGuid().ToString("N");
            var status = doc.RootElement.TryGetProperty("status", out var st) && st.TryGetProperty("callStatus", out var cs)
                ? cs.GetString() ?? "Initiated" : "Initiated";
            return new DialResult(id, status, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RingCentral RingOut failed agent={Agent} phone={Phone}", agentId, phoneNumber);
            throw;
        }
    }

    public async Task HangupAsync(string callId, CancellationToken ct = default)
    {
        try
        {
            await AuthorizeAsync(ct);
            await _http.DeleteAsync($"/restapi/v1.0/account/~/extension/~/ring-out/{callId}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RingCentral hangup failed call={Call}", callId);
        }
    }

    public async Task<string> GetStatusAsync(string callId, CancellationToken ct = default)
    {
        try
        {
            await AuthorizeAsync(ct);
            var response = await _http.GetAsync($"/restapi/v1.0/account/~/extension/~/ring-out/{callId}", ct);
            if (!response.IsSuccessStatusCode) return "Unknown";
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("status", out var st) && st.TryGetProperty("callStatus", out var cs)
                ? cs.GetString() ?? "Unknown" : "Unknown";
        }
        catch { return "Unknown"; }
    }

    /// <summary>Sets the bearer token. Prefers a pre-issued <c>ApiToken</c>; otherwise runs a client-credentials OAuth exchange.</summary>
    private async Task AuthorizeAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_opts.ApiToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
            return;
        }
        if (string.IsNullOrEmpty(_opts.ClientId) || string.IsNullOrEmpty(_opts.ClientSecret)) return;

        var tokenUrl = string.IsNullOrEmpty(_opts.TokenUrl) ? "/restapi/oauth/token" : _opts.TokenUrl;
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetProperty("access_token").GetString();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
