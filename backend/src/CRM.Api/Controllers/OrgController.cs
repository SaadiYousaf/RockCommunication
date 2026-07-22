using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Org;
using CRM.Application.Users.Commands;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/org")]
public class OrgController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrgController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    /// <summary>
    /// Returns the org hierarchy — CEO → leadership → teams → members — for the
    /// caller's agency. SuperAdmin may pass <c>?agencyId=…</c> to view another tenant.
    /// </summary>
    [HttpGet("tree")]
    [HasPermission(Permissions.TeamRead)]
    public async Task<IActionResult> Tree([FromQuery] Guid? agencyId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetOrgTreeQuery(agencyId), ct));

    public record SetTeamBody(Guid? TeamId);

    /// <summary>
    /// Assign or move a user to a team. Pass <c>{ teamId: null }</c> to unassign.
    /// </summary>
    [HttpPut("users/{userId:guid}/team")]
    [HasPermission(Permissions.TeamWrite)]
    public async Task<IActionResult> SetUserTeam(Guid userId, [FromBody] SetTeamBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SetUserTeamCommand(userId, body.TeamId), ct));
    }

    public record SetLeadBody(Guid? UserId);

    /// <summary>
    /// Set or clear the team-lead user for a team.
    /// </summary>
    [HttpPut("teams/{teamId:guid}/lead")]
    [HasPermission(Permissions.TeamWrite)]
    public async Task<IActionResult> SetTeamLead(Guid teamId, [FromBody] SetLeadBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new SetTeamLeadCommand(teamId, body.UserId), ct);
        return NoContent();
    }
}
