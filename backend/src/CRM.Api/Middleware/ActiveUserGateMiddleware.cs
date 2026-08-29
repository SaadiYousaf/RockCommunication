using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Infrastructure.Identity;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace CRM.Api.Middleware;

/// <summary>
/// The per-request account gate — "is this caller still allowed to be here?"
///
/// JWTs are stateless: once issued they stay valid until they expire (15 minutes). Revoking refresh
/// tokens stops a user minting a NEW session but does nothing about the one already in their browser.
/// This middleware is what makes access revocation take effect immediately, and it checks THREE
/// things, because any of them can end a user's access:
///
///   1. the account itself was deactivated,
///   2. their agency was disabled,
///   3. their call centre was disabled.
///
/// It used to check only (1). That was the hole: disabling an agency never wrote the user's IsActive
/// column, so everyone underneath it kept working — reads and writes — for the remainder of their
/// token. The cascade now writes that column too, but this gate checks tenant state independently so
/// enforcement does not depend on a cascade having run correctly. Belt and braces, deliberately.
///
/// Performance: one query per user per 30 seconds, cached in-process. Do not lower the TTL; the
/// cascade calls <see cref="Invalidate(Guid)"/> for every affected account the moment it commits, so
/// the propagation delay for a real disable is zero on the serving process rather than 30 seconds.
/// </summary>
public class ActiveUserGateMiddleware
{
    /// <summary>Why a caller was refused. Sent as a machine-readable field so the UI never string-matches English.</summary>
    private const string ReasonAccountDisabled = "account_disabled";
    private const string ReasonTenantDisabled = "tenant_disabled";

    private static readonly ConcurrentDictionary<Guid, (bool Active, string? Reason, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);

    private readonly RequestDelegate _next;

    public ActiveUserGateMiddleware(RequestDelegate next) => _next = Guard.AgainstNull(next);

    public async Task Invoke(HttpContext ctx, ICurrentUser current, AppDbContext db)
    {
        Guard.AgainstNull(ctx);
        // Anonymous endpoints (login, password-reset, public webhooks…) are skipped.
        if (current.UserId is null)
        {
            await _next(ctx);
            return;
        }

        var uid = current.UserId.Value;
        var now = DateTime.UtcNow;

        if (!_cache.TryGetValue(uid, out var entry) || entry.ExpiresAt <= now)
        {
            entry = await EvaluateAsync(db, uid, now, ctx.RequestAborted);
            _cache[uid] = entry;
        }

        if (!entry.Active)
        {
            var tenant = entry.Reason == ReasonTenantDisabled;
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers["WWW-Authenticate"] = $"Bearer error=\"{entry.Reason}\"";
            await ctx.Response.WriteAsJsonAsync(new
            {
                title = tenant ? "Access unavailable" : "Account disabled",
                status = 401,
                // Reuse the same copy the login path shows, so a user sees one consistent
                // explanation whether they are signed out mid-session or refused at sign-in.
                detail = tenant
                    ? TenantLoginGate.DisabledMessage
                    : "Your account is currently unavailable. Please contact your administrator for assistance.",
                reason = entry.Reason,
            });
            return;
        }

        await _next(ctx);
    }

    /// <summary>
    /// Status is read from the USER ROW rather than the JWT claims on purpose: a SuperAdmin who has
    /// scoped themselves into an agency still carries their own (empty) agency on their row, and a
    /// mid-session context switch must not change whether they are allowed in at all.
    /// </summary>
    private static async Task<(bool, string?, DateTime)> EvaluateAsync(
        AppDbContext db, Guid uid, DateTime now, CancellationToken ct)
    {
        var expires = now.Add(_ttl);

        var row = await db.Set<ApplicationUser>().IgnoreQueryFilters()
            .Where(u => u.Id == uid)
            .Select(u => new { u.IsActive, u.AgencyId, u.CallCenterId })
            .FirstOrDefaultAsync(ct);

        // User row missing → treat as inactive (deleted out from under them).
        if (row is null) return (false, ReasonAccountDisabled, expires);

        // Tenant state is evaluated BEFORE the individual flag on purpose. A cascade switches both
        // off at once, and "your organisation's access was disabled" is the truthful and more useful
        // explanation for someone who did nothing wrong — telling them their account was disabled
        // would send them chasing the wrong problem.
        //
        // Platform users (SuperAdmin / central submission agents) carry the empty agency and have no
        // tenant to be disabled with. That is what stops a SuperAdmin locking themselves out the
        // instant they disable an agency. Mirrors TenantLoginGate.
        if (row.AgencyId != Guid.Empty)
        {
            var agencyOk = await db.Set<Agency>().IgnoreQueryFilters()
                .AnyAsync(a => a.Id == row.AgencyId && a.IsActive && !a.IsDeleted, ct);
            if (!agencyOk) return (false, ReasonTenantDisabled, expires);

            if (row.CallCenterId is { } ccId && ccId != Guid.Empty)
            {
                var ccOk = await db.Set<CallCenter>().IgnoreQueryFilters()
                    .AnyAsync(c => c.Id == ccId && c.IsActive && !c.IsDeleted, ct);
                if (!ccOk) return (false, ReasonTenantDisabled, expires);
            }
        }

        if (!row.IsActive) return (false, ReasonAccountDisabled, expires);

        return (true, null, expires);
    }

    /// <summary>Drops the cached entry so the next request re-checks the database.</summary>
    public static void Invalidate(Guid userId) => _cache.TryRemove(userId, out _);
}

/// <summary>
/// Lets Infrastructure drop cache entries without referencing the API project. Registered in
/// Program.cs; the single invalidation path for both the tenant cascade and per-user admin actions.
/// </summary>
public class ActiveUserCache : IActiveUserCache
{
    public void Invalidate(Guid userId) => ActiveUserGateMiddleware.Invalidate(userId);

    public void Invalidate(IEnumerable<Guid> userIds)
    {
        if (userIds is null) return;
        foreach (var id in userIds) ActiveUserGateMiddleware.Invalidate(id);
    }
}
