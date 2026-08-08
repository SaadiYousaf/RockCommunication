using CRM.Application.Feed;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// The team "Pulse" feed — a social timeline any authenticated user in the agency can post to,
/// react to, comment on, and @mention teammates. Delete is self-or-admin (enforced in the handlers).
/// </summary>
[ApiController]
[Authorize]
[Route("api/feed")]
public class FeedController : ControllerBase
{
    private readonly IMediator _mediator;
    public FeedController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListFeedQuery(skip, take), ct));

    public record PostBody(string Body);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PostBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreatePostCommand(body.Body), ct));
    }

    [HttpDelete("{postId:guid}")]
    public async Task<IActionResult> Delete(Guid postId, CancellationToken ct)
    {
        await _mediator.Send(new DeletePostCommand(postId), ct);
        return NoContent();
    }

    public record ReactBody(string Emoji);

    [HttpPost("{postId:guid}/react")]
    public async Task<IActionResult> React(Guid postId, [FromBody] ReactBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new ToggleReactionCommand(postId, body.Emoji), ct);
        return NoContent();
    }

    [HttpPost("{postId:guid}/comments")]
    public async Task<IActionResult> Comment(Guid postId, [FromBody] PostBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new AddCommentCommand(postId, body.Body), ct));
    }

    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteComment(Guid commentId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteCommentCommand(commentId), ct);
        return NoContent();
    }
}
