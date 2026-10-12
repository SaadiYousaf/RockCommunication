using CRM.Application.Hr;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// End-of-day reflections, asked at sign-out: what the person learned, and whether they feel they
/// did justice with the day.
///
/// Anyone signed in may file their own and read their own history. Reading other people's is
/// restricted to leadership in the handler (<see cref="ShiftReflectionAccess"/>) rather than here,
/// because an unprivileged caller is not refused — they are narrowed to themselves.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/reflections")]
public class HrReflectionsController : ControllerBase
{
    private readonly IMediator _mediator;
    public HrReflectionsController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] Guid? userId, [FromQuery] string? scope, CancellationToken ct)
        => Ok(await _mediator.Send(new ListShiftReflectionsQuery(from, to, userId, scope), ct));

    public record SubmitBody(string? Learned = null, bool? DidJustice = null, string? JusticeNote = null);

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(
            new SubmitShiftReflectionCommand(body.Learned, body.DidJustice, body.JusticeNote), ct));
    }
}
