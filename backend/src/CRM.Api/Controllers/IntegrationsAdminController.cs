using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Infrastructure.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CRM.Api.Controllers;

/// <summary>
/// Centralized integration management:
///   - Reports the live status of each external provider (stub vs HTTP)
///   - Surfaces the (sanitized) config used to construct it
///   - Lets ops reach a "ping" endpoint where one is meaningful
///
/// We never echo secrets back — passwords / tokens are masked or omitted.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/integrations")]
public class IntegrationsAdminController : ControllerBase
{
    private readonly IntegrationOptions _opts;
    private readonly IConfiguration _config;

    public IntegrationsAdminController(IOptions<IntegrationOptions> opts, IConfiguration config)
    {
        _opts = opts.Value;
        _config = config;
    }

    public record IntegrationField(string Label, string? Value, bool Masked);
    public record IntegrationInfo(
        string Code,
        string Name,
        string Provider,
        bool Active,
        bool UsingStub,
        string Mode,
        IReadOnlyList<IntegrationField> Fields);

    [HttpGet]
    [HasPermission(Permissions.IntegrationsView)]
    public ActionResult<IReadOnlyList<IntegrationInfo>> List()
    {
        var items = new List<IntegrationInfo>();

        // ---- Jornaya ----
        items.Add(new IntegrationInfo(
            Code: "jornaya",
            Name: "Jornaya (LeadiD)",
            Provider: _opts.Jornaya.Provider,
            Active: !string.Equals(_opts.Jornaya.Provider, "Stub", StringComparison.OrdinalIgnoreCase),
            UsingStub: string.Equals(_opts.Jornaya.Provider, "Stub", StringComparison.OrdinalIgnoreCase),
            Mode: ProviderMode(_opts.Jornaya.Provider),
            Fields: new[] {
                new IntegrationField("Base URL",   _opts.Jornaya.BaseUrl,   false),
                new IntegrationField("Account ID", _opts.Jornaya.AccountId, false),
                new IntegrationField("Token",      Mask(_opts.Jornaya.Token), true),
                new IntegrationField("Timeout",    $"{_opts.Jornaya.TimeoutSeconds}s", false),
            }));

        // ---- Dialer (Vici / Zoom Phone / RingCentral) ----
        var dialerLive = DialerLive(_opts.Dialer);
        items.Add(new IntegrationInfo(
            Code: "dialer",
            Name: $"Dialer ({DialerDisplayName(_opts.Dialer.Provider)})",
            Provider: _opts.Dialer.Provider,
            Active: dialerLive,
            UsingStub: !dialerLive,
            Mode: dialerLive ? "Live HTTP" : "Stub",
            Fields: new[] {
                new IntegrationField("Provider",    DialerDisplayName(_opts.Dialer.Provider), false),
                new IntegrationField("Base URL",    _opts.Dialer.BaseUrl,             false),
                new IntegrationField("Username",    _opts.Dialer.Username,            false),
                new IntegrationField("Password",    Mask(_opts.Dialer.Password),      true),
                new IntegrationField("Account ID",  _opts.Dialer.AccountId,           false),
                new IntegrationField("Client ID",   _opts.Dialer.ClientId,            false),
                new IntegrationField("Client secret", Mask(_opts.Dialer.ClientSecret), true),
                new IntegrationField("API token",   Mask(_opts.Dialer.ApiToken),      true),
                new IntegrationField("From number",  _opts.Dialer.FromNumber,          false),
                new IntegrationField("Source",      _opts.Dialer.Source,              false),
                new IntegrationField("Timeout",     $"{_opts.Dialer.TimeoutSeconds}s", false),
            }));

        // ---- SMS (Twilio / GHL) ----
        items.Add(new IntegrationInfo(
            Code: "sms",
            Name: "SMS",
            Provider: _opts.Sms.Provider,
            Active: !string.Equals(_opts.Sms.Provider, "Stub", StringComparison.OrdinalIgnoreCase),
            UsingStub: string.Equals(_opts.Sms.Provider, "Stub", StringComparison.OrdinalIgnoreCase),
            Mode: ProviderMode(_opts.Sms.Provider),
            Fields: new[] {
                new IntegrationField("Provider",     _opts.Sms.Provider,    false),
                new IntegrationField("Base URL",     _opts.Sms.BaseUrl,     false),
                new IntegrationField("API Key",      Mask(_opts.Sms.ApiKey), true),
                new IntegrationField("From Number",  _opts.Sms.FromNumber,  false),
            }));

        // ---- Email (SMTP) ----
        var emailLive = !string.IsNullOrEmpty(_opts.Email.SmtpHost)
            && !string.IsNullOrEmpty(_opts.Email.Username)
            && !string.IsNullOrEmpty(_opts.Email.Password);
        items.Add(new IntegrationInfo(
            Code: "email",
            Name: "Email (SMTP)",
            Provider: _opts.Email.Provider,
            Active: emailLive,
            UsingStub: !emailLive,
            Mode: emailLive ? "Live SMTP" : "Stub",
            Fields: new[] {
                new IntegrationField("Provider",     _opts.Email.Provider,                  false),
                new IntegrationField("SMTP host",    _opts.Email.SmtpHost,                  false),
                new IntegrationField("SMTP port",    _opts.Email.SmtpPort.ToString(),       false),
                new IntegrationField("SSL",          _opts.Email.UseSsl ? "yes" : "no",     false),
                new IntegrationField("Username",     _opts.Email.Username,                  false),
                new IntegrationField("Password",     Mask(_opts.Email.Password),            true),
                new IntegrationField("From",         _opts.Email.FromAddress,               false),
                new IntegrationField("App URL",      _opts.Email.AppUrl,                    false),
            }));

        // ---- Carriers (Aetna / UHC / etc.) ----
        var carrierFields = _opts.Carriers.Endpoints.SelectMany(kv => new[]
        {
            new IntegrationField($"{kv.Key} · base URL", kv.Value.BaseUrl,           false),
            new IntegrationField($"{kv.Key} · API key",  Mask(kv.Value.ApiKey),      true),
            new IntegrationField($"{kv.Key} · timeout",  $"{kv.Value.TimeoutSeconds}s", false),
        }).ToList();
        items.Add(new IntegrationInfo(
            Code: "carriers",
            Name: "Carrier APIs",
            Provider: _opts.Carriers.Endpoints.Count > 0 ? "Http" : "Stub",
            Active: _opts.Carriers.Endpoints.Count > 0,
            UsingStub: _opts.Carriers.Endpoints.Count == 0,
            Mode: _opts.Carriers.Endpoints.Count > 0 ? "Live HTTP" : "Stub",
            Fields: carrierFields.Count > 0 ? carrierFields : new() {
                new IntegrationField("Endpoints", "(none configured — using stubs)", false),
            }));

        // ---- Funding ----
        items.Add(new IntegrationInfo(
            Code: "funding",
            Name: "Funding provider",
            Provider: _opts.Funding.Provider,
            Active: !string.Equals(_opts.Funding.Provider, "Stub", StringComparison.OrdinalIgnoreCase),
            UsingStub: string.Equals(_opts.Funding.Provider, "Stub", StringComparison.OrdinalIgnoreCase),
            Mode: ProviderMode(_opts.Funding.Provider),
            Fields: new[] {
                new IntegrationField("Base URL", _opts.Funding.BaseUrl,        false),
                new IntegrationField("API Key",  Mask(_opts.Funding.ApiKey),   true),
                new IntegrationField("Auto-fund on validate",
                    _config.GetValue("Sales:AutoFundOnValidate", true) ? "enabled" : "disabled", false),
            }));

        // ---- Lyons (banking validation) ----
        var lyonsSection = _config.GetSection("Integrations:Lyons");
        items.Add(new IntegrationInfo(
            Code: "lyons",
            Name: "Lyons (banking validation)",
            Provider: lyonsSection["Provider"] ?? "Stub",
            Active: string.Equals(lyonsSection["Provider"], "Http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(lyonsSection["BaseUrl"]),
            UsingStub: !string.Equals(lyonsSection["Provider"], "Http", StringComparison.OrdinalIgnoreCase),
            Mode: ProviderMode(lyonsSection["Provider"]),
            Fields: new[] {
                new IntegrationField("Base URL", lyonsSection["BaseUrl"],        false),
                new IntegrationField("API Key",  Mask(lyonsSection["ApiKey"]),   true),
                new IntegrationField("Timeout",  lyonsSection["TimeoutSeconds"], false),
            }));

        // ---- BLA ----
        var blaSection = _config.GetSection("Integrations:Bla");
        items.Add(new IntegrationInfo(
            Code: "bla",
            Name: "BLA (lead-quality)",
            Provider: blaSection["Provider"] ?? "Stub",
            Active: string.Equals(blaSection["Provider"], "Http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(blaSection["BaseUrl"]),
            UsingStub: !string.Equals(blaSection["Provider"], "Http", StringComparison.OrdinalIgnoreCase),
            Mode: ProviderMode(blaSection["Provider"]),
            Fields: new[] {
                new IntegrationField("Base URL", blaSection["BaseUrl"],         false),
                new IntegrationField("API Key",  Mask(blaSection["ApiKey"]),    true),
                new IntegrationField("Timeout",  blaSection["TimeoutSeconds"],  false),
            }));

        // ---- Trello ----
        var trelloSection = _config.GetSection("Integrations:Trello");
        items.Add(new IntegrationInfo(
            Code: "trello",
            Name: "Trello",
            Provider: trelloSection["Provider"] ?? "Stub",
            Active: string.Equals(trelloSection["Provider"], "Http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(trelloSection["Token"]),
            UsingStub: !string.Equals(trelloSection["Provider"], "Http", StringComparison.OrdinalIgnoreCase),
            Mode: ProviderMode(trelloSection["Provider"]),
            Fields: new[] {
                new IntegrationField("Base URL", trelloSection["BaseUrl"],     false),
                new IntegrationField("Key",      Mask(trelloSection["Key"]),   true),
                new IntegrationField("Token",    Mask(trelloSection["Token"]), true),
            }));

        return Ok(items);
    }

    public record HealthResult(string Code, string Mode, bool Healthy, string Message, long ElapsedMs);

    /// <summary>
    /// Cheap "did we configure this?" health check. Doesn't make outbound HTTP calls
    /// (those would be slow + can incur cost), instead validates that required
    /// settings are present.  Replace with real /health probes per provider as
    /// needed.
    /// </summary>
    [HttpPost("{code}/check")]
    [HasPermission(Permissions.IntegrationsView)]
    public ActionResult<HealthResult> Check(string code)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (mode, healthy, msg) = code.ToLowerInvariant() switch
        {
            "jornaya" => (ProviderMode(_opts.Jornaya.Provider),
                !string.IsNullOrEmpty(_opts.Jornaya.BaseUrl) || string.Equals(_opts.Jornaya.Provider, "Stub", StringComparison.OrdinalIgnoreCase),
                "Configuration looks valid."),
            "dialer" => (DialerLive(_opts.Dialer) ? "Live HTTP" : "Stub",
                true, $"{DialerDisplayName(_opts.Dialer.Provider)} dialer wired ({(DialerLive(_opts.Dialer) ? "live" : "stub")})."),
            "sms" => (ProviderMode(_opts.Sms.Provider), true, "SMS provider wired."),
            "email" => (string.IsNullOrEmpty(_opts.Email.SmtpHost) ? "Stub" : "Live SMTP",
                !string.IsNullOrEmpty(_opts.Email.FromAddress), "FROM address required."),
            "carriers" => (_opts.Carriers.Endpoints.Count > 0 ? "Live HTTP" : "Stub",
                true, $"{_opts.Carriers.Endpoints.Count} carrier endpoint(s) configured."),
            "funding" => (ProviderMode(_opts.Funding.Provider), true, "Funding provider wired."),
            "lyons" => (ProviderMode(_config["Integrations:Lyons:Provider"]),
                true, "Lyons banking validator wired (stub if not configured)."),
            "bla" or "trello" => ("Stub or HTTP",
                true, "Optional integration, will use stub if not configured."),
            _ => ("Unknown", false, $"No integration with code '{code}'."),
        };
        sw.Stop();
        return Ok(new HealthResult(code, mode, healthy, msg, sw.ElapsedMilliseconds));
    }

    private static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length <= 6) return new string('•', value.Length);
        return value.Substring(0, 2) + new string('•', value.Length - 4) + value.Substring(value.Length - 2);
    }

