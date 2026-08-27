using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
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
    // User-facing notification copy. Kept as constants (not inline literals) and deliberately free of
    // internal role codes — the affected user is told their access changed, not the raw role names.
    private const string AccessChangedTitle = "Your access was updated";
    private const string AccessChangedBody =
        "An administrator updated your access. Sign out and back in for the changes to take effect.";

    private const string DeactivatedTitle = "Your account was deactivated";
    private const string DeactivatedBody =
        "An administrator deactivated your account. You have been signed out and can't sign in again until it is reactivated.";
    private const string ReactivatedTitle = "Your account was reactivated";
    private const string ReactivatedBody =
        "An administrator reactivated your account. You can sign in again.";

    private const string TeamChangedTitle = "Your team was changed";
    private const string TeamMovedBodyFormat = "An administrator moved you to the {0} team.";
    private const string TeamRemovedBody = "An administrator removed you from your team.";

    private const string CallCenterChangedTitle = "Your call center was changed";
    private const string CallCenterMovedBodyFormat = "An administrator moved you to the {0} call center.";
    private const string CallCenterRemovedBody = "An administrator removed you from your call center.";

    private const string PasswordResetTitle = "Your password was reset";
    private const string PasswordResetBody =
        "An administrator reset your password and signed you out everywhere. You'll be asked to choose a new password the next time you sign in. If this wasn't expected, contact your administrator straight away.";
    private const string PasswordResetWith2FaBody =
        "An administrator reset your password, turned off your two-step verification and signed you out everywhere. You'll be asked to choose a new password the next time you sign in, and you'll need to set two-step verification up again. If this wasn't expected, contact your administrator straight away.";

    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly ICurrentUser _current;
    private readonly INotificationDispatcher _notify;

    public UserAdminService(
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        AppDbContext db,
        IJwtTokenService jwt,
        ICurrentUser current,
        INotificationDispatcher notify)
    {
        _users = Guard.AgainstNull(users);
        _roles = Guard.AgainstNull(roles);
        _db = Guard.AgainstNull(db);
        _jwt = Guard.AgainstNull(jwt);
        _current = Guard.AgainstNull(current);
        _notify = Guard.AgainstNull(notify);
    }

    // Best-effort in-app notice to the affected user — never notify yourself, and never let a
    // notification failure fail the admin action that triggered it. Deep-links to their profile.
    private async Task NotifyTargetAsync(ApplicationUser target, string title, string body, CancellationToken ct)
    {
        if (target.Id == _current.UserId) return;
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(target.AgencyId, target.Id, title, body, "/profile"),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* graceful — notification is not critical to the admin action */ }
    }

    private Task NotifyAccessChangedAsync(ApplicationUser target, CancellationToken ct)
        => NotifyTargetAsync(target, AccessChangedTitle, AccessChangedBody, ct);

    private bool CallerIsSuperAdmin => _current.Roles?.Contains(Roles.SuperAdmin) == true;

    // Only SuperAdmin, Admin or CEO may HAND OUT an agency-admin-equivalent role (Roles.Elevated) —
    // otherwise a users.manage holder (CallCenterAdmin / ProgramManager / ProjectManager) could
    // escalate itself or others to agency-wide admin power in a single request. The same rule is
    // enforced on the register (create) path in AuthController.
    private bool CallerCanGrantElevated => Roles.CanGrantElevated(_current.Roles ?? Array.Empty<string>());

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

        // Resource-based: a call-center-pinned caller (e.g. a Call Center Admin) may only
        // manage users inside their own call center. Agency-level callers (CEO/Admin, whose
        // CallCenterId is null) keep agency-wide reach. ApplicationUser is not a TenantEntity,
        // so the global query filter does NOT cover this — the check must live here.
        if (_current.CallCenterId is { } callerCallCenter && target.CallCenterId != callerCallCenter)
            throw new ForbiddenAccessException("You can only manage users in your own call center.");

        // Rank: within their scope a caller may only manage accounts strictly BELOW their own rank
        // (or their own account). So an Admin/CEO can reset a junior's password but not a peer
        // Admin/CEO; a Call Center Admin / ProgramManager can manage their agents but not each other.
        // SuperAdmin already returned above and is unrestricted.
        if (target.Id != _current.UserId)
        {
            var callerRank = Roles.RankOf(_current.Roles ?? Array.Empty<string>());
            var targetRank = Roles.RankOf(await _users.GetRolesAsync(target));
            if (targetRank >= callerRank)
                throw new ForbiddenAccessException("You can only manage users below your own role level.");
        }
    }

    public async Task EnsureCanManageAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        await AuthorizeTargetAsync(user);
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

        // Anti-escalation: block granting an administrative role unless the caller is SuperAdmin/Admin/CEO.
        if (!CallerCanGrantElevated)
        {
            var newlyElevated = roles.Except(existing, StringComparer.OrdinalIgnoreCase)
                .Where(r => Roles.Elevated.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
            if (newlyElevated.Count > 0)
                throw new ForbiddenAccessException(
                    $"You are not permitted to assign the role(s): {string.Join(", ", newlyElevated)}.");
        }

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

        // Notify the affected user their access changed — required whenever an action touches another
        // user. Only when something actually changed, so a no-op save stays silent.
        if (toRemove.Count > 0 || toAdd.Count > 0)
            await NotifyAccessChangedAsync(user, ct);

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

        // Being switched off mid-shift (or back on) is something the user must be told about.
        // NOTE: a deactivated user can no longer sign in, so they won't SEE this in-app notice until
        // they are reactivated — we still record it so the notice is waiting for them, and so the
        // event is on their notification history either way. Reactivation is delivered normally.
        await NotifyTargetAsync(user,
            isActive ? ReactivatedTitle : DeactivatedTitle,
            isActive ? ReactivatedBody : DeactivatedBody, ct);

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

        // Account recovery: an admin reset hands the account to someone who does NOT have the
        // original owner's authenticator or email inbox. If 2FA is enabled, logging in would demand
        // a code we can't obtain — a lockout. So clear the existing enrolment; the new password alone
        // gets them in. If their role still mandates 2FA they'll enrol a FRESH device after login
        // (a new QR / OTP target they control), which re-secures the account without the lockout.
        var twoFactorCleared = user.TwoFactorEnabled;
        if (user.TwoFactorEnabled)
        {
            await _users.SetTwoFactorEnabledAsync(user, false);
            await _users.ResetAuthenticatorKeyAsync(user);
            user.PreferredTwoFactorMethod = null;
        }

        // An admin-set password is temporary by definition: force the user to choose their
        // own on next login (mirrors the invitation flow) and drop their live sessions.
        user.MustChangePassword = true;
        await _users.UpdateAsync(user);
        await _jwt.RevokeAllForUserAsync(userId, ct);

        // A password reset (and the two-step verification that went with it) is a security event on
        // the user's own account — they must hear about it so an unexpected one can be challenged.
        await NotifyTargetAsync(user, PasswordResetTitle,
            twoFactorCleared ? PasswordResetWith2FaBody : PasswordResetBody, ct);
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

        string? teamName = null;   // for the user-facing notice below
        if (teamId is { } tid)
        {
            // Reject cross-tenant moves: the team must live in the user's agency.
            var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == tid, ct)
                ?? throw new NotFoundException(nameof(Team), tid);
            if (team.AgencyId != user.AgencyId)
                throw new ConflictException("Team belongs to a different agency.");
            teamName = team.Name;
        }

        var previousTeamId = user.TeamId;
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

        // Being moved between teams changes who they report to and what they see — tell them.
        // Only on a real move, so a no-op save stays silent (mirrors UpdateRolesAsync).
        if (previousTeamId != teamId)
            await NotifyTargetAsync(user, TeamChangedTitle,
                teamName is null ? TeamRemovedBody : string.Format(TeamMovedBodyFormat, teamName), ct);

        var roles = await _users.GetRolesAsync(user);
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId, roles.ToList(), Array.Empty<string>(),
            TeamId: user.TeamId);
    }

    public async Task<UserSummaryDto> SetCallCenterAsync(Guid userId, Guid? callCenterId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        await AuthorizeTargetAsync(user);

        string? callCenterName = null;   // for the user-facing notice below
        if (callCenterId is { } ccId)
        {
            // The call center must live in the user's own agency — no cross-tenant pinning.
            var cc = await _db.CallCenters.FirstOrDefaultAsync(c => c.Id == ccId, ct)
                ?? throw new NotFoundException(nameof(CallCenter), ccId);
            if (cc.AgencyId != user.AgencyId)
                throw new ConflictException("Call center belongs to a different agency.");
            callCenterName = cc.Name;
        }

        var previousCallCenterId = user.CallCenterId;
        user.CallCenterId = callCenterId;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        // Being pinned to a different call center changes the whole scope of what they can work on.
        // Only on a real move, so a no-op save stays silent.
        if (previousCallCenterId != callCenterId)
            await NotifyTargetAsync(user, CallCenterChangedTitle,
                callCenterName is null ? CallCenterRemovedBody : string.Format(CallCenterMovedBodyFormat, callCenterName), ct);

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
