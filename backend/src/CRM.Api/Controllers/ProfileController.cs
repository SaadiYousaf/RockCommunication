using CRM.Application.Common.Interfaces;
using CRM.Application.Profile;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// Self-service user profile — like an MS / Office account. The org-owned fields (name, username,
/// email, roles, designation, team, call centre, agency) are READ-ONLY here and have no write
/// path; only the personal fields (avatar, phone, location, bio) are editable, and only on the
/// caller's OWN profile. Viewing another user is confined to the same agency (or SuperAdmin),
/// enforced in the handlers.
/// </summary>
[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileStorage _files;

    public ProfileController(IMediator mediator, IFileStorage files)
    {
        _mediator = Guard.AgainstNull(mediator);
        _files = Guard.AgainstNull(files);
    }

    /// <summary>The signed-in user's own profile.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMyProfileQuery(), ct));

    /// <summary>Update the caller's editable personal fields (phone / location / bio). Self-only.</summary>
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileInput body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateMyProfileCommand(body), ct));
    }

    /// <summary>Upload / replace the caller's avatar. Rejects non-images and files over 5 MB,
    /// stores the bytes via <see cref="IFileStorage"/>, then points the profile at the key.</summary>
    [HttpPost("me/avatar")]
    [RequestSizeLimit(ProfileConstants.MaxAvatarBytes + 1024 * 1024)]   // slack for the multipart envelope; the byte cap is enforced on file.Length below
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        Guard.AgainstNull(file);
        if (file.Length == 0 || file.Length > ProfileConstants.MaxAvatarBytes
            || !(file.ContentType?.StartsWith("image/") ?? false))
            return BadRequest("Please upload an image file under 5 MB.");

        await using var stream = file.OpenReadStream();
        var key = await _files.SaveAsync(ProfileConstants.AvatarContainer, file.FileName, stream, ct);
        await _mediator.Send(new SetMyAvatarCommand(key), ct);
        return Ok(new { key });
    }

    /// <summary>View another user's profile — same-agency-scoped in the handler.</summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetUserProfileQuery(userId), ct));

    /// <summary>Streams a user's avatar (same-agency access enforced in the handler). 404 if none.</summary>
    [HttpGet("{userId:guid}/avatar")]
    public async Task<IActionResult> GetAvatar(Guid userId, CancellationToken ct)
    {
        var key = await _mediator.Send(new GetAvatarKeyQuery(userId), ct);
        if (string.IsNullOrEmpty(key)) return NotFound();
        try
        {
            var stream = await _files.OpenReadAsync(key, ct);
            return File(stream, ContentTypeFor(key));
        }
        catch (FileNotFoundException)
        {
            // The DB points at an avatar whose bytes are gone (e.g. cleaned up) — 404, not a 500.
            return NotFound();
        }
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
