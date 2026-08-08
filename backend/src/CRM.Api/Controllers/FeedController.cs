using CRM.Application.Common.Interfaces;
using CRM.Application.Feed;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// The team "Pulse" feed — a social timeline any authenticated user in the agency can post to
/// (text and/or an image, with a post kind like Announcement), react to, comment on, and @mention
/// teammates. Every new post notifies the whole agency. Delete is self-or-admin (enforced in handlers).
/// </summary>
[ApiController]
[Authorize]
[Route("api/feed")]
public class FeedController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileStorage _files;

    public FeedController(IMediator mediator, IFileStorage files)
    {
        _mediator = Guard.AgainstNull(mediator);
        _files = Guard.AgainstNull(files);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListFeedQuery(skip, take), ct));

    public record PostBody(string? Body, FeedPostKind Kind = FeedPostKind.Update, string? ImageKey = null);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PostBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new CreatePostCommand(body.Body ?? "", body.Kind, body.ImageKey), ct));
    }

    /// <summary>Upload an image to attach to a post. Returns the opaque storage key to pass to POST /api/feed.</summary>
    [HttpPost("image")]
    [RequestSizeLimit(FeedConstants.MaxImageBytes + 1024 * 1024)]   // slack for the multipart envelope
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
    {
        Guard.AgainstNull(file);
        if (file.Length == 0 || file.Length > FeedConstants.MaxImageBytes
            || !(file.ContentType?.StartsWith("image/") ?? false))
            return BadRequest(new { error = "Please choose an image under 8 MB." });

        await using var stream = file.OpenReadStream();
        var key = await _files.SaveAsync(FeedConstants.ImageContainer, file.FileName, stream, ct);
        return Ok(new { key });
    }

    /// <summary>Streams a post's attached image (agency-scoped access enforced in the handler). 404 if none.</summary>
    [HttpGet("{postId:guid}/image")]
    public async Task<IActionResult> Image(Guid postId, CancellationToken ct)
    {
        var key = await _mediator.Send(new GetPostImageKeyQuery(postId), ct);
        if (string.IsNullOrEmpty(key)) return NotFound();
        var stream = await _files.OpenReadAsync(key, ct);
        return File(stream, ContentTypeFor(key));
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

    public record CommentBody(string Body);

    [HttpPost("{postId:guid}/comments")]
    public async Task<IActionResult> Comment(Guid postId, [FromBody] CommentBody body, CancellationToken ct)
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

    private static string ContentTypeFor(string key) => System.IO.Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };
}
