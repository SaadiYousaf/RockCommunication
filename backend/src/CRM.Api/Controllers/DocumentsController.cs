using CRM.Application.Common.Interfaces;
using CRM.Application.Documents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// Shared Word documents / spreadsheets. Bytes are never served statically — the
/// content endpoint streams through here so the protected viewer can render them
/// while copy/print/download are disabled client-side.
/// </summary>
[ApiController]
[Authorize]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileStorage _files;
    public DocumentsController(IMediator mediator, IFileStorage files)
    { _mediator = mediator; _files = files; }

    private const long MaxBytes = 30L * 1024 * 1024; // 30 MB

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentDto>>> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListDocumentsQuery(), ct));

    [HttpPost("upload")]
    [RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<DocumentDto>> Upload(
        [FromForm] string? name, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File is required.");
        if (file.Length > MaxBytes) return BadRequest($"File exceeds {MaxBytes / (1024 * 1024)} MB limit.");
        if (!DocumentKinds.IsAllowed(file.FileName))
            return BadRequest("Only Word documents and spreadsheets are allowed.");

        await using var stream = file.OpenReadStream();
        var key = await _files.SaveAsync("documents", file.FileName, stream, ct);
        var dto = await _mediator.Send(new CreateDocumentCommand(
            string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(file.FileName) : name!,
            file.FileName, file.ContentType ?? "application/octet-stream", file.Length, key), ct);
        return Ok(dto);
    }

    /// <summary>
    /// Streams the raw document bytes for the protected client renderer. Marked
    /// no-store so browsers don't cache a copy to disk.
    /// </summary>
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var info = await _mediator.Send(new GetDocumentContentQuery(id), ct);
        var stream = await _files.OpenReadAsync(info.StorageKey, ct);
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        Response.Headers.Pragma = "no-cache";
        // inline (not attachment) — the viewer fetches & renders; it never offers "Save as".
        return File(stream, info.ContentType, enableRangeProcessing: false);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteDocumentCommand(id), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/notes")]
    public async Task<ActionResult<IReadOnlyList<DocumentNoteDto>>> Notes(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListDocumentNotesQuery(id), ct));

    public record AddNoteBody(string Body);

    [HttpPost("{id:guid}/notes")]
    public async Task<ActionResult<DocumentNoteDto>> AddNote(Guid id, [FromBody] AddNoteBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new AddDocumentNoteCommand(id, body.Body), ct));
}
