using CRM.Application.Hr;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>HR attendance register — mark daily status, view a day, or roll up a month.
/// HR / Admin / SuperAdmin only (enforced in the handlers).</summary>
[ApiController]
[Authorize]
[Route("api/hr/attendance")]
public class HrAttendanceController : ControllerBase
{
    private readonly IMediator _mediator;
    public HrAttendanceController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet("day")]
    public async Task<IActionResult> Day([FromQuery] DateTime date, [FromQuery] Guid? callCenterId, CancellationToken ct)
        => Ok(await _mediator.Send(new AttendanceForDateQuery(date, callCenterId), ct));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? callCenterId, CancellationToken ct)
        => Ok(await _mediator.Send(new AttendanceSummaryQuery(year, month, callCenterId), ct));

    public record MarkBody(Guid EmployeeId, DateTime Date, AttendanceStatus Status, string? Notes);

    [HttpPost("mark")]
    public async Task<IActionResult> Mark([FromBody] MarkBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new MarkAttendanceCommand(body.EmployeeId, body.Date, body.Status, body.Notes), ct);
        return NoContent();
    }
}
