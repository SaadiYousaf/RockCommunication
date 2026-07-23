using CRM.Application.CallCenter;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CRM.Domain.Constants;

namespace CRM.Api.Controllers;

[ApiController]
[Route("api/webhooks/dialer")]
[EnableRateLimiting("webhook")]   // 600/min/IP — prevents upstream provider bursts from DoSing us.
public class DialerWebhookController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _config;

    public DialerWebhookController(IMediator mediator, IConfiguration config)
    {
        _mediator = Guard.AgainstNull(mediator);
        _config = Guard.AgainstNull(config);
    }

    // NOTE: this action reads the raw request body itself (no [FromBody]) so the HMAC is
    // computed over the exact bytes received. [FromBody] would consume the stream during
    // model binding, leaving the signature to be checked over an empty body.
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        var secret = _config["Webhooks:Dialer:Secret"];

        // Fail CLOSED: without a configured signing secret we cannot authenticate the
        // caller, so refuse the request rather than accepting it unverified.
        if (string.IsNullOrEmpty(secret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Webhook is not configured." });

        if (!Request.Headers.TryGetValue(AppConstants.HttpHeaderNames.Signature, out var signature) || string.IsNullOrEmpty(signature))
            return Unauthorized(new { error = "Missing X-Signature." });

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var computed = ComputeHmac(body, secret);
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signature.ToString())))
            return Unauthorized(new { error = "Bad signature." });

        DialerEventCommand? cmd;
        try { cmd = JsonSerializer.Deserialize<DialerEventCommand>(body, JsonWeb); }
        catch { return BadRequest(new { error = "Invalid payload." }); }
        if (cmd is null) return BadRequest(new { error = "Invalid payload." });

        await _mediator.Send(cmd, ct);
        return Accepted();
    }

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private static string ComputeHmac(string payload, string secret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