    private static string DialerDisplayName(string? provider) => (provider ?? "Vici").ToLowerInvariant() switch
    {
        "zoom" or "zoomphone" => "Zoom Phone",
        "ringcentral" => "RingCentral",
        "stub" => "Stub",
        _ => "ViciDial",
    };

    // A dialer is "live" when it has the transport config it needs: Vici needs a BaseUrl;
    // Zoom/RingCentral need either a pre-issued token or OAuth client credentials.
    private static bool DialerLive(DialerOptions d) => (d.Provider ?? "Vici").ToLowerInvariant() switch
    {
        "zoom" or "zoomphone" => !string.IsNullOrEmpty(d.ApiToken) || (!string.IsNullOrEmpty(d.ClientId) && !string.IsNullOrEmpty(d.ClientSecret) && !string.IsNullOrEmpty(d.AccountId)),
        "ringcentral" => !string.IsNullOrEmpty(d.ApiToken) || (!string.IsNullOrEmpty(d.ClientId) && !string.IsNullOrEmpty(d.ClientSecret)),
        "stub" => false,
        _ => !string.IsNullOrEmpty(d.BaseUrl),
    };

    private static string ProviderMode(string? provider) =>
        string.IsNullOrEmpty(provider) || string.Equals(provider, "Stub", StringComparison.OrdinalIgnoreCase)
            ? "Stub" : "Live HTTP";
}
