using CRM.Application.Confidential;
using CRM.Domain.Common;
using CRM.Domain.Enums;
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
// Defense in depth: the handler's EnsureAdmin() is the business rule, but without a class-level
// gate a future handler that forgets it would expose the credential vault to any logged-in user.
[Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
[Route("api/confidential/portal-credentials")]
public class ConfidentialController : ControllerBase
{
    private readonly IMediator _mediator;
    public ConfidentialController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    public record CredentialBody(string PortalName, string? Url, string Username, string Password, string? Notes);

    /// <summary>The vault WITHOUT secrets — passwords are fetched one at a time via reveal.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListPortalCredentialsQuery(), ct));

    /// <summary>
    /// Reveal ONE credential's password. Deliberately a separate call so the whole vault is never
    /// sent in a single response, and so each disclosure is recorded in the audit trail.
    /// </summary>
    [HttpGet("{id:guid}/reveal")]
    public async Task<IActionResult> Reveal(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new RevealPortalCredentialQuery(id), ct));

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
