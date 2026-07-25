using CRM.Api.Authorization;
using CRM.Application.Agencies;
using CRM.Application.Common.Authorization;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainRoles = CRM.Domain.Enums.Roles;

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
    public AgenciesController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListAgenciesQuery(includeInactive), ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAgencyQuery(id), ct));

    /// <summary>CEO name + email are mandatory — the Agency CEO is provisioned with the agency.</summary>
    public record CreateAgencyBody(string Name, string? Code, string CeoName, string CeoEmail);

    [HttpPost]
    [HasPermission(Permissions.AgenciesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        var dto = await _mediator.Send(new CreateAgencyCommand(body.Name, body.Code, body.CeoName, body.CeoEmail), ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    public record UpdateAgencyBody(string Name, string? Code, bool IsActive);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateAgencyCommand(id, body.Name, body.Code, body.IsActive), ct));
    }

    public record AssignCeoBody(Guid UserId);

    [HttpPost("{id:guid}/assign-ceo")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> AssignCeo(Guid id, [FromBody] AssignCeoBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new AssignCeoCommand(id, body.UserId), ct));
    }

    // ─── Agency Panel + cross-agency Submission Agent support ───────────────────

    /// <summary>Agency options for the approval popup's Agency picker (SuperAdmin or a central Submission Agent).</summary>
    [HttpGet("options")]
    [Authorize(Roles = DomainRoles.SuperAdmin + "," + DomainRoles.Validator)]
    public async Task<IActionResult> Options(CancellationToken ct)
        => Ok(await _mediator.Send(new ListAgencyOptionsQuery(), ct));

    /// <summary>License Agents of an agency — for the panel roster and the approval popup's Agent picker.</summary>
    [HttpGet("{id:guid}/license-agents")]
    [Authorize(Roles = DomainRoles.SuperAdmin + "," + DomainRoles.Validator)]
    public async Task<IActionResult> LicenseAgents(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListAgencyLicenseAgentsQuery(id), ct));

    public record CreateLicenseAgentBody(string Name, string Email);

    [HttpPost("{id:guid}/license-agents")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CreateLicenseAgent(Guid id, [FromBody] CreateLicenseAgentBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateLicenseAgentCommand(id, body.Name, body.Email), ct));
    }

    [HttpGet("{id:guid}/call-centers")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CallCenters(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListAgencyCallCentersQuery(id), ct));

    public record CreateCallCenterInAgencyBody(string Name, string? Code, string? AdminName, string? AdminEmail);

    [HttpPost("{id:guid}/call-centers")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CreateCallCenter(Guid id, [FromBody] CreateCallCenterInAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateCallCenterInAgencyCommand(id, body.Name, body.Code, body.AdminName, body.AdminEmail), ct));
    }

    public record UpdateCallCenterInAgencyBody(string Name, string? Code, bool IsActive);

    [HttpPut("{id:guid}/call-centers/{callCenterId:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> UpdateCallCenter(Guid id, Guid callCenterId, [FromBody] UpdateCallCenterInAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateCallCenterInAgencyCommand(id, callCenterId, body.Name, body.Code, body.IsActive), ct));
    }

    [HttpGet("submission-agents")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> SubmissionAgents(CancellationToken ct)
        => Ok(await _mediator.Send(new ListSubmissionAgentsQuery(), ct));

    public record CreateSubmissionAgentBody(string Name, string Email);

    [HttpPost("submission-agents")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CreateSubmissionAgent([FromBody] CreateSubmissionAgentBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateSubmissionAgentCommand(body.Name, body.Email), ct));
    }
}
