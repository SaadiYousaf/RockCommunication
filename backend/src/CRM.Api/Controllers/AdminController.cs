using CRM.Api.Authorization;
using CRM.Application.Admin;
using CRM.Application.Common.Authorization;
using CRM.Application.Users.Commands;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    public AdminController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet("ip-allowlist")]
    [HasPermission(Permissions.IpAllowlistManage)]
    public async Task<IActionResult> ListIp(CancellationToken ct)
        => Ok(await _mediator.Send(new ListIpAllowlistQuery(), ct));

    public record IpEntryBody(string CidrOrIp, string? Note);

    [HttpPost("ip-allowlist")]
    [HasPermission(Permissions.IpAllowlistManage)]
    public async Task<IActionResult> AddIp([FromBody] IpEntryBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new AddIpAllowlistCommand(body.CidrOrIp, body.Note), ct));
    }

    [HttpDelete("ip-allowlist/{id:guid}")]
    [HasPermission(Permissions.IpAllowlistManage)]
    public async Task<IActionResult> RemoveIp(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RemoveIpAllowlistCommand(id), ct);
        return NoContent();
    }

    [HttpGet("verticals")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> ListVerticals(CancellationToken ct)
        => Ok(await _mediator.Send(new ListVerticalsQuery(), ct));

    public record CreateVerticalBody(string Name, string? Description);
    [HttpPost("verticals")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> CreateVertical([FromBody] CreateVerticalBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateVerticalCommand(body.Name, body.Description), ct));
    }

    public record UpdateVerticalBody(string Name, string? Description, bool IsActive);
    [HttpPut("verticals/{id:guid}")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpdateVertical(Guid id, [FromBody] UpdateVerticalBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateVerticalCommand(id, body.Name, body.Description, body.IsActive), ct));
    }

    [HttpGet("horizontals")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> ListHorizontals(CancellationToken ct)
        => Ok(await _mediator.Send(new ListHorizontalsQuery(), ct));

    public record CreateHorizontalBody(string Name, string? Description);
    [HttpPost("horizontals")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> CreateHorizontal([FromBody] CreateHorizontalBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateHorizontalCommand(body.Name, body.Description), ct));
    }

    public record UpdateHorizontalBody(string Name, string? Description, bool IsActive);
    [HttpPut("horizontals/{id:guid}")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpdateHorizontal(Guid id, [FromBody] UpdateHorizontalBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateHorizontalCommand(id, body.Name, body.Description, body.IsActive), ct));
    }

    [HttpGet("commission-config")]
    [HasPermission(Permissions.CommissionsView)]
    public async Task<IActionResult> CommissionConfig(CancellationToken ct)
        => Ok(await _mediator.Send(new ListCommissionConfigQuery(), ct));

    [HttpPut("commission-config")]
    [HasPermission(Permissions.PayrollProcess)]
    public async Task<IActionResult> UpsertCommissionConfig([FromBody] AgencyCommissionRuleDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new UpsertCommissionConfigCommand(dto), ct));
    }

    public record RolesBody(string[] Roles);
    [HttpPut("users/{id:guid}/roles")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> UpdateRoles(Guid id, [FromBody] RolesBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateUserRolesCommand(id, body.Roles), ct));
    }

    public record ActiveBody(bool IsActive);
    [HttpPut("users/{id:guid}/active")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] ActiveBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        var result = await _mediator.Send(new SetActiveCommand(id, body.IsActive), ct);
        // Force every cached "is this user active?" lookup to re-check the DB on the
        // next request — without this, the 30-second cache lets a deactivated user
        // keep loading pages until the entry expires.
        Middleware.ActiveUserGateMiddleware.Invalidate(id);
        return Ok(result);
    }

    public record ResetPwBody(string NewPassword);
    [HttpPut("users/{id:guid}/password")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> ResetPw(Guid id, [FromBody] ResetPwBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new ResetPasswordCommand(id, body.NewPassword), ct);
        return NoContent();
    }

    // ---- Audit log ----
    [HttpGet("audit")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Audit(
        [FromQuery] string? entityName,
        [FromQuery] string? entityId,
        [FromQuery] string? action,
        [FromQuery] string? userId,
        [FromQuery] DateTime? after,
        [FromQuery] DateTime? before,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListAuditQuery(
            entityName, entityId, action, userId, after, before, search, skip, take), ct));

    [HttpGet("audit/filters")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> AuditFilters(CancellationToken ct)
        => Ok(await _mediator.Send(new DistinctAuditFiltersQuery(), ct));
}
