using CRM.Application.Notifications;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// The signed-in user's own notification inbox. No permission gate — every authenticated user has
/// notifications; the handlers scope strictly to their own UserId.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    public NotificationsController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 30, [FromQuery] bool unreadOnly = false, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListMyNotificationsQuery(take, unreadOnly), ct));

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
        => Ok(new { count = await _mediator.Send(new MyUnreadNotificationCountQuery(), ct) });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new MarkNotificationReadCommand(id), ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _mediator.Send(new MarkAllNotificationsReadCommand(), ct);
        return NoContent();
    }
}
