using CRM.Api.Authorization;
using CRM.Application.Chat;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IChatAttachmentStorage _storage;
    public ChatController(IMediator mediator, IChatAttachmentStorage storage)
    { _mediator = Guard.AgainstNull(mediator); _storage = Guard.AgainstNull(storage); }

    private const long MaxAttachmentBytes = 25L * 1024 * 1024;
    // Refused at the gate. Server-side enforcement; client can show a friendlier
    // hint up-front but must not be the only line of defence.
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".msi", ".ps1", ".sh", ".jar",
        ".vbs", ".vbe", ".js", ".scr", ".dll", ".lnk",
    };

    [HttpGet("rooms")]
    [HasPermission(Permissions.ChatRead)]
    public async Task<ActionResult<IReadOnlyList<ChatRoomDto>>> Rooms(CancellationToken ct)
        => Ok(await _mediator.Send(new ListMyRoomsQuery(), ct));

    [HttpPost("rooms")]
    [HasPermission(Permissions.ChatWrite)]
    public async Task<ActionResult<ChatRoomDto>> CreateRoom([FromBody] CreateRoomDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new CreateRoomCommand(dto), ct));
    }

    /// <summary>Open (or reuse) a 1:1 direct conversation with a colleague in the same office.</summary>
    [HttpPost("direct/{userId:guid}")]
    [HasPermission(Permissions.ChatWrite)]
    public async Task<ActionResult<ChatRoomDto>> StartDirect(Guid userId, CancellationToken ct)
        => Ok(await _mediator.Send(new StartDirectMessageCommand(userId), ct));

    [HttpGet("rooms/{id:guid}/messages")]
    [HasPermission(Permissions.ChatRead)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> Messages(Guid id, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _mediator.Send(new RoomMessagesQuery(id, take), ct));

    public record SendBody(string Body);

    [HttpPost("rooms/{id:guid}/messages")]
    [HasPermission(Permissions.ChatWrite)]
    public async Task<ActionResult<ChatMessageDto>> Send(Guid id, [FromBody] SendBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SendMessageCommand(id, body.Body), ct));
    }

    [HttpPost("rooms/{id:guid}/messages/upload")]
    [HasPermission(Permissions.ChatWrite)]
    [RequestSizeLimit(MaxAttachmentBytes + 1024 * 1024)] // body slack
    public async Task<ActionResult<ChatMessageDto>> SendWithAttachment(
        Guid id,
        [FromForm] string? body,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File is required.");
        if (file.Length > MaxAttachmentBytes) return BadRequest($"File exceeds {MaxAttachmentBytes / (1024 * 1024)} MB limit.");
        var ext = Path.GetExtension(file.FileName);
        if (BlockedExtensions.Contains(ext)) return BadRequest("File type not allowed.");

        await using var stream = file.OpenReadStream();
        var key = await _storage.SaveAsync(id, file.FileName, stream, ct);
        var dto = await _mediator.Send(new SendMessageCommand(
            id, body ?? string.Empty, key, file.FileName, file.ContentType, file.Length), ct);
        return Ok(dto);
    }

    [HttpGet("messages/{messageId:guid}/attachment")]
    [HasPermission(Permissions.ChatRead)]
    public async Task<IActionResult> DownloadAttachment(Guid messageId, CancellationToken ct)
    {
        var info = await _mediator.Send(new GetAttachmentQuery(messageId), ct);
        var stream = await _storage.OpenReadAsync(info.StorageKey, ct);
        // inline so images render in the bubble; the original filename is still
        // surfaced via Content-Disposition for "Save as".
        return File(stream, info.ContentType, info.FileName);
    }

    [HttpGet("unread")]
    [HasPermission(Permissions.ChatRead)]
    public async Task<ActionResult<IReadOnlyList<UnreadInfo>>> Unread(CancellationToken ct)
        => Ok(await _mediator.Send(new UnreadCountsQuery(), ct));

    [HttpPost("rooms/{id:guid}/read")]
    [HasPermission(Permissions.ChatRead)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new MarkRoomReadCommand(id), ct);
        return NoContent();
    }
}
