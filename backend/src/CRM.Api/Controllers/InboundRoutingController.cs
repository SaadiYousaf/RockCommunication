using CRM.Application.CallCenter;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Route("api/webhooks/inbound")]
public class InboundRoutingController : ControllerBase
{
    private readonly IMediator _mediator;
    public InboundRoutingController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Telephony provider hits this when a call rings the agency's number — gets a routing decision.
    /// Returns the agentUserId to ring, or instructions for queue/voicemail/IVR.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("route")]
    public async Task<IActionResult> Route([FromBody] RouteInboundCallCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));
}
