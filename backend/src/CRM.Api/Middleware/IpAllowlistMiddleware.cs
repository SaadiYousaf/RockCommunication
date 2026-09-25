using CRM.Api.Services;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace CRM.Api.Middleware;

/// <summary>
/// Restricts the application to a set of approved networks — the office, typically — so an account
/// whose password leaks still cannot be used from anywhere else.
///
/// Three things make this safe to switch on:
///
/// SUPERADMIN IS EXEMPT. Whoever administers the allowlist has to be able to reach it from outside
/// the allowlisted network, or one wrong entry locks every person out of the platform permanently
/// with no way back in. The exemption is what makes the feature recoverable, and it is why this
/// middleware runs AFTER authentication: before it, there is no identity to exempt.
///
/// AN EMPTY LIST ALLOWS EVERYTHING. The feature is off until somebody deliberately adds an entry,
/// so deploying it changes nothing for an installation that is not using it.
///
/// MACHINE CALLERS ARE EXEMPT. Webhooks and public lead capture come from carriers and dialers on
/// addresses nobody can enumerate in advance. They authenticate by HMAC signature or endpoint
/// secret instead, so pinning them to an IP list adds nothing and would silently break intake.
/// </summary>
public class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Paths that must stay reachable from any address.
    ///
    /// The sign-in endpoints are here because identity is not yet established when they are called:
    /// blocking them would stop an exempt SuperAdmin from ever authenticating, which is the one
    /// thing this design depends on. A blocked user can therefore still sign in — and is then
    /// refused, with an explanation, on their first real request.
    /// </summary>
    private static readonly string[] AlwaysAllowed =
    {
        "/api/auth/login",
        "/api/auth/2fa/verify",
        "/api/auth/refresh",
        "/api/auth/logout",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",
        "/api/auth/email/confirm",
        "/api/auth/email/resend-confirmation",
        // Machine-to-machine, authenticated by signature rather than origin.
        "/api/webhooks/",
        "/api/public/leads",
        "/swagger",
        "/health",
    };

    public IpAllowlistMiddleware(RequestDelegate next) => _next = Guard.AgainstNull(next);

    public async Task Invoke(HttpContext ctx, AppDbContext db, IIpAllowlistCache cache)
    {
        Guard.AgainstNull(ctx);

        var path = ctx.Request.Path.Value ?? string.Empty;
        if (AlwaysAllowed.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(ctx);
            return;
        }

        // The administrator of the allowlist is never governed by it.
        if (ctx.User?.IsInRole(Roles.SuperAdmin) == true)
        {
            await _next(ctx);
            return;
        }

        var entries = await cache.GetAsync(db, ctx.RequestAborted);
        if (entries.Length == 0) { await _next(ctx); return; }

        // The real client address (CF-Connecting-IP / X-Forwarded-For), not the socket peer — behind
        // the reverse proxy that is always 127.0.0.1, which would make the loopback allowance below
        // wave everybody through.
        var ip = ClientIp.ResolveAddress(ctx);
        if (ip is null) { await _next(ctx); return; }
        if (IPAddress.IsLoopback(ip)) { await _next(ctx); return; }

        if (entries.Any(e => Match(ip, e)))
        {
            await _next(ctx);
            return;
        }

        await DenyAsync(ctx, ip);
    }

    /// <summary>
    /// problem+json with a machine-readable code, matching every other gate in the pipeline, so the
    /// front end can show a real explanation instead of a bare "Forbidden".
    ///
    /// The address is echoed back deliberately: the person reading it has to be able to tell their
    /// administrator which address to approve, and it is their own address — nothing is disclosed
    /// that the caller does not already have.
    /// </summary>
    private static async Task DenyAsync(HttpContext ctx, IPAddress ip)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            title = "This network isn't approved",
            status = StatusCodes.Status403Forbidden,
            detail = $"You can only sign in to this application from an approved network. "
                   + $"Ask your administrator to approve this address: {ip}.",
            code = "ip_not_allowed",
            address = ip.ToString(),
        }));
    }

    private static bool Match(IPAddress ip, string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return false;
        entry = entry.Trim();

        if (entry.Contains('/'))
        {
            var parts = entry.Split('/');
            if (parts.Length != 2) return false;
            if (!IPAddress.TryParse(parts[0], out var net)) return false;
            if (!int.TryParse(parts[1], out var prefix)) return false;
            return InRange(ip, net, prefix);
        }
        return IPAddress.TryParse(entry, out var single) && single.Equals(ip);
    }

    private static bool InRange(IPAddress address, IPAddress network, int prefix)
    {
        if (address.AddressFamily != network.AddressFamily) return false;

        var addrBytes = address.GetAddressBytes();
        var netBytes = network.GetAddressBytes();
        if (prefix < 0 || prefix > addrBytes.Length * 8) return false;

        var bits = prefix;
        for (int i = 0; i < addrBytes.Length; i++)
        {
            if (bits >= 8)
            {
                if (addrBytes[i] != netBytes[i]) return false;
                bits -= 8;
            }
            else if (bits > 0)
            {
                var mask = (byte)(0xFF << (8 - bits));
                return (addrBytes[i] & mask) == (netBytes[i] & mask);
            }
            else return true;
        }
        return true;
    }
}
