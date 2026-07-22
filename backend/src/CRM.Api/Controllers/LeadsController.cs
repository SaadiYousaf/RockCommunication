using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Leads.Commands;
using CRM.Application.Leads.Dtos;
using CRM.Application.Leads.Queries;
using CRM.Application.Ai;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/leads")]
public class LeadsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeadsController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    /// <summary>"Lead troubleshooting" — full diagnostic for why a lead may be stuck.</summary>
    [HttpGet("{id:guid}/diagnostics")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<ActionResult<LeadDiagnosticsDto>> Diagnostics(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new LeadDiagnosticsQuery(id), ct));

    [HttpGet]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<ActionResult<PagedLeadsResult>> List(
        [FromQuery] WorkflowStage? stage,
        [FromQuery] Guid? assignedUserId,
        [FromQuery] LeadDisposition? disposition,
        [FromQuery] string? state,
        [FromQuery] Guid? campaignId,
        [FromQuery] Guid? leadSourceId,
        [FromQuery] int? minScore,
        [FromQuery] DateTime? createdAfter,
        [FromQuery] DateTime? createdBefore,
        [FromQuery] string sort = "createdAt-desc",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListLeadsQuery(
            stage, assignedUserId, disposition, state, campaignId, leadSourceId,
            minScore, createdAfter, createdBefore, sort, skip, take), ct));

    [HttpGet("mine")]
    [HasPermission(Permissions.QueueRead)]
    public async Task<ActionResult<IReadOnlyList<LeadDto>>> Mine(
        [FromQuery] WorkflowStage? stage,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new MyQueueQuery(stage, take), ct));

    [HttpGet("{id:guid}/timeline")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<ActionResult<LeadTimelineDto>> Timeline(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new LeadTimelineQuery(id), ct));

    [HttpGet("search")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<ActionResult<IReadOnlyList<LeadDto>>> Search(
        [FromQuery] string? phone, [FromQuery] string? email, [FromQuery] string? name,
        [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _mediator.Send(new LeadSearchQuery(phone, email, name, take), ct));

    [HttpGet("duplicates")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<ActionResult<IReadOnlyList<DuplicateGroup>>> Duplicates(CancellationToken ct)
        => Ok(await _mediator.Send(new DuplicateScanQuery(), ct));

    [HttpPost("{id:guid}/rescore")]
    [HasPermission(Permissions.LeadsWrite)]
    public async Task<IActionResult> Rescore(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new RescoreLeadCommand(id), ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<ActionResult<LeadDetailDto>> Detail(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetLeadDetailQuery(id), ct));

    public record NotesBody(string? Notes);
    [HttpPut("{id:guid}/notes")]
    [HasPermission(Permissions.LeadsWrite)]
    public async Task<IActionResult> UpdateNotes(Guid id, [FromBody] NotesBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new UpdateLeadNotesCommand(id, body.Notes), ct);
        return NoContent();
    }

    // ---- Bulk actions ----
    public record BulkAssignBody(Guid[] LeadIds, Guid AssigneeUserId);
    [HttpPost("bulk/assign")]
    [HasPermission(Permissions.LeadsAssign)]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new BulkAssignLeadsCommand(body.LeadIds, body.AssigneeUserId), ct));
    }

    public record BulkStageBody(Guid[] LeadIds, WorkflowStage ToStage, LeadDisposition Disposition, string? Notes);
    [HttpPost("bulk/stage")]
    [HasPermission(Permissions.LeadsTransition)]
    public async Task<IActionResult> BulkStage([FromBody] BulkStageBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new BulkSetStageCommand(body.LeadIds, body.ToStage, body.Disposition, body.Notes), ct));
    }

    public record BulkCadenceBody(Guid[] LeadIds, Guid CadenceId);
    [HttpPost("bulk/enroll-cadence")]
    [HasPermission(Permissions.LeadsAssign)]
    public async Task<IActionResult> BulkEnrollCadence([FromBody] BulkCadenceBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new BulkEnrollCadenceCommand(body.LeadIds, body.CadenceId), ct));
    }

    // ---- CSV export ----
    [HttpGet("export.csv")]
    [HasPermission(Permissions.LeadsExport)]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] WorkflowStage? stage,
        [FromQuery] Guid? assignedUserId,
        [FromQuery] LeadDisposition? disposition,
        [FromQuery] string? state,
        [FromQuery] DateTime? createdAfter,
        [FromQuery] DateTime? createdBefore,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListLeadsQuery(
            stage, assignedUserId, disposition, state, null, null, null,
            createdAfter, createdBefore, "createdAt-desc", 0, 5000), ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("FirstName,LastName,Phone,Email,State,Stage,Disposition,JornayaVerified,CreatedAt");
        foreach (var l in result.Items)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(l.FirstName), Csv(l.LastName), Csv(l.PhoneNumber),
                Csv(l.Email), Csv(l.State), l.Stage.ToString(), l.Disposition.ToString(),
                l.JornayaVerified.ToString(), l.CreatedAt.ToString("o")
            }));
        }
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv", $"leads-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");

        static string Csv(string? s)
        {
            s ??= "";
            return s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? $"\"{s.Replace("\"", "\"\"")}\""
                : s;
        }
    }

    [HttpPost]
    [HasPermission(Permissions.LeadsWrite)]
    public async Task<ActionResult<LeadDto>> Create([FromBody] CreateLeadDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        var result = await _mediator.Send(new CreateLeadCommand(dto), ct);
        return CreatedAtAction(nameof(List), new { }, result);
    }

    [HttpPost("{id:guid}/transition")]
    [HasPermission(Permissions.LeadsTransition)]
    public async Task<ActionResult<LeadDto>> Transition(Guid id, [FromBody] TransitionLeadDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new TransitionLeadCommand(id, dto), ct));
    }

    public record AssignBody(string TargetRole, string Strategy = "round-robin", Guid? UserId = null);

    [HttpPost("{id:guid}/assign")]
    [HasPermission(Permissions.LeadsAssign)]
    public async Task<ActionResult<LeadDto>> Assign(Guid id, [FromBody] AssignBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new AssignLeadCommand(id, body.TargetRole, body.Strategy, body.UserId), ct));
    }
}
