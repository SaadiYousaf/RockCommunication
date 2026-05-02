using CRM.Api.Authorization;
using CRM.Application.Callbacks;
using CRM.Application.Common.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/callbacks")]
public class CallbacksController : ControllerBase
{
    private readonly IMediator _mediator;
    public CallbacksController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [HasPermission(Permissions.CallbacksWrite)]
    public async Task<ActionResult<CallbackDto>> Schedule([FromBody] ScheduleCallbackDto dto, CancellationToken ct)
        => Ok(await _mediator.Send(new ScheduleCallbackCommand(dto), ct));

    [HttpPost("{id:guid}/complete")]
    [HasPermission(Permissions.CallbacksWrite)]
    public async Task<ActionResult<CallbackDto>> Complete(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new CompleteCallbackCommand(id), ct));

    [HttpGet("mine")]
    [HasPermission(Permissions.CallbacksRead)]
    public async Task<ActionResult<IReadOnlyList<CallbackDto>>> Mine([FromQuery] bool includeCompleted = false, CancellationToken ct = default)
        => Ok(await _mediator.Send(new MyCallbacksQuery(includeCompleted), ct));
}
