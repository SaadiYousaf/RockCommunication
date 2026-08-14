using CRM.Application.Bugs;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// In-app bug reporting. Any signed-in user can file a bug and comment; triage (status transitions
/// and assignment) is restricted to Admins/SuperAdmin in the handlers.
/// </summary>
[ApiController]
[Authorize]
[Route("api/bugs")]
public class BugsController : ControllerBase
{
    private readonly IMediator _mediator;
    public BugsController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] BugStatus? status, [FromQuery] string? scope, CancellationToken ct)
        => Ok(await _mediator.Send(new ListBugReportsQuery(status, scope), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetBugReportQuery(id), ct));

    public record CreateBody(string Title, string Description, BugSeverity Severity = BugSeverity.Medium,
        string? PageUrl = null, string? UserAgent = null);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(
            new CreateBugReportCommand(body.Title, body.Description, body.Severity, body.PageUrl, body.UserAgent), ct));
    }

    public record StatusBody(BugStatus Status, string? Resolution = null);

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] StatusBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateBugStatusCommand(id, body.Status, body.Resolution), ct));
    }

    public record AssignBody(Guid? AssignedToUserId);

    [HttpPatch("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new AssignBugCommand(id, body.AssignedToUserId), ct));
    }

    public record CommentBody(string Comment);

    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> Comment(Guid id, [FromBody] CommentBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CommentBugCommand(id, body.Comment), ct));
    }
}
