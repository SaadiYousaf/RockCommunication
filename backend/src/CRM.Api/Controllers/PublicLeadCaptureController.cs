using CRM.Application.Lists;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CRM.Api.Controllers;

[ApiController]
[Route("api/public/leads")]
public class PublicLeadCaptureController : ControllerBase
{
    private readonly IMediator _mediator;
    public PublicLeadCaptureController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Public endpoint — no auth. HMAC-signed via X-Signature header (sha256 hex of raw body
    /// with the endpoint's stored hash as the key). Origin allowlist enforced.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{slug}")]
    public async Task<IActionResult> Capture(string slug, [FromBody] PublicLeadPayload payload, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("X-Signature", out var sig))
            return Unauthorized(new { error = "Missing X-Signature." });

        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

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
    public PublicEndpointAdminController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _mediator.Send(new ListPublicEndpointsQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePublicEndpointCommand cmd, CancellationToken ct) =>
        Ok(await _mediator.Send(cmd, ct));
}
