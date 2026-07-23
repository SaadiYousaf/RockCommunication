using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Users.Commands;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Identity;

public class UserAdminService : IUserAdminService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly ICurrentUser _current;

    public UserAdminService(
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        AppDbContext db,
        IJwtTokenService jwt,
        ICurrentUser current)
    {
        _users = Guard.AgainstNull(users);
        _roles = Guard.AgainstNull(roles);
        _db = Guard.AgainstNull(db);
        _jwt = Guard.AgainstNull(jwt);
        _current = Guard.AgainstNull(current);
    }

    private bool CallerIsSuperAdmin => _current.Roles?.Contains(Roles.SuperAdmin) == true;

    /// <summary>
    /// Tenant + privilege guard for user-admin operations. A non-SuperAdmin caller may
    /// only act on users inside their own agency, and never on a SuperAdmin account.
    /// SuperAdmin bypasses (it is the cross-tenant operator).
    /// </summary>
    private async Task AuthorizeTargetAsync(ApplicationUser target)
    {
        if (CallerIsSuperAdmin) return;
        if (_current.AgencyId is null || target.AgencyId != _current.AgencyId)
            throw new ForbiddenAccessException("You can only manage users in your own agency.");
        if (await _users.IsInRoleAsync(target, Roles.SuperAdmin))
            throw new ForbiddenAccessException("You are not permitted to manage this account.");
    }

    public async Task<UserSummaryDto> UpdateRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        Guard.AgainstNull(roles);

        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        await AuthorizeTargetAsync(user);

        // Role allow-list. A non-SuperAdmin caller may never grant the global SuperAdmin
        // role (it bypasses both the permission framework and the multi-tenant filter).
        // Roles are never auto-created here — they must be provisioned via role management.
        if (!CallerIsSuperAdmin && roles.Any(r => string.Equals(r, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)))
            throw new ForbiddenAccessException("You are not permitted to assign the SuperAdmin role.");
        foreach (var role in roles)
            if (!await _roles.RoleExistsAsync(role))
                throw new ConflictException($"Role '{role}' does not exist.");

        var existing = await _users.GetRolesAsync(user);
        var toRemove = existing.Except(roles, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = roles.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();

        if (toRemove.Count > 0)
        {
            var rm = await _users.RemoveFromRolesAsync(user, toRemove);
            if (!rm.Succeeded) throw new ConflictException(string.Join("; ", rm.Errors.Select(e => e.Description)));
        }
        if (toAdd.Count > 0)
        {
            var add = await _users.AddToRolesAsync(user, toAdd);
            if (!add.Succeeded) throw new ConflictException(string.Join("; ", add.Errors.Select(e => e.Description)));
        }

        var assigned = await _users.GetRolesAsync(user);
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId, assigned.ToList(), Array.Empty<string>());
    }

    public async Task<UserSummaryDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        await AuthorizeTargetAsync(user);
        user.IsActive = isActive;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        // Force-logout-everywhere on deactivation: revoke every outstanding refresh
        // token AND bump the user's SecurityStamp so any short-lived access token
        // they're holding fails the next request (the JwtBearer events check
        // SecurityStamp via Identity's IUserStore on validation).
        if (!isActive)
        {
            await _jwt.RevokeAllForUserAsync(user.Id, ct);
            await _users.UpdateSecurityStampAsync(user);
        }

        var roles = await _users.GetRolesAsync(user);
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId,
            roles.ToList(), Array.Empty<string>(), IsActive: user.IsActive);
    }

    public async Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        await AuthorizeTargetAsync(user);
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded) throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<UserSummaryDto> SetPreferred2FaAsync(Guid userId, string method, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        await AuthorizeTargetAsync(user);
        user.PreferredTwoFactorMethod = method;
        await _users.UpdateAsync(user);
        var roles = await _users.GetRolesAsync(user);
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId, roles.ToList(), Array.Empty<string>());
    }

    public async Task<UserSummaryDto> SetTeamAsync(Guid userId, Guid? teamId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        // Cross-tenant guard covers the unassign (teamId == null) path too.
        await AuthorizeTargetAsync(user);

        if (teamId is { } tid)
        {
            // Reject cross-tenant moves: the team must live in the user's agency.
            var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == tid, ct)
                ?? throw new NotFoundException(nameof(Team), tid);
            if (team.AgencyId != user.AgencyId)
                throw new ConflictException("Team belongs to a different agency.");
        }

        user.TeamId = teamId;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        // If this user was a team-lead anywhere they are no longer on, clear that pointer
        // so the org tree doesn't show them as leading a team they've left.
        var oldLeads = await _db.Teams
            .Where(t => t.TeamLeadUserId == userId && (teamId == null || t.Id != teamId))
            .ToListAsync(ct);
        foreach (var t in oldLeads) t.TeamLeadUserId = null;
        if (oldLeads.Count > 0) await _db.SaveChangesAsync(ct);

        var roles = await _users.GetRolesAsync(user);
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId, roles.ToList(), Array.Empty<string>(),
            TeamId: user.TeamId);
    }

    public async Task<UserSummaryDto> SetCallCenterAsync(Guid userId, Guid? callCenterId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        await AuthorizeTargetAsync(user);

        if (callCenterId is { } ccId)
        {
            // The call center must live in the user's own agency — no cross-tenant pinning.
            var cc = await _db.CallCenters.FirstOrDefaultAsync(c => c.Id == ccId, ct)
                ?? throw new NotFoundException(nameof(CallCenter), ccId);
            if (cc.AgencyId != user.AgencyId)
                throw new ConflictException("Call center belongs to a different agency.");
        }

        user.CallCenterId = callCenterId;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        var roles = await _users.GetRolesAsync(user);
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId, roles.ToList(), Array.Empty<string>(),
            TeamId: user.TeamId, CallCenterId: user.CallCenterId);
    }

    public async Task<UserSummaryDto> SetAgencyAsync(Guid userId, Guid agencyId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        var agency = await _db.Agencies.FirstOrDefaultAsync(a => a.Id == agencyId, ct)
            ?? throw new NotFoundException(nameof(Agency), agencyId);

        if (user.AgencyId == agencyId)
        {
            // No-op: caller is already in this agency. Return the current snapshot.
            var rolesNow = await _users.GetRolesAsync(user);
            return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId,
                rolesNow.ToList(), Array.Empty<string>(), TeamId: user.TeamId);
        }

        // Moving a user to a new tenant invalidates anything that lives inside their old
        // tenant — team membership, team-lead pointer, ownership of cross-references.
        // Clear those before updating AgencyId so we don't leave dangling state.
        var oldTeamId = user.TeamId;
        user.TeamId = null;
        user.AgencyId = agencyId;
        var update = await _users.UpdateAsync(user);
        if (!update.Succeeded) throw new ConflictException(string.Join("; ", update.Errors.Select(e => e.Description)));

        // If they were leading any team back in their old agency, clear that pointer.
        var leadOf = await _db.Teams.Where(t => t.TeamLeadUserId == userId).ToListAsync(ct);
        foreach (var t in leadOf) t.TeamLeadUserId = null;
        if (leadOf.Count > 0 || oldTeamId is not null) await _db.SaveChangesAsync(ct);

        var roles = await _users.GetRolesAsync(user);
        _ = agency; // referenced for validation above
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId,
            roles.ToList(), Array.Empty<string>(), TeamId: user.TeamId);
    }

    public async Task SetTeamLeadAsync(Guid teamId, Guid? userId, CancellationToken ct = default)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
            ?? throw new NotFoundException(nameof(Team), teamId);

        if (userId is { } uid)
        {
            var user = await _users.FindByIdAsync(uid.ToString())
                ?? throw new NotFoundException("User", uid);
            if (user.AgencyId != team.AgencyId)
                throw new ConflictException("User belongs to a different agency.");
            // Auto-place the lead on the team they now lead.
            if (user.TeamId != teamId)
            {
                user.TeamId = teamId;
                await _users.UpdateAsync(user);
            }
        }

        team.TeamLeadUserId = userId;
        await _db.SaveChangesAsync(ct);
    }
}
