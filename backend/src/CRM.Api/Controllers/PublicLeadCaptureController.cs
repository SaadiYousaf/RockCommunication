using CRM.Application.Lists;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Text.Json;

namespace CRM.Api.Controllers;

[ApiController]
[Route("api/public/leads")]
public class PublicLeadCaptureController : ControllerBase
{
    private readonly IMediator _mediator;
    public PublicLeadCaptureController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Public endpoint — no auth. HMAC-signed via X-Signature header (sha256 hex of the raw
    /// body with the endpoint's stored hash as the key). The signature is MANDATORY.
    /// We read the raw body ourselves (no [FromBody]) so the HMAC is verified over the exact
    /// bytes; [FromBody] would consume the stream and leave the signature checked over nothing.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("webhook")]
    [HttpPost("{slug}")]
    public async Task<IActionResult> Capture(string slug, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("X-Signature", out var sig) || string.IsNullOrEmpty(sig))
            return Unauthorized(new { error = "Missing X-Signature." });

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;
        if (string.IsNullOrWhiteSpace(body)) return BadRequest(new { error = "Empty body." });

        PublicLeadPayload? payload;
        try { payload = JsonSerializer.Deserialize<PublicLeadPayload>(body, JsonWeb); }
        catch { return BadRequest(new { error = "Invalid payload." }); }
        if (payload is null) return BadRequest(new { error = "Invalid payload." });

        try
        {
            var leadId = await _mediator.Send(new CapturePublicLeadCommand(slug, sig.ToString(), payload, body), ct);
            return Accepted(new { leadId });
        }
        catch (Application.Common.Exceptions.ForbiddenAccessException) { return Unauthorized(new { error = "Bad signature." }); }
        catch (Application.Common.Exceptions.ConflictException ex) { return Conflict(new { error = ex.Message }); }
        catch (Application.Common.Exceptions.NotFoundException) { return NotFound(); }
    }
}

[ApiController]
[Authorize]
[Route("api/admin/public-endpoints")]
public class PublicEndpointAdminController : ControllerBase
{
    private readonly IMediator _mediator;
    public PublicEndpointAdminController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _mediator.Send(new ListPublicEndpointsQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePublicEndpointCommand cmd, CancellationToken ct)
    {
        Guard.AgainstNull(cmd);
        return Ok(await _mediator.Send(cmd, ct));
    }
}
