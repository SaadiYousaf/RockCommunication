using CRM.Api.Authorization;
using CRM.Application.Agencies;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// SuperAdmin-only management of agencies (a.k.a. call centers).
/// Agency users do not call this controller — they are scoped to their own
/// agency by the JWT and tenant filters.
/// </summary>
[ApiController]
[Authorize]
[Route("api/agencies")]
public class AgenciesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileStorage _files;
    public AgenciesController(IMediator mediator, IFileStorage files)
    {
        _mediator = Guard.AgainstNull(mediator);
        _files = Guard.AgainstNull(files);
    }

    // Customer-facing branding lives in App_Data alongside avatars/documents.
    private const string LogoContainer = "agency-logos";
    private const long MaxLogoBytes = 2 * 1024 * 1024;   // 2 MB is plenty for a logo

    [HttpGet]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListAgenciesQuery(includeInactive), ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAgencyQuery(id), ct));

    /// <summary>CEO name + email are mandatory — the Agency CEO is provisioned with the agency.</summary>
    public record CreateAgencyBody(string Name, string? Code, string CeoName, string CeoEmail);

    [HttpPost]
    [HasPermission(Permissions.AgenciesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        var dto = await _mediator.Send(new CreateAgencyCommand(body.Name, body.Code, body.CeoName, body.CeoEmail), ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    public record UpdateAgencyBody(string Name, string? Code, bool IsActive, string? SenderEmail = null,
        string? DisplayCurrency = null, decimal? ExchangeRate = null);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateAgencyCommand(id, body.Name, body.Code, body.IsActive, body.SenderEmail,
            body.DisplayCurrency, body.ExchangeRate), ct));
    }

    /// <summary>Upload / replace the agency logo shown in customer emails. Images only, under 2 MB.</summary>
    [HttpPost("{id:guid}/logo")]
    [HasPermission(Permissions.AgenciesManage)]
    [RequestSizeLimit(MaxLogoBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(Guid id, IFormFile file, CancellationToken ct)
    {
        Guard.AgainstNull(file);
        if (file.Length == 0 || file.Length > MaxLogoBytes || !(file.ContentType?.StartsWith("image/") ?? false))
            return BadRequest("Please upload an image file under 2 MB.");

        await using var stream = file.OpenReadStream();
        var key = await _files.SaveAsync(LogoContainer, file.FileName, stream, ct);
        await _mediator.Send(new SetAgencyLogoCommand(id, key), ct);
        return Ok(new { key });
    }

    /// <summary>Streams the agency logo (SuperAdmin or same-agency, enforced in the handler). 404 if none.</summary>
    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        var key = await _mediator.Send(new GetAgencyLogoKeyQuery(id), ct);
        if (string.IsNullOrEmpty(key)) return NotFound();
        try
        {
            var stream = await _files.OpenReadAsync(key, ct);
            return File(stream, ContentTypeFor(key));
        }
        catch (FileNotFoundException)
        {
            return NotFound();   // key present but bytes missing — 404, not a 500
        }
    }

    private static string ContentTypeFor(string key) => System.IO.Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    public record AssignCeoBody(Guid UserId);

    [HttpPost("{id:guid}/assign-ceo")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> AssignCeo(Guid id, [FromBody] AssignCeoBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new AssignCeoCommand(id, body.UserId), ct));
    }

    // ─── Agency Panel + cross-agency Submission Agent support ───────────────────

    /// <summary>Agency options for the approval popup's Agency picker. Gated by the same permission
    /// as validating a sale (SalesValidate) so anyone who can approve — Validator, agency Admin, CEO,
    /// SuperAdmin — can render the picker; the handler still scopes a plain agency user to their own.</summary>
    [HttpGet("options")]
    [HasPermission(Permissions.SalesValidate)]
    public async Task<IActionResult> Options(CancellationToken ct)
        => Ok(await _mediator.Send(new ListAgencyOptionsQuery(), ct));

    /// <summary>License Agents of an agency — for the panel roster and the approval popup's Agent picker.
    /// Gated by SalesValidate (matches the assign-license-agent write) so an agency Admin approving a
    /// sale can load the list; the handler enforces the caller can only read their own agency's agents.</summary>
    [HttpGet("{id:guid}/license-agents")]
    [HasPermission(Permissions.SalesValidate)]
    public async Task<IActionResult> LicenseAgents(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListAgencyLicenseAgentsQuery(id), ct));

    public record CreateLicenseAgentBody(string Name, string Email);

    [HttpPost("{id:guid}/license-agents")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CreateLicenseAgent(Guid id, [FromBody] CreateLicenseAgentBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateLicenseAgentCommand(id, body.Name, body.Email), ct));
    }

    [HttpGet("{id:guid}/call-centers")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CallCenters(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListAgencyCallCentersQuery(id), ct));

    public record CreateCallCenterInAgencyBody(string Name, string? Code, string? AdminName, string? AdminEmail);

    [HttpPost("{id:guid}/call-centers")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CreateCallCenter(Guid id, [FromBody] CreateCallCenterInAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateCallCenterInAgencyCommand(id, body.Name, body.Code, body.AdminName, body.AdminEmail), ct));
    }

    public record UpdateCallCenterInAgencyBody(string Name, string? Code, bool IsActive);

    [HttpPut("{id:guid}/call-centers/{callCenterId:guid}")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> UpdateCallCenter(Guid id, Guid callCenterId, [FromBody] UpdateCallCenterInAgencyBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateCallCenterInAgencyCommand(id, callCenterId, body.Name, body.Code, body.IsActive), ct));
    }

    [HttpGet("submission-agents")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> SubmissionAgents(CancellationToken ct)
        => Ok(await _mediator.Send(new ListSubmissionAgentsQuery(), ct));

    public record CreateSubmissionAgentBody(string Name, string Email);

    [HttpPost("submission-agents")]
    [HasPermission(Permissions.AgenciesManage)]
    public async Task<IActionResult> CreateSubmissionAgent([FromBody] CreateSubmissionAgentBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreateSubmissionAgentCommand(body.Name, body.Email), ct));
    }
}
