using CRM.Api.Services;
using CRM.Application.Hr;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>HR payroll — per-employee monthly pay (attendance + commission auto-derived, money
/// components entered by HR) and PDF salary slips. HR / Admin / SuperAdmin only.</summary>
[ApiController]
[Authorize]
[Route("api/hr/payroll")]
public class HrPayrollController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _config;

    public HrPayrollController(IMediator mediator, IConfiguration config)
    {
        _mediator = Guard.AgainstNull(mediator);
        _config = Guard.AgainstNull(config);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? callCenterId, CancellationToken ct)
        => Ok(await _mediator.Send(new ListPayrollQuery(year, month, callCenterId), ct));

    public record SaveBody(int Year, int Month, SavePayrollInput Input);

    [HttpPut("{employeeId:guid}")]
    public async Task<IActionResult> Save(Guid employeeId, [FromBody] SaveBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SavePayrollCommand(employeeId, body.Year, body.Month, body.Input), ct));
    }

    [HttpGet("slip")]
    public async Task<IActionResult> Slip([FromQuery] Guid employeeId, [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var row = await _mediator.Send(new GetPayrollSlipQuery(employeeId, year, month), ct);
        var company = _config["Company:Name"] ?? "Rock Communication";
        var pdf = PayrollSlipPdf.Build(row, company);
        return File(pdf, "application/pdf", $"salary-slip-{row.AgentCode}-{year}-{month:D2}.pdf");
    }
}
