using CRM.Application.Hr;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>HR social-media handling reports — posts made + queries answered. HR/Admin/SuperAdmin.</summary>
[ApiController]
// Mirror HrAccess.EnsureHr at the door (defense in depth).
[Authorize(Roles = Roles.HR + "," + Roles.Admin + "," + Roles.SuperAdmin)]
[Route("api/hr/social-reports")]
public class HrSocialController : ControllerBase
{
    private readonly IMediator _mediator;
    public HrSocialController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct)
        => Ok(await _mediator.Send(new ListSocialReportsQuery(search), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SocialMediaInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateSocialReportCommand(body), ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SocialMediaInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateSocialReportCommand(id, body), ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteSocialReportCommand(id), ct);
        return NoContent();
    }
}
