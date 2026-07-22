using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Identity;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly ICurrentUser _current;

    public PermissionService(AppDbContext db, UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles, ICurrentUser current)
    {
        _db = Guard.AgainstNull(db); _users = Guard.AgainstNull(users); _roles = Guard.AgainstNull(roles); _current = Guard.AgainstNull(current);
    }

    private bool CallerIsSuperAdmin => _current.Roles?.Contains(Roles.SuperAdmin) == true;

    /// <summary>
    /// A non-SuperAdmin may only EDIT permissions of a role owned by their own agency —
    /// never a global system template (AgencyId == null, shared across all tenants) and
    /// never another agency's role. Otherwise editing a shared role escalates every user
    /// of that role in every tenant.
    /// </summary>
    private void EnsureCanEditRole(ApplicationRole role)
    {
        if (CallerIsSuperAdmin) return;
        if (role.AgencyId is null || role.AgencyId != _current.AgencyId)
            throw new ForbiddenAccessException("You are not permitted to modify this role.");
    }

    /// <summary>Read is allowed for system templates (shared) and the caller's own-agency roles.</summary>
    private void EnsureCanReadRole(ApplicationRole role)
    {
        if (CallerIsSuperAdmin) return;
        if (role.AgencyId is not null && role.AgencyId != _current.AgencyId)
            throw new ForbiddenAccessException("You are not permitted to view this role.");
    }

    public async Task<bool> HasAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        var perms = await GetForUserAsync(userId, ct);
        return perms.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Array.Empty<string>();
        var roleNames = await _users.GetRolesAsync(user);

        var roleIds = await _db.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .Select(r => r.Id).ToListAsync(ct);

        return await (from rp in _db.RolePermissions
                      join p in _db.Permissions on rp.PermissionId equals p.Id
                      where roleIds.Contains(rp.RoleId)
                      select p.Code).Distinct().ToListAsync(ct);
    }

    public async Task GrantToRoleAsync(string roleName, IEnumerable<string> permissionCodes, CancellationToken ct = default)
    {
        Guard.AgainstNull(permissionCodes);

        var role = await _roles.FindByNameAsync(roleName);
        if (role is null) return;

        var codes = permissionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var perms = await _db.Permissions.Where(p => codes.Contains(p.Code)).ToListAsync(ct);

        foreach (var p in perms)
        {
            if (!await _db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == p.Id, ct))
                _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id });
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetForRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null) return Array.Empty<string>();
        EnsureCanReadRole(role);

        return await (from rp in _db.RolePermissions
                      join p in _db.Permissions on rp.PermissionId equals p.Id
                      where rp.RoleId == roleId
                      select p.Code).Distinct().ToListAsync(ct);
    }

    public async Task SetForRoleAsync(Guid roleId, IEnumerable<string> permissionCodes, CancellationToken ct = default)
    {
        Guard.AgainstNull(permissionCodes);

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null) return;
        EnsureCanEditRole(role);

        var codes = permissionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allPerms = await _db.Permissions.ToListAsync(ct);
        var desired = allPerms.Where(p => codes.Contains(p.Code)).Select(p => p.Id).ToHashSet();

        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        var toRemove = existing.Where(rp => !desired.Contains(rp.PermissionId)).ToList();
        if (toRemove.Count > 0) _db.RolePermissions.RemoveRange(toRemove);

        var existingIds = existing.Select(rp => rp.PermissionId).ToHashSet();
        foreach (var pid in desired.Except(existingIds))
            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = pid });

        await _db.SaveChangesAsync(ct);
    }
}
