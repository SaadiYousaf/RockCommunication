using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CRM.Application.Common.Integrations;
using CRM.Application.Sales.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Integrations.Lyons;

public class LyonsOptions
{
    public string Provider { get; set; } = "Stub";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Offline Lyons validator. Deterministically maps a routing/account pair onto a
/// banking code so the sale flow is fully exercisable without the live service:
///   - routing must be 9 digits and pass the ABA checksum, else <c>Blocked</c>;
///   - account must be at least 4 digits, else <c>Blocked</c>;
///   - accounts whose last digit is 9 are flagged <c>RequiresRecording</c> (198);
///   - everything else is <c>Clear</c> (103).
/// </summary>
public class StubLyonsBankingValidator : ILyonsBankingValidator
{
    private readonly ILogger<StubLyonsBankingValidator> _logger;
    public StubLyonsBankingValidator(ILogger<StubLyonsBankingValidator> logger) => _logger = logger;

    public Task<LyonsValidationResult> ValidateAsync(LyonsValidationRequest request, CancellationToken ct = default)
    {
        var routing = new string((request.RoutingNumber ?? "").Where(char.IsDigit).ToArray());
        var account = new string((request.AccountNumber ?? "").Where(char.IsDigit).ToArray());
        var reference = $"LYONS-STUB-{Guid.NewGuid():N}".Substring(0, 18);

        if (!IsValidAba(routing))
            return Task.FromResult(new LyonsValidationResult(BankValidationStatus.Blocked, 0, null, reference,
                "Routing number failed ABA checksum validation."));

        if (account.Length < 4)
            return Task.FromResult(new LyonsValidationResult(BankValidationStatus.Blocked, 0, null, reference,
                "Account number is too short to validate."));

        var bankName = $"Bank {routing.Substring(0, 4)}";
        if (account[^1] == '9')
        {
            _logger.LogInformation("Lyons stub: account flagged (needs recording) ref={Ref}", reference);
            return Task.FromResult(new LyonsValidationResult(BankValidationStatus.RequiresRecording,
                BankingPolicy.RequiresRecording, bankName, reference, "Account flagged — verification recording required."));
        }

        return Task.FromResult(new LyonsValidationResult(BankValidationStatus.Clear,
            BankingPolicy.Clear, bankName, reference, "Account verified."));
    }

    /// <summary>ABA routing-number checksum: 3(d1+d4+d7)+7(d2+d5+d8)+(d3+d6+d9) ≡ 0 (mod 10).</summary>
    internal static bool IsValidAba(string routing)
    {
        if (routing.Length != 9) return false;
        var d = routing.Select(c => c - '0').ToArray();
        var sum = 3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + (d[2] + d[5] + d[8]);
        return sum % 10 == 0;
    }
}

/// <summary>Live Lyons Commercial Data validator (POST /verify), mapping its response onto the banking code.</summary>
public class HttpLyonsBankingValidator : ILyonsBankingValidator
{
    private readonly HttpClient _http;
    private readonly LyonsOptions _opts;
    private readonly ILogger<HttpLyonsBankingValidator> _logger;

    public HttpLyonsBankingValidator(HttpClient http, IOptions<LyonsOptions> opts, ILogger<HttpLyonsBankingValidator> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
        if (!string.IsNullOrEmpty(_opts.BaseUrl)) _http.BaseAddress = new Uri(_opts.BaseUrl);
        if (!string.IsNullOrEmpty(_opts.ApiKey)) _http.DefaultRequestHeaders.Add("X-Api-Key", _opts.ApiKey);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    private record LyonsResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("bankName")] string? BankName,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("reason")] string? Reason);

    public async Task<LyonsValidationResult> ValidateAsync(LyonsValidationRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/verify", request, ct);
            if (!resp.IsSuccessStatusCode)
                return new LyonsValidationResult(BankValidationStatus.Blocked, 0, null, null, $"Lyons HTTP {(int)resp.StatusCode}");

            var body = await resp.Content.ReadFromJsonAsync<LyonsResponse>(cancellationToken: ct);
            var status = (body?.Status ?? "blocked").ToLowerInvariant() switch
            {
                "clear" or "verified" or "pass" or "valid" => BankValidationStatus.Clear,
                "flagged" or "review" or "conditional" or "recording" => BankValidationStatus.RequiresRecording,
                _ => BankValidationStatus.Blocked,
            };
            var code = status switch
            {
                BankValidationStatus.Clear => BankingPolicy.Clear,
                BankValidationStatus.RequiresRecording => BankingPolicy.RequiresRecording,
                _ => 0,
            };
            return new LyonsValidationResult(status, code, body?.BankName, body?.Reference, body?.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lyons validation failed");
            throw;
        }
    }
}
