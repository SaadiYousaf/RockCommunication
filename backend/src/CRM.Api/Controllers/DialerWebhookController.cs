using CRM.Application.CallCenter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

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
        _mediator = mediator;
        _config = config;
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Handle([FromBody] DialerEventCommand cmd, CancellationToken ct)
    {
        var secret = _config["Webhooks:Dialer:Secret"];
        if (!string.IsNullOrEmpty(secret))
        {
            if (!Request.Headers.TryGetValue("X-Signature", out var signature))
                return Unauthorized(new { error = "Missing X-Signature." });

            // Re-read body for signature verification
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct);
            Request.Body.Position = 0;

            var computed = ComputeHmac(body, secret);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(signature.ToString())))
                return Unauthorized(new { error = "Bad signature." });
        }

        await _mediator.Send(cmd, ct);
        return Accepted();
    }

    private static string ComputeHmac(string payload, string secret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
