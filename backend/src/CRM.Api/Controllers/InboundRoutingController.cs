using CRM.Application.CallCenter;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CRM.Api.Controllers;

[ApiController]
[Route("api/webhooks/inbound")]
[EnableRateLimiting("webhook")]
public class InboundRoutingController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _config;
    public InboundRoutingController(IMediator mediator, IConfiguration config)
    {
        _mediator = Guard.AgainstNull(mediator);
        _config = Guard.AgainstNull(config);
    }
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Telephony provider hits this when a call rings the agency's number — gets a routing decision.
    /// Returns the agentUserId to ring, or instructions for queue/voicemail/IVR.
    ///
    /// Authenticated by a mandatory HMAC-SHA256 X-Signature over the raw body (fail closed):
    /// without it, an unauthenticated caller could spam live agents and pollute the CRM with
    /// placeholder leads/queued calls for any AgencyId supplied in the body.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("route")]
    public async Task<IActionResult> Route(CancellationToken ct)
    {
        var secret = _config["Webhooks:Inbound:Secret"] ?? _config["Webhooks:Dialer:Secret"];
        if (string.IsNullOrEmpty(secret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Webhook is not configured." });

        if (!Request.Headers.TryGetValue("X-Signature", out var signature) || string.IsNullOrEmpty(signature))
            return Unauthorized(new { error = "Missing X-Signature." });

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(signature.ToString())))
            return Unauthorized(new { error = "Bad signature." });

        RouteInboundCallCommand? cmd;
        try { cmd = JsonSerializer.Deserialize<RouteInboundCallCommand>(body, JsonWeb); }
        catch { return BadRequest(new { error = "Invalid payload." }); }
        if (cmd is null) return BadRequest(new { error = "Invalid payload." });

        return Ok(await _mediator.Send(cmd, ct));
    }
}
