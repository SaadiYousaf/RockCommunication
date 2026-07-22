using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace CRM.Infrastructure.Integrations.Sms;

/// <summary>
/// Twilio SMS using the public REST API:
///   POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json
/// Auth = Basic ("AccountSid:AuthToken").
/// Configure in appsettings:
///   "Sms": {
///     "Provider":   "Twilio",
///     "BaseUrl":    "https://api.twilio.com",
///     "ApiKey":     "AC...:auth-token",   // accountSid:authToken pair
///     "FromNumber": "+15551234567"        // Twilio-purchased number
///   }
/// </summary>
public class HttpTwilioSmsProvider : ISmsProvider
{
    private readonly HttpClient _http;
    private readonly SmsOptions _opts;
    private readonly ILogger<HttpTwilioSmsProvider> _logger;
    private readonly string? _accountSid;

    public string Name => "Twilio";

    public HttpTwilioSmsProvider(HttpClient http, IOptions<IntegrationOptions> opts, ILogger<HttpTwilioSmsProvider> logger)
    {
        _http = Guard.AgainstNull(http);
        _opts = Guard.AgainstNull(opts).Value.Sms;
        _logger = Guard.AgainstNull(logger);

        _http.BaseAddress = new Uri(string.IsNullOrEmpty(_opts.BaseUrl) ? "https://api.twilio.com" : _opts.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(15);

        if (!string.IsNullOrEmpty(_opts.ApiKey) && _opts.ApiKey.Contains(':'))
        {
            // Twilio "ApiKey" carries SID:AuthToken
            var parts = _opts.ApiKey.Split(':', 2);
            _accountSid = parts[0];
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{parts[0]}:{parts[1]}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
    }

    public async Task<SmsResult> SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        Guard.AgainstNull(message);
        if (string.IsNullOrEmpty(_accountSid) || string.IsNullOrEmpty(_opts.FromNumber))
        {
            _logger.LogWarning("Twilio not fully configured (need ApiKey=accountSid:authToken + FromNumber).");
            return new SmsResult(false, null, "twilio-not-configured");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = message.To.StartsWith("+") ? message.To : $"+1{message.To.TrimStart('1')}",
            ["From"] = _opts.FromNumber!,
            ["Body"] = message.Body,
        };

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var resp = await _http.PostAsync($"/2010-04-01/Accounts/{_accountSid}/Messages.json", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var raw = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Twilio {Status}: {Body}", resp.StatusCode, raw.Length > 300 ? raw[..300] : raw);
                return new SmsResult(false, null, $"http-{(int)resp.StatusCode}");
            }
            var body = await resp.Content.ReadFromJsonAsync<TwilioMessageDto>(cancellationToken: ct);
            return new SmsResult(true, body?.Sid, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio send failed");
            return new SmsResult(false, null, ex.Message);
        }
    }

    private record TwilioMessageDto(string? Sid, string? Status);
}
