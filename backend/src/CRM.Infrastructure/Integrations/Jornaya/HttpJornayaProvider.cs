using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace CRM.Infrastructure.Integrations.Jornaya;

public class HttpJornayaProvider : IJornayaProvider
{
    private readonly HttpClient _http;
    private readonly JornayaOptions _opts;
    private readonly ILogger<HttpJornayaProvider> _logger;

    public HttpJornayaProvider(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpJornayaProvider> logger)
    {
        _http = Guard.AgainstNull(http);
        _opts = Guard.AgainstNull(opts).Value.Jornaya;
        _logger = Guard.AgainstNull(logger);
        if (!string.IsNullOrEmpty(_opts.BaseUrl)) _http.BaseAddress = new Uri(_opts.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
        if (!string.IsNullOrEmpty(_opts.Token))
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.Token);
    }

    public async Task<JornayaVerificationResult> VerifyAsync(string leadId, string? jornayaLeadId, CancellationToken ct = default)
    {
        Guard.AgainstNullOrWhiteSpace(leadId);
        if (string.IsNullOrWhiteSpace(jornayaLeadId))
            return new JornayaVerificationResult(false, null, DateTime.UtcNow);

        try
        {
            var response = await _http.PostAsJsonAsync("/leadid/inquire", new
            {
                accountId = _opts.AccountId,
                lead_id = jornayaLeadId,
                campaign = "default"
            }, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Jornaya returned {Status} for lead {Lead}", response.StatusCode, leadId);
                return new JornayaVerificationResult(false, jornayaLeadId, DateTime.UtcNow);
            }

            var raw = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken: ct);
            return new JornayaVerificationResult(true, jornayaLeadId, DateTime.UtcNow, raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jornaya call failed for lead {Lead}", leadId);
            throw;
        }
    }
}
