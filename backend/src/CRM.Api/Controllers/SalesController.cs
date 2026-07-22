using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Application.Sales.Commands;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileStorage _files;
    public SalesController(IMediator mediator, IFileStorage files)
    { _mediator = Guard.AgainstNull(mediator); _files = Guard.AgainstNull(files); }

    [HttpPost]
    [HasPermission(Permissions.SalesRecord)]
    public async Task<ActionResult<SaleDto>> Record([FromBody] RecordSaleDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new RecordSaleCommand(dto), ct));
    }

    private const long MaxRecordingBytes = 100L * 1024 * 1024; // 100 MB
    private static readonly HashSet<string> AllowedRecordingExt = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".wav", ".m4a", ".ogg", ".webm", ".aac", ".mp4" };

    public record RecordingUploadResult(string Key, string FileName, long Size);

    /// <summary>
    /// Uploads a verification recording ahead of recording a sale (used when the
    /// banking code is 198). Returns an opaque key the closer then passes in the
    /// <c>RecordingKey</c> field of the sale payload.
    /// </summary>
    [HttpPost("recording-upload")]
    [HasPermission(Permissions.SalesRecord)]
    [RequestSizeLimit(MaxRecordingBytes + 1024 * 1024)]
    public async Task<ActionResult<RecordingUploadResult>> UploadRecording(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File is required.");
        if (file.Length > MaxRecordingBytes) return BadRequest($"Recording exceeds {MaxRecordingBytes / (1024 * 1024)} MB limit.");
        var ext = Path.GetExtension(file.FileName);
        if (!AllowedRecordingExt.Contains(ext)) return BadRequest("Only audio/video recordings are allowed.");

        await using var stream = file.OpenReadStream();
        var key = await _files.SaveAsync("sale-recordings", file.FileName, stream, ct);
        return Ok(new RecordingUploadResult(key, file.FileName, file.Length));
    }

    [HttpGet]
    [HasPermission(Permissions.SalesRead)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? closerUserId,
        [FromQuery] string? carrier,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string sort = "soldAt-desc",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new CRM.Application.Sales.Queries.ListSalesQuery(
            closerUserId, carrier, status, from, to, sort, skip, take), ct));

    public record ValidateBody(bool Approve, string? Notes);

    [HttpPost("{id:guid}/validate")]
    [HasPermission(Permissions.SalesValidate)]
    public async Task<ActionResult<SaleDto>> Validate(Guid id, [FromBody] ValidateBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new ValidateSaleCommand(id, body.Approve, body.Notes), ct));
    }

    [HttpPost("{id:guid}/fund")]
    [HasPermission(Permissions.SalesFund)]
    public async Task<ActionResult<SaleDto>> Fund(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new FundSaleCommand(id), ct));

    public record SelfValidateBody(string? Notes);
    [HttpPost("{id:guid}/self-validate")]
    [HasPermission(Permissions.SalesValidate)]
    public async Task<ActionResult<SaleDto>> SelfValidate(Guid id, [FromBody] SelfValidateBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SelfValidateSaleCommand(id, body.Notes), ct));
    }

    [HttpGet("payroll-export")]
    [HasPermission(Permissions.PayrollProcess)]
    public async Task<IActionResult> ExportCsv(
        [FromServices] IMediator mediator,
        [FromQuery] Guid? runId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        Guard.AgainstNull(mediator);
        var rows = await mediator.Send(new CRM.Application.Sales.Queries.ExportPayrollQuery(runId, from, to), ct);
        var csv = BuildCsv(rows);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"payroll-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private static string BuildCsv(IReadOnlyList<CRM.Application.Sales.Queries.PayrollExportRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("AgentUserName,AgentEmail,RuleName,Amount,EarnedAt,Paid");
        foreach (var r in rows)
            sb.AppendLine($"{Csv(r.AgentUserName)},{Csv(r.AgentEmail)},{Csv(r.RuleName)},{r.Amount},{r.EarnedAt:O},{r.Paid}");
        return sb.ToString();

        static string Csv(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    [HttpGet("commissions")]
    [HasPermission(Permissions.CommissionsView)]
    public async Task<ActionResult<IReadOnlyList<CommissionEntryDto>>> MyCommissions(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool? paid, CancellationToken ct)
    {
        var f = from ?? DateTime.UtcNow.AddDays(-30);
        var t = to ?? DateTime.UtcNow.AddDays(1);
        return Ok(await _mediator.Send(new MyCommissionsQuery(f, t, paid), ct));
    }

    [HttpGet("payroll-runs")]
    [HasPermission(Permissions.PayrollView)]
    public async Task<ActionResult<IReadOnlyList<PayrollRunDto>>> Runs(CancellationToken ct)
        => Ok(await _mediator.Send(new ListPayrollRunsQuery(), ct));

    public record CreatePayrollBody(DateTime PeriodStart, DateTime PeriodEnd);

    [HttpPost("payroll-runs")]
    [HasPermission(Permissions.PayrollProcess)]
    public async Task<ActionResult<PayrollRunDto>> CreateRun([FromBody] CreatePayrollBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreatePayrollRunCommand(body.PeriodStart, body.PeriodEnd), ct));
    }
}
