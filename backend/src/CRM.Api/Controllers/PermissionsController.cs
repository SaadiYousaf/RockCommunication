using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _currentUser;

    public PermissionsController(IPermissionService permissions, ICurrentUser currentUser)
    {
        _permissions = Guard.AgainstNull(permissions);
        _currentUser = Guard.AgainstNull(currentUser);
    }

    public sealed record PermissionDef(string Code, string Group);
    public sealed record SetRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);

    /// <summary>Catalog of every permission code defined in the system. Metadata only — any authenticated user who can see Role Management needs it.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<PermissionDef>> List()
        => Ok(Permissions.All
            .OrderBy(c => c)
            .Select(c => new PermissionDef(c, c.Split('.')[0]))
            .ToList());

    /// <summary>Permission codes effectively granted to the current user via their roles.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<string>>> Mine(CancellationToken ct)
    {
        if (_currentUser.UserId is null) return Unauthorized();
        // SuperAdmin bypasses the framework — surface "*" so the UI knows everything is allowed.
        if (_currentUser.Roles.Contains(CRM.Domain.Enums.Roles.SuperAdmin))
            return Ok(new[] { "*" }.Concat(Permissions.All).ToList());
        var perms = await _permissions.GetForUserAsync(_currentUser.UserId.Value, ct);
        return Ok(perms);
    }

    /// <summary>Permission codes granted to a specific role.</summary>
    [HttpGet("role/{roleId:guid}")]
    [HasPermission(Permissions.PermissionsManage)]
    public async Task<ActionResult<IReadOnlyList<string>>> ForRole(Guid roleId, CancellationToken ct)
        => Ok(await _permissions.GetForRoleAsync(roleId, ct));

    /// <summary>Replace the permission grants for a role. Used by the CEO permission matrix UI.</summary>
    [HttpPut("role/{roleId:guid}")]
    [HasPermission(Permissions.PermissionsManage)]
    public async Task<IActionResult> SetForRole(Guid roleId, [FromBody] SetRolePermissionsRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _permissions.SetForRoleAsync(roleId, req.PermissionCodes ?? Array.Empty<string>(), ct);
        return NoContent();
    }
}
