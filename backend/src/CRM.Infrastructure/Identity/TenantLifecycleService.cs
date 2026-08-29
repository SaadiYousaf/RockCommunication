using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Infrastructure.Identity;

/// <summary>
/// Disabling a tenant, done properly — see <see cref="ITenantLifecycleService"/> for why.
///
/// Everything happens inside ONE transaction on the shared <see cref="AppDbContext"/>. Identity uses
/// that same context, so agencies, call centres, user rows, refresh tokens and audit entries all
/// commit or roll back together; there is no state where the agency is off but its users are still
/// signed in. Precedent for the pattern: RecordSale.
/// </summary>
public class TenantLifecycleService : ITenantLifecycleService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly ICurrentUser _current;
    private readonly INotificationDispatcher _notify;
    private readonly IActiveUserCache _activeUsers;

    public TenantLifecycleService(
        AppDbContext db,
        IJwtTokenService jwt,
        ICurrentUser current,
        INotificationDispatcher notify,
        IActiveUserCache activeUsers)
    {
        _db = Guard.AgainstNull(db);
        _jwt = Guard.AgainstNull(jwt);
        _current = Guard.AgainstNull(current);
        _notify = Guard.AgainstNull(notify);
        _activeUsers = Guard.AgainstNull(activeUsers);
    }

    private const string RestoredTitle = "Access restored";
    private const string RestoredBody =
        "Your organization's access has been restored. You can sign in again.";

    public async Task<TenantCascadeResult> SetAgencyActiveAsync(Guid agencyId, bool isActive, CancellationToken ct = default)
    {
        // Guid.Empty is the platform tenant that SuperAdmins and central agents live in. Cascading
        // over it would disable the operators who administer the system.
        if (agencyId == Guid.Empty) throw new ForbiddenAccessException();

        var agency = await _db.Agencies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == agencyId && !a.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Agency), agencyId);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        agency.IsActive = isActive;

        // Deliberately NOT short-circuiting when the flag already matches: re-running the sweep is
        // how a partially-applied earlier attempt converges. The sweeps below are no-ops when
        // nothing matches, so a repeat call is cheap and changes nothing.
        var centres = isActive
            ? await _db.CallCenters.IgnoreQueryFilters()
                .Where(c => c.AgencyId == agencyId && !c.IsDeleted && c.CascadeDisabledAt != null).ToListAsync(ct)
            : await _db.CallCenters.IgnoreQueryFilters()
                .Where(c => c.AgencyId == agencyId && !c.IsDeleted && c.IsActive).ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var c in centres)
        {
            c.IsActive = isActive;
            // The marker records "off because the agency is off", so enable restores exactly these
            // and leaves individually-disabled centres alone.
            c.CascadeDisabledAt = isActive ? null : now;
        }

        var userIds = await AffectedUserIdsAsync(u => u.AgencyId == agencyId, isActive, ct);
        await ApplyToUsersAsync(userIds, isActive, now, ct);

        WriteAudit(agencyId, nameof(Agency), agencyId, isActive, centres.Count, userIds.Count);

        await _db.SaveChangesAsync(ct);

        // Revoking refresh tokens stops them minting a NEW session; the per-request account gate is
        // what stops the session they already hold. Both are needed.
        if (!isActive) await _jwt.RevokeAllForAgencyAsync(agencyId, ct);

        await tx.CommitAsync(ct);

        await AfterCommitAsync(userIds, isActive, ct);
        return new TenantCascadeResult(centres.Count, userIds.Count);
    }

    public async Task<TenantCascadeResult> SetCallCenterActiveAsync(Guid callCenterId, bool isActive, CancellationToken ct = default)
    {
        if (callCenterId == Guid.Empty) throw new ForbiddenAccessException();

        var centre = await _db.CallCenters.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == callCenterId && !c.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(CallCenter), callCenterId);

        // Enabling a centre under a disabled agency would advertise an access the agency gate still
        // refuses — the admin would see "Active" and the agents still could not sign in.
        if (isActive)
        {
            var agencyActive = await _db.Agencies.IgnoreQueryFilters()
                .AnyAsync(a => a.Id == centre.AgencyId && a.IsActive && !a.IsDeleted, ct);
            if (!agencyActive)
                throw new ConflictException("Enable the agency before enabling this call centre.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        centre.IsActive = isActive;
        // Disabling a centre on its own is an individual decision, so it carries no cascade marker —
        // re-enabling its agency later must not silently switch it back on.
        centre.CascadeDisabledAt = null;

        var userIds = await AffectedUserIdsAsync(u => u.CallCenterId == callCenterId, isActive, ct);
        await ApplyToUsersAsync(userIds, isActive, now, ct);

        WriteAudit(centre.AgencyId, nameof(CallCenter), callCenterId, isActive, 1, userIds.Count);

        await _db.SaveChangesAsync(ct);

        if (!isActive) await _jwt.RevokeAllForCallCenterAsync(callCenterId, ct);

        await tx.CommitAsync(ct);

        await AfterCommitAsync(userIds, isActive, ct);
        return new TenantCascadeResult(1, userIds.Count);
    }

    public async Task<TenantDisableImpact> GetAgencyDisableImpactAsync(Guid agencyId, CancellationToken ct = default)
    {
        if (agencyId == Guid.Empty) throw new ForbiddenAccessException();

        var centres = await _db.CallCenters.IgnoreQueryFilters()
            .CountAsync(c => c.AgencyId == agencyId && !c.IsDeleted && c.IsActive, ct);

        var userIds = await AffectedUserIdsAsync(u => u.AgencyId == agencyId, isActive: false, ct);

        var sessions = userIds.Count == 0 ? 0 : await _db.RefreshTokens
            .CountAsync(t => userIds.Contains(t.UserId) && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow, ct);

        return new TenantDisableImpact(centres, userIds.Count, sessions);
    }

    /// <summary>
    /// The users a sweep will actually touch. On disable that is everyone currently active; on
    /// enable it is only those the cascade itself switched off.
    ///
    /// SuperAdmins are excluded unconditionally. Their rows carry the empty agency so the predicate
    /// already misses them, but a mis-seeded platform account must never be collateral damage in a
    /// tenant shutdown — the cost of the extra check is one join.
    /// </summary>
    private async Task<List<Guid>> AffectedUserIdsAsync(
        System.Linq.Expressions.Expression<Func<ApplicationUser, bool>> scope, bool isActive, CancellationToken ct)
    {
        var superAdminIds = _db.UserRoles
            .Where(ur => _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.SuperAdmin))
            .Select(ur => ur.UserId);

        var q = _db.Users.Where(scope).Where(u => !superAdminIds.Contains(u.Id));

        q = isActive
            ? q.Where(u => u.CascadeDisabledAt != null)
            : q.Where(u => u.IsActive);

        return await q.Select(u => u.Id).ToListAsync(ct);
    }

    /// <summary>
    /// Set-based so a large tenant is one statement rather than N tracked updates. ApplicationUser is
    /// not a BaseEntity, so it produces no audit rows either way — nothing is lost by bypassing the
    /// change tracker, and the explicit audit entry records the counts.
    /// </summary>
    private async Task ApplyToUsersAsync(List<Guid> userIds, bool isActive, DateTime now, CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        await _db.Users.Where(u => userIds.Contains(u.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, isActive)
                .SetProperty(u => u.CascadeDisabledAt, isActive ? (DateTime?)null : now), ct);
    }

    /// <summary>
    /// An explicit entry, because the automatic interceptor cannot serve this purpose: Agency is not
    /// a tenant-scoped entity, so its auto row carries a null AgencyId and the tenant can never see
    /// the record of its own shutdown. "Disabled"/"Enabled" also reads as a distinct action in the
    /// audit filter, where "Updated" is indistinguishable from a rename.
    /// </summary>
    private void WriteAudit(Guid agencyId, string entityName, Guid entityId, bool isActive, int centres, int users)
    {
        _db.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId == Guid.Empty ? null : agencyId,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            Action = isActive ? "Enabled" : "Disabled",
            UserId = _current.UserId?.ToString(),
            UserName = _current.UserName,
            Changes = JsonSerializer.Serialize(new
            {
                callCentersAffected = centres,
                usersAffected = users,
            }),
            IpAddress = _current.IpAddress,
        });
    }

    /// <summary>
    /// Runs only once the transaction has committed, so nothing here can observe or advertise a
    /// state that later rolls back.
    /// </summary>
    private async Task AfterCommitAsync(List<Guid> userIds, bool isActive, CancellationToken ct)
    {
        if (userIds.Count == 0) return;

        // Drop the cached "this account is fine" entries so the very next request re-reads the
        // database. Without this the accounts stay usable for the remainder of the cache TTL.
        _activeUsers.Invalidate(userIds);

        // Only on restore. A disabled user cannot sign in to read a notice, so sending one per
        // account would be pure noise — the audit entry is the durable record of a shutdown.
        if (!isActive) return;

        var restored = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AgencyId }).ToListAsync(ct);

        foreach (var u in restored)
        {
            try
            {
                await _notify.DispatchAsync(
                    new NotificationPayload(u.AgencyId, u.Id, RestoredTitle, RestoredBody, "/profile"),
                    new[] { NotificationChannelType.InApp }, ct);
            }
            catch { /* graceful — a notification failure must never undo a completed restore */ }
        }
    }
}
