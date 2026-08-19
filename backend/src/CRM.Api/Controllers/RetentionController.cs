using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Retention;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// Retention worklist — policies that went bad post-submission (bad bank, NSF, client cancelled,
/// declined, application error). A Retention agent sees ONLY these, within their agency/call-center
/// scope, and works them: updates the status (e.g. mark recovered) and leaves a note.
/// </summary>
[ApiController]
[Authorize]
[Route("api/retention")]
public class RetentionController : ControllerBase
{
    private readonly IMediator _mediator;

    public RetentionController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    /// <summary>The problem policies in the caller's scope, newest first.</summary>
    [HasPermission(Permissions.RetentionRead)]
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListRetentionPoliciesQuery(), ct));

    public record ResolveBody(ValidatorStatus NewStatus, string? Note);

    /// <summary>Work a policy: set its new status and (optionally) leave a note.</summary>
    [HasPermission(Permissions.RetentionWork)]
    [HttpPost("{saleId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid saleId, [FromBody] ResolveBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new ResolveRetentionCommand(saleId, body.NewStatus, body.Note), ct));
    }
}
