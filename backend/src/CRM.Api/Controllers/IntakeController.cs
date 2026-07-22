using CRM.Application.Intake;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// The Fronter → Verifier → Closer intake pipeline.
///   - Fronter captures the Jornaya lead form (→ Verifier queue).
///   - Verifier records a status; "Verified" pushes it to the Closer queue.
///   - Closer completes the application; "Complete and Sold" creates the sale.
///
/// Each stage is restricted to its role (Admin / SuperAdmin may oversee all three).
/// </summary>
[ApiController]
[Authorize]
[Route("api/intake")]
public class IntakeController : ControllerBase
{
    private readonly IMediator _mediator;
    public IntakeController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    // ---- Fronter ----
    [HttpPost("leads")]
    [Authorize(Roles = Roles.Fronter + "," + Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<IntakeLeadResult>> Capture([FromBody] IntakeLeadDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new CaptureIntakeLeadCommand(dto), ct));
    }

    // ---- Verifier ----
    [HttpGet("verify/queue")]
    [Authorize(Roles = Roles.Verifier + "," + Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<IReadOnlyList<IntakeQueueItem>>> VerifierQueue(CancellationToken ct)
        => Ok(await _mediator.Send(new VerifierQueueQuery(), ct));

    public record VerifierStatusBody(VerifierStatus Status, string? Notes, DateTime? CallbackAt);

    [HttpPost("verify/{leadId:guid}/status")]
    [Authorize(Roles = Roles.Verifier + "," + Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<VerifierStatusResult>> SetVerifierStatus(Guid leadId, [FromBody] VerifierStatusBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SetVerifierStatusCommand(leadId, body.Status, body.Notes, body.CallbackAt), ct));
    }

    // ---- Closer ----
    [HttpGet("close/queue")]
    [Authorize(Roles = Roles.Closer + "," + Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<IReadOnlyList<IntakeQueueItem>>> CloserQueue(CancellationToken ct)
        => Ok(await _mediator.Send(new CloserQueueQuery(), ct));

    [HttpGet("close/{leadId:guid}")]
    [Authorize(Roles = Roles.Closer + "," + Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<ClosingApplicationView>> GetClosing(Guid leadId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetClosingApplicationQuery(leadId), ct));

    public record SubmitClosingBody(CloserStatus Status, ClosingApplicationDto Application);

    [HttpPost("close/{leadId:guid}")]
    [Authorize(Roles = Roles.Closer + "," + Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<ClosingApplicationResult>> SubmitClosing(Guid leadId, [FromBody] SubmitClosingBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SubmitClosingApplicationCommand(leadId, body.Status, body.Application), ct));
    }
}
