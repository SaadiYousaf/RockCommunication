using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Roles.Commands;
using CRM.Application.Roles.Dtos;
using CRM.Application.Roles.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RolesController(IMediator mediator) => _mediator = mediator;

    public record CreateRoleRequest(string Name, IReadOnlyList<string> ModuleCodes);
    public record RenameRoleRequest(string Name);
    public record SetModulesRequest(IReadOnlyList<string> ModuleCodes);

    [HttpGet]
    [HasPermission(Permissions.RolesRead)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListRolesQuery(), ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.RolesRead)]
    public async Task<ActionResult<RoleDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetRoleQuery(id), ct));

    [HttpPost]
    [HasPermission(Permissions.RolesManage)]
    public async Task<ActionResult<RoleDto>> Create([FromBody] CreateRoleRequest req, CancellationToken ct)
    {
        var dto = await _mediator.Send(new CreateRoleCommand(req.Name, req.ModuleCodes ?? Array.Empty<string>()), ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}/name")]
    [HasPermission(Permissions.RolesManage)]
    public async Task<ActionResult<RoleDto>> Rename(Guid id, [FromBody] RenameRoleRequest req, CancellationToken ct)
        => Ok(await _mediator.Send(new RenameRoleCommand(id, req.Name), ct));

    [HttpPut("{id:guid}/modules")]
    [HasPermission(Permissions.RolesManage)]
    public async Task<ActionResult<RoleDto>> SetModules(Guid id, [FromBody] SetModulesRequest req, CancellationToken ct)
        => Ok(await _mediator.Send(new SetRoleModulesCommand(id, req.ModuleCodes ?? Array.Empty<string>()), ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteRoleCommand(id), ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/modules")]
public class ModulesController : ControllerBase
{
    private readonly IMediator _mediator;
    public ModulesController(IMediator mediator) => _mediator = mediator;

    // Module catalog is metadata only (codes/labels) — any authenticated user
    // who can see Role Management needs it. Don't gate on a permission code.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ModuleDto>>> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListModulesQuery(), ct));

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<string>>> Mine(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMyModulesQuery(), ct));
}
