using CRM.Application.Common.Interfaces;
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
    private readonly IFileStorage _files;
    public HrController(IMediator mediator, IFileStorage files)
    {
        _mediator = Guard.AgainstNull(mediator);
        _files = Guard.AgainstNull(files);
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] Guid? callCenterId,
        [FromQuery] EmployeeDesignation? designation, CancellationToken ct)
        => Ok(await _mediator.Send(new ListEmployeesQuery(search, callCenterId, designation), ct));

    /// <summary>Employees with a birthday in the next N days (soonest first).</summary>
    [HttpGet("birthdays")]
    public async Task<IActionResult> Birthdays([FromQuery] int days = 30, CancellationToken ct = default)
        => Ok(await _mediator.Send(new UpcomingBirthdaysQuery(days), ct));

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

    /// <summary>Create an employee record for every existing user login that doesn't already
    /// have one. Returns how many were created.</summary>
    [HttpPost("sync-from-users")]
    public async Task<IActionResult> SyncFromUsers(CancellationToken ct)
        => Ok(new { created = await _mediator.Send(new SyncEmployeesFromUsersCommand(), ct) });

    /// <summary>Upload a photo / ID-card image for an employee. Stores the file and points the
    /// chosen image slot at it. Returns the opaque storage key.</summary>
    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(Guid id, [FromQuery] EmployeeImageKind kind, IFormFile file, CancellationToken ct)
    {
        Guard.AgainstNull(file);
        if (file.Length == 0 || !(file.ContentType?.StartsWith("image/") ?? false))
            return BadRequest("Please upload an image file.");
        await using var stream = file.OpenReadStream();
        var key = await _files.SaveAsync("hr-employees", file.FileName, stream, ct);
        await _mediator.Send(new SetEmployeeImageCommand(id, kind, key), ct);
        return Ok(new { key });
    }

    /// <summary>Streams an employee's photo / ID-card image (HR-authorized).</summary>
    [HttpGet("{id:guid}/image/{kind}")]
    public async Task<IActionResult> GetImage(Guid id, EmployeeImageKind kind, CancellationToken ct)
    {
        var key = await _mediator.Send(new GetEmployeeImageKeyQuery(id, kind), ct);
        if (string.IsNullOrEmpty(key)) return NotFound();
        var stream = await _files.OpenReadAsync(key, ct);
        return File(stream, ContentTypeFor(key));
    }

    private static string ContentTypeFor(string key) => System.IO.Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };
}
