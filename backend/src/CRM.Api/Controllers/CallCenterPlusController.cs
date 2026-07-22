using CRM.Api.Authorization;
using CRM.Application.Cadences;
using CRM.Application.CallCenter;
using CRM.Application.Common.Authorization;
using CRM.Application.Knowledge;
using CRM.Application.Lists;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cc")]
public class CallCenterPlusController : ControllerBase
{
    private readonly IMediator _mediator;
    public CallCenterPlusController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    // ===== Lead lists =====
    [HttpGet("lists")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<IActionResult> ListLists(CancellationToken ct)
        => Ok(await _mediator.Send(new ListLeadListsQuery(), ct));

    [HttpPut("lists")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertList([FromBody] UpsertLeadListCommand cmd, CancellationToken ct)
    {
        Guard.AgainstNull(cmd);
        return Ok(await _mediator.Send(cmd, ct));
    }

    [HttpPost("lists/{id:guid}/import")]
    [HasPermission(Permissions.LeadsImport)]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ImportCsv(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file." });
        using var reader = new StreamReader(file.OpenReadStream());
        var rows = new List<CsvLeadRow>();
        var line = await reader.ReadLineAsync(ct);
        var header = (line ?? "").Split(',').Select(h => h.Trim().ToLowerInvariant()).ToList();
        int idx(string c) => header.IndexOf(c);
        var iFn = idx("firstname"); var iLn = idx("lastname"); var iPh = idx("phone");
        var iEm = idx("email"); var iSt = idx("state"); var iPc = idx("postal");
        var iSc = idx("source"); var iJj = idx("jornaya");
        if (iFn < 0 || iLn < 0 || iPh < 0)
            return BadRequest(new { error = "CSV must have firstname,lastname,phone columns." });

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            var parts = ParseCsv(line);
            string? Get(int i) => i >= 0 && i < parts.Count ? parts[i] : null;
            rows.Add(new CsvLeadRow(
                Get(iFn) ?? "", Get(iLn) ?? "", Get(iPh) ?? "",
                Get(iEm), Get(iSt), Get(iPc), Get(iSc), Get(iJj)));
        }
        var batch = await _mediator.Send(new ImportLeadsCommand(id, rows, file.FileName), ct);
        return Ok(batch);
    }

    [HttpGet("lists/{id:guid}/imports")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<IActionResult> ListImports(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListImportBatchesQuery(id), ct));

    private static List<string> ParseCsv(string line)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (var c in line)
        {
            if (c == '"') inQuote = !inQuote;
            else if (c == ',' && !inQuote) { parts.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        parts.Add(sb.ToString().Trim());
        return parts;
    }

    // ===== Cadences =====
    [HttpGet("cadences")]
    public async Task<IActionResult> ListCadences(CancellationToken ct)
        => Ok(await _mediator.Send(new ListCadencesQuery(), ct));

    [HttpPut("cadences")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertCadence([FromBody] UpsertCadenceCommand cmd, CancellationToken ct)
    {
        Guard.AgainstNull(cmd);
        return Ok(await _mediator.Send(cmd, ct));
    }

    public record EnrollBody(Guid CadenceId, Guid LeadId);
    [HttpPost("cadences/enroll")]
    public async Task<IActionResult> EnrollCadence([FromBody] EnrollBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new EnrollLeadInCadenceCommand(body.CadenceId, body.LeadId), ct);
        return NoContent();
    }

    public record StopEnrollBody(string? Reason);
    [HttpPost("cadences/enrollments/{id:guid}/stop")]
    public async Task<IActionResult> StopEnroll(Guid id, [FromBody] StopEnrollBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new StopCadenceEnrollmentCommand(id, body.Reason), ct);
        return NoContent();
    }

    [HttpGet("cadences/enrollments")]
    public async Task<IActionResult> ListEnrollments([FromQuery] Guid? cadenceId, [FromQuery] string? status,
        [FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListEnrollmentsQuery(cadenceId, status, take), ct));

    // ===== Voicemail =====
    [HttpGet("voicemails")]
    public async Task<IActionResult> ListVoicemails(CancellationToken ct)
        => Ok(await _mediator.Send(new ListVoicemailAssetsQuery(), ct));

    [HttpPut("voicemails")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertVoicemail([FromBody] UpsertVoicemailAssetCommand cmd, CancellationToken ct)
    {
        Guard.AgainstNull(cmd);
        return Ok(await _mediator.Send(cmd, ct));
    }

    public record DropBody(Guid LeadId, Guid VoicemailAssetId, Guid? CallRecordId);
    [HttpPost("voicemails/drop")]
    public async Task<IActionResult> DropVoicemail([FromBody] DropBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new DropVoicemailCommand(body.LeadId, body.VoicemailAssetId, body.CallRecordId), ct));
    }

    // ===== Inbound queues =====
    [HttpGet("queues")]
    public async Task<IActionResult> ListQueues(CancellationToken ct)
        => Ok(await _mediator.Send(new ListInboundQueuesQuery(), ct));

    [HttpPut("queues")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertQueue([FromBody] UpsertInboundQueueCommand cmd, CancellationToken ct)
    {
        Guard.AgainstNull(cmd);
        return Ok(await _mediator.Send(cmd, ct));
    }

    [HttpGet("queues/{id:guid}/ivr")]
    public async Task<IActionResult> GetIvr(Guid id, CancellationToken ct)
    {
        var menu = await _mediator.Send(new GetIvrMenuQuery(id), ct);
        return menu is null ? NotFound() : Ok(menu);
    }

    [HttpPut("queues/ivr")]
    [HasPermission(Permissions.CampaignsManage)]
    public async Task<IActionResult> UpsertIvr([FromBody] UpsertIvrMenuCommand cmd, CancellationToken ct)
    {
        Guard.AgainstNull(cmd);
        return Ok(await _mediator.Send(cmd, ct));
    }

    // ===== Call transfer + dial mode =====
    public record TransferBody(Guid TargetAgentUserId, string TransferType, string? Note);
    [HttpPost("calls/{id:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new TransferCallCommand(id, body.TargetAgentUserId, body.TransferType, body.Note), ct);
        return NoContent();
    }

    [HttpGet("dial-mode")]
    public async Task<IActionResult> DialMode([FromQuery] Guid? campaignId, CancellationToken ct)
        => Ok(new { mode = await _mediator.Send(new GetEffectiveDialModeQuery(campaignId), ct) });

    // ===== Wallboard + leaderboard =====
    [HttpGet("wallboard")]
    [HasPermission(Permissions.SupervisorView)]
    public async Task<IActionResult> Wallboard(CancellationToken ct)
        => Ok(await _mediator.Send(new GetWallboardQuery(), ct));

    [HttpGet("leaderboard")]
    public async Task<IActionResult> Leaderboard([FromQuery] string period = "today", CancellationToken ct = default)
        => Ok(await _mediator.Send(new GetLeaderboardQuery(period), ct));
}
