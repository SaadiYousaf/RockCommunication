using CRM.Application.Queues;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>Live per-user work-queue counts for the sidebar "N new" badges. No permission gate —
/// the handler scopes to the caller; the nav only shows the badges the user's role can act on.</summary>
[ApiController]
[Authorize]
[Route("api/work-queues")]
public class WorkQueuesController : ControllerBase
{
    private readonly IMediator _mediator;
    public WorkQueuesController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet("counts")]
    public async Task<ActionResult<QueueCountsDto>> Counts(CancellationToken ct)
        => Ok(await _mediator.Send(new QueueCountsQuery(), ct));
}
