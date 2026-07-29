using CRM.Application.Hr;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// HR module — employee (agent) master records. Every action is HR / Admin / SuperAdmin only
/// (enforced in the handlers via EnsureHr). CNIC and bank account number are encrypted at rest.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/employees")]
public class HrController : ControllerBase
{
    private readonly IMediator _mediator;
    public HrController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] Guid? callCenterId,
        [FromQuery] EmployeeDesignation? designation, CancellationToken ct)
        => Ok(await _mediator.Send(new ListEmployeesQuery(search, callCenterId, designation), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetEmployeeQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeeInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateEmployeeCommand(body), ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EmployeeInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateEmployeeCommand(id, body), ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteEmployeeCommand(id), ct);
        return NoContent();
    }
}
