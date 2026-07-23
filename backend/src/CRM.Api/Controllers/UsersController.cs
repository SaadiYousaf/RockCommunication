using CRM.Api.Authorization;
using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _user;

    public UsersController(IIdentityService identity, ICurrentUser user)
    {
        _identity = Guard.AgainstNull(identity);
        _user = Guard.AgainstNull(user);
    }

    /// <summary>
    /// List users in the caller's agency. Admins and SuperAdmins may pass an explicit
    /// <c>agencyId</c> to scope the result to a different tenant — used by the
    /// "Assign CEO" modal where the target agency isn't necessarily the caller's.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.UsersRead)]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> List(
        [FromQuery] Guid? agencyId,
        CancellationToken ct)
    {
        var agencyFilter = ResolveAgencyFilter(agencyId);
        return Ok(await _identity.ListUsersAsync(agencyFilter, ct));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.UsersRead)]
    public async Task<ActionResult<UserSummaryDto>> Get(Guid id, CancellationToken ct)
    {
        var u = await _identity.GetUserAsync(id, ct);
        if (u is null) return NotFound();
        // Tenant scoping (the Identity store has no global query filter): a non-SuperAdmin
        // may only read users in their own agency. Prevents cross-tenant PII disclosure via
        // a guessed/leaked user GUID.
        if (!_user.Roles.Contains(Roles.SuperAdmin) && u.AgencyId != _user.AgencyId)
            return NotFound();
        return Ok(u);
    }

    // Lightweight {id, userName} list used to render sender names in chat,
    // assignee names on leads, etc. — does NOT require UsersRead, since every
    // authenticated user already sees these names in shared UI surfaces.
    [HttpGet("directory")]
    public async Task<ActionResult<IReadOnlyList<object>>> Directory(
        [FromQuery] Guid? agencyId,
        CancellationToken ct)
    {
        var agencyFilter = ResolveAgencyFilter(agencyId);
        var users = await _identity.ListUsersAsync(agencyFilter, ct);
        return Ok(users.Select(u => new { id = u.Id, userName = u.UserName }).ToList());
    }

    /// <summary>
    /// Resolves the effective agency filter for a list query.
    ///   - SuperAdmin: the only cross-tenant operator — may pass any
    ///     <paramref name="requestedAgencyId"/>; null means "all agencies".
    ///   - Everyone else (including agency Admin): pinned to their own agency regardless
    ///     of the parameter, so a per-agency admin can't enumerate another tenant's users
    ///     by passing a different agencyId on the URL.
    /// </summary>
    private Guid? ResolveAgencyFilter(Guid? requestedAgencyId)
    {
        if (_user.Roles.Contains(Roles.SuperAdmin)) return requestedAgencyId; // null = all agencies
        return _user.AgencyId; // pinned to the caller's tenant
    }
}
