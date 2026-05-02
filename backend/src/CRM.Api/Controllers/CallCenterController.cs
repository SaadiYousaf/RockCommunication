using CRM.Api.Authorization;
using CRM.Application.CallCenter;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Compliance;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cc")]
public class CallCenterController : ControllerBase
{
    private readonly IMediator _mediator;
    public CallCenterController(IMediator mediator) => _mediator = mediator;

    // ---- Agent session ----

    [HttpPost("clock-in")]
    public async Task<ActionResult<AgentSessionDto>> ClockIn(CancellationToken ct)
        => Ok(await _mediator.Send(new ClockInCommand(), ct));

    [HttpPost("clock-out")]
    public async Task<ActionResult<AgentSessionDto>> ClockOut(CancellationToken ct)
        => Ok(await _mediator.Send(new ClockOutCommand(), ct));

    public record StatusBody(AgentStatus Status, string? Reason);
    [HttpPost("status")]
    public async Task<ActionResult<AgentSessionDto>> Status([FromBody] StatusBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new SetAgentStatusCommand(body.Status, body.Reason), ct));

    [HttpGet("session")]
    public async Task<ActionResult<AgentSessionDto?>> Session(CancellationToken ct)
    {
        // Return null (200 OK) when the user has no active session — easier for
        // clients to consume than a 404 they have to interpret as absence.
        var s = await _mediator.Send(new GetMySessionQuery(), ct);
        return Ok(s);
    }

    public record WrapUpBody(string WrapUpCode, string? Notes);
    [HttpPost("calls/{id:guid}/wrap-up")]
    public async Task<IActionResult> WrapUp(Guid id, [FromBody] WrapUpBody body, CancellationToken ct)
    {
        await _mediator.Send(new WrapUpCallCommand(id, body.WrapUpCode, body.Notes), ct);
        return NoContent();
    }

    [HttpGet("calls/recent")]
    public async Task<IActionResult> RecentCalls([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _mediator.Send(new MyRecentCallsQuery(take), ct));

    [HttpGet("calls")]
    public async Task<IActionResult> ListCalls(
        [FromQuery] Guid? agentUserId,
        [FromQuery] string? direction,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string sort = "initiatedAt-desc",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListCallsQuery(
            agentUserId, direction, status, from, to, sort, skip, take), ct));

    [HttpGet("calls/by-provider")]
    public async Task<IActionResult> CallByProvider(
        [FromQuery] string providerCallId, [FromQuery] string provider = "Vici", CancellationToken ct = default)
    {
        var c = await _mediator.Send(new FindCallByProviderQuery(provider, providerCallId), ct);
        return c is null ? NotFound() : Ok(c);
    }

    // ---- Wrap-up codes ----

    [HttpGet("wrap-up-codes")]
    public async Task<IActionResult> ListWrapUpCodes(CancellationToken ct)
        => Ok(await _mediator.Send(new ListWrapUpCodesQuery(), ct));

    [HttpPut("wrap-up-codes")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertWrapUpCode([FromBody] UpsertWrapUpCodeCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    // ---- DNC ----

    [HttpGet("dnc")]
    public async Task<IActionResult> ListDnc([FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListDncQuery(skip, take), ct));

    public record AddDncBody(string Phone, string? Reason, string? Source, DateTime? ExpiresAt);
    [HttpPost("dnc")]
    [HasPermission(Permissions.DncManage)]
    public async Task<IActionResult> AddDnc([FromBody] AddDncBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new AddDncCommand(body.Phone, body.Reason, body.Source ?? "Internal", body.ExpiresAt), ct));

    [HttpDelete("dnc/{id:guid}")]
    [HasPermission(Permissions.DncManage)]
    public async Task<IActionResult> RemoveDnc(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RemoveDncCommand(id), ct);
        return NoContent();
    }

    // ---- Compliance pre-flight ----

    public record ComplianceBody(string Phone, string? State);
    [HttpPost("compliance/check")]
    public async Task<IActionResult> ComplianceCheck(
        [FromBody] ComplianceBody body,
        [FromServices] IComplianceGuard guard,
        [FromServices] ICurrentUser user,
        CancellationToken ct)
    {
        if (user.AgencyId is null) return Forbid();
        var result = await guard.CheckOutboundDialAsync(user.AgencyId.Value, body.Phone, body.State, ct);
        return Ok(result);
    }

    // ---- Campaigns / LeadSources / Skills / Scripts ----

    [HttpGet("campaigns")]
    public async Task<IActionResult> ListCampaigns(CancellationToken ct)
        => Ok(await _mediator.Send(new ListCampaignsQuery(), ct));

    [HttpPut("campaigns")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertCampaign([FromBody] UpsertCampaignCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    [HttpGet("lead-sources")]
    public async Task<IActionResult> ListLeadSources(CancellationToken ct)
        => Ok(await _mediator.Send(new ListLeadSourcesQuery(), ct));

    [HttpPut("lead-sources")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertLeadSource([FromBody] UpsertLeadSourceCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    [HttpGet("skills")]
    public async Task<IActionResult> ListSkills(CancellationToken ct)
        => Ok(await _mediator.Send(new ListSkillsQuery(), ct));

    [HttpPut("skills")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertSkill([FromBody] UpsertSkillCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    public record AssignSkillBody(Guid UserId, Guid SkillId, int Proficiency);
    [HttpPost("skills/assign")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> AssignAgentSkill([FromBody] AssignSkillBody body, CancellationToken ct)
    {
        await _mediator.Send(new AssignAgentSkillCommand(body.UserId, body.SkillId, body.Proficiency), ct);
        return NoContent();
    }

    [HttpDelete("skills/assign")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> RemoveAgentSkill([FromQuery] Guid userId, [FromQuery] Guid skillId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveAgentSkillCommand(userId, skillId), ct);
        return NoContent();
    }

    [HttpGet("agents/{id:guid}/skills")]
    public async Task<IActionResult> AgentSkills(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAgentSkillsQuery(id), ct));

    [HttpGet("scripts")]
    public async Task<IActionResult> ListScripts(
        [FromQuery] WorkflowStage? stage, [FromQuery] string? role, [FromQuery] Guid? campaignId, CancellationToken ct)
        => Ok(await _mediator.Send(new ListScriptsQuery(stage, role, campaignId), ct));

    [HttpPut("scripts")]
    [HasPermission(Permissions.ScriptsManage)]
    public async Task<IActionResult> UpsertScript([FromBody] UpsertScriptCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    // ---- Supervisor ----

    [HttpGet("supervisor/live")]
    [HasPermission(Permissions.SupervisorView)]
    public async Task<IActionResult> LiveBoard(CancellationToken ct)
        => Ok(await _mediator.Send(new LiveAgentBoardQuery(), ct));

    public record ForceStatusBody(AgentStatus Status, string? Reason);
    [HttpPost("supervisor/agents/{id:guid}/force-status")]
    [HasPermission(Permissions.SupervisorControl)]
    public async Task<IActionResult> ForceStatus(Guid id, [FromBody] ForceStatusBody body, CancellationToken ct)
    {
        await _mediator.Send(new ForceAgentStatusCommand(id, body.Status, body.Reason), ct);
        return NoContent();
    }

    public record CoachBody(string Mode);
    [HttpPost("supervisor/agents/{id:guid}/coach")]
    [HasPermission(Permissions.SupervisorControl)]
    public async Task<IActionResult> Coach(Guid id, [FromBody] CoachBody body, CancellationToken ct)
    {
        await _mediator.Send(new CoachCallCommand(id, body.Mode), ct);
        return NoContent();
    }
}
