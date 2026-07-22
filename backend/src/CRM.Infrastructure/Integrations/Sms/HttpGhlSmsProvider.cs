using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace CRM.Infrastructure.Integrations.Sms;

public class HttpGhlSmsProvider : ISmsProvider
{
    private readonly HttpClient _http;
    private readonly SmsOptions _opts;
    private readonly ILogger<HttpGhlSmsProvider> _logger;

    public string Name => "GHL";

    public HttpGhlSmsProvider(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpGhlSmsProvider> logger)
    {
        _http = Guard.AgainstNull(http);
        _opts = Guard.AgainstNull(opts).Value.Sms;
        _logger = Guard.AgainstNull(logger);
        if (!string.IsNullOrEmpty(_opts.BaseUrl)) _http.BaseAddress = new Uri(_opts.BaseUrl);
        if (!string.IsNullOrEmpty(_opts.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.ApiKey);
    }

    public async Task<SmsResult> SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        Guard.AgainstNull(message);
        try
        {
            var response = await _http.PostAsJsonAsync("/v1/conversations/messages", new
            {
                type = "SMS",
                message = message.Body,
                phone = message.To,
                from = _opts.FromNumber
            }, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                return new SmsResult(false, null, detail);
            }
            var body = await response.Content.ReadFromJsonAsync<GhlSendResponse>(cancellationToken: ct);
            return new SmsResult(true, body?.MessageId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GHL SMS send failed to {To}", message.To);
            return new SmsResult(false, null, ex.Message);
        }
    }

    private record GhlSendResponse(string? MessageId);
}
