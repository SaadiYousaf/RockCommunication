using CRM.Api.Authorization;
using CRM.Application.Agencies;
using CRM.Application.Common.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// SuperAdmin-only management of agencies (a.k.a. call centers).
/// Agency users do not call this controller — they are scoped to their own
/// agency by the JWT and tenant filters.
/// </summary>
[ApiController]
[Authorize]
[Route("api/agencies")]
public class AgenciesController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgenciesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListAgenciesQuery(includeInactive), ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAgencyQuery(id), ct));

    public record CreateAgencyBody(string Name, string? Code);

    [HttpPost]
    [HasPermission(Permissions.AgenciesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateAgencyBody body, CancellationToken ct)
    {
        var dto = await _mediator.Send(new CreateAgencyCommand(body.Name, body.Code), ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    public record UpdateAgencyBody(string Name, string? Code, bool IsActive);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAgencyBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new UpdateAgencyCommand(id, body.Name, body.Code, body.IsActive), ct));

    public record AssignCeoBody(Guid UserId);

    [HttpPost("{id:guid}/assign-ceo")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> AssignCeo(Guid id, [FromBody] AssignCeoBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new AssignCeoCommand(id, body.UserId), ct));
}
