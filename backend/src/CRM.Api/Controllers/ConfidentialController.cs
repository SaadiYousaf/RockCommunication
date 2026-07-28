using CRM.Application.Confidential;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// The agency's confidential vault of external insurance / carrier portal logins. Every
/// action is Admin / SuperAdmin only (enforced in the handlers via EnsureAdmin), and the
/// stored passwords are encrypted at rest.
/// </summary>
[ApiController]
[Authorize]
[Route("api/confidential/portal-credentials")]
public class ConfidentialController : ControllerBase
{
    private readonly IMediator _mediator;
    public ConfidentialController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    public record CredentialBody(string PortalName, string? Url, string Username, string Password, string? Notes);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListPortalCredentialsQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CredentialBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(
            new CreatePortalCredentialCommand(body.PortalName, body.Url, body.Username, body.Password, body.Notes), ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CredentialBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(
            new UpdatePortalCredentialCommand(id, body.PortalName, body.Url, body.Username, body.Password, body.Notes), ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeletePortalCredentialCommand(id), ct);
        return NoContent();
    }
}
