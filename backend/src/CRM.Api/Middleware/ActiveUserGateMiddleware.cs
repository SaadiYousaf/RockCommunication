using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Infrastructure.Identity;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace CRM.Api.Middleware;

/// <summary>
/// "Force-logout on deactivation" gate.
///
/// JWTs are stateless — once issued they remain valid until they expire (15 min
/// default). When an admin deactivates a user we already revoke their refresh
/// tokens, but their currently-loaded browser tab keeps working until the
/// short-lived access token expires.
///
/// This middleware closes that gap: for every authenticated request, it looks
/// up <c>ApplicationUser.IsActive</c> and returns <c>401</c> the moment the
/// account is flipped off. The frontend's auth pipeline already treats 401
/// as "session ended, send to /login".
///
/// Performance: results are cached in-process for 30 seconds keyed by user id,
/// so the lookup is effectively free in the steady state. The cache is short
/// enough that deactivation propagates to all concurrent requests within a
/// few seconds.
/// </summary>
public class ActiveUserGateMiddleware
{
    // user-id → (isActive, expires-at-utc)
    private static readonly ConcurrentDictionary<Guid, (bool Active, DateTime ExpiresAt)> _cache = new();
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
            var isActive = await db.Set<ApplicationUser>()
                .Where(u => u.Id == uid)
                .Select(u => (bool?)u.IsActive)
                .FirstOrDefaultAsync(ctx.RequestAborted);

            // User row missing → treat as inactive (deleted out from under them).
            entry = (isActive ?? false, now.Add(_ttl));
            _cache[uid] = entry;
        }

        if (!entry.Active)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers["WWW-Authenticate"] = "Bearer error=\"account_disabled\"";
            await ctx.Response.WriteAsJsonAsync(new
            {
                title = "Account disabled",
                status = 401,
                detail = "Your account has been deactivated by an administrator.",
            });
            return;
        }

        await _next(ctx);
    }

    /// <summary>Drops the cached entry so the next request re-checks the DB.</summary>
    public static void Invalidate(Guid userId) => _cache.TryRemove(userId, out _);
}
