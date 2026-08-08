using CRM.Application.Hr;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>HR recruitment — interview / candidate records. HR / Admin / SuperAdmin only.</summary>
[ApiController]
[Authorize]
[Route("api/hr/interviews")]
public class HrInterviewsController : ControllerBase
{
    private readonly IMediator _mediator;
    public HrInterviewsController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] OfferStatus? status, CancellationToken ct)
        => Ok(await _mediator.Send(new ListInterviewsQuery(search, status), ct));

    /// <summary>Candidates with a training scheduled in the next N days (soonest first).</summary>
    [HttpGet("trainings")]
    public async Task<IActionResult> Trainings([FromQuery] int days = 14, CancellationToken ct = default)
        => Ok(await _mediator.Send(new UpcomingTrainingsQuery(days), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetInterviewQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InterviewInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateInterviewCommand(body), ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] InterviewInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateInterviewCommand(id, body), ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteInterviewCommand(id), ct);
        return NoContent();
    }

    public record BulkStatusBody(IReadOnlyList<Guid> Ids, OfferStatus Status);

    /// <summary>Move several candidates to one offer status at once. Returns { updated }.</summary>
    [HttpPatch("bulk-status")]
    public async Task<IActionResult> BulkStatus([FromBody] BulkStatusBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        var updated = await _mediator.Send(new BulkSetInterviewStatusCommand(body.Ids, body.Status), ct);
        return Ok(new { updated });
    }
}
