using CRM.Application.Scheduling;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// Scheduler / meetings module — a Teams-style calendar. Available to any authenticated user;
/// the handlers scope every read/write to meetings the caller organizes or is invited to (on
/// top of the agency tenant filter). Times are UTC.
/// </summary>
[ApiController]
[Authorize]
[Route("api/meetings")]
public class MeetingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeetingsController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    /// <summary>Meetings I organize or am invited to that overlap the [from, to) window.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Ok(await _mediator.Send(new ListMeetingsQuery(from, to), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetMeetingQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MeetingInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateMeetingCommand(body), ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] MeetingInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateMeetingCommand(id, body), ct));
    }

    /// <summary>Cancel a meeting (organizer only) — notifies every invitee by email.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new CancelMeetingCommand(id), ct));

    /// <summary>RSVP to a meeting I'm invited to (Accept / Decline / Tentative).</summary>
    [HttpPost("{id:guid}/respond")]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondRequest body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new RespondToMeetingCommand(id, body.Response), ct));
    }

    /// <summary>Body for <see cref="Respond"/>.</summary>
    public record RespondRequest(CRM.Domain.Enums.AttendeeResponse Response);
}
