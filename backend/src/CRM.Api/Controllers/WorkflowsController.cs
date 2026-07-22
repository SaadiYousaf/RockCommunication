using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Workflows;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly IMediator _mediator;
    public WorkflowsController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet("rules")]
    [HasPermission(Permissions.WorkflowsView)]
    public async Task<IActionResult> List([FromQuery] string? eventType, CancellationToken ct)
        => Ok(await _mediator.Send(new ListWorkflowRulesQuery(eventType), ct));

    [HttpGet("rules/{id:guid}")]
    [HasPermission(Permissions.WorkflowsView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetWorkflowRuleQuery(id), ct));

    [HttpPut("rules")]
    [HasPermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> Upsert([FromBody] UpsertWorkflowRuleDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new UpsertWorkflowRuleCommand(dto), ct));
    }

    [HttpDelete("rules/{id:guid}")]
    [HasPermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteWorkflowRuleCommand(id), ct);
        return NoContent();
    }

    [HttpGet("event-types")]
    [HasPermission(Permissions.WorkflowsView)]
    public async Task<IActionResult> EventTypes(CancellationToken ct)
        => Ok(await _mediator.Send(new AvailableEventTypesQuery(), ct));

    [HttpGet("action-types")]
    [HasPermission(Permissions.WorkflowsView)]
    public async Task<IActionResult> ActionTypes(CancellationToken ct)
        => Ok(await _mediator.Send(new AvailableActionTypesQuery(), ct));

    [HttpGet("executions")]
    [HasPermission(Permissions.WorkflowsView)]
    public async Task<IActionResult> Executions([FromQuery] Guid? ruleId, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListExecutionsQuery(ruleId, take), ct));
}
