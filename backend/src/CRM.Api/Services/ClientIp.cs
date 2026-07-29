using System.Net;

namespace CRM.Api.Services;

/// <summary>
/// Resolves the real client IP behind the Cloudflare → nginx → Kestrel chain. The raw socket
/// address (<c>ctx.Connection.RemoteIpAddress</c>) is just the proxy — 127.0.0.1 in production —
/// so IP allowlisting and per-IP rate limiting MUST use this, or the allowlist fails open and every
/// request shares one rate-limit partition. Mirrors CurrentUserService.IpAddress resolution order.
/// </summary>
public static class ClientIp
{
    public static string Resolve(HttpContext ctx)
    {
        return FromHeader(ctx, "CF-Connecting-IP")
            ?? FromHeader(ctx, "X-Forwarded-For")
            ?? FromHeader(ctx, "X-Real-IP")
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        static string? FromHeader(HttpContext c, string name)
        {
            var raw = c.Request.Headers[name].ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var first = raw.Split(',')[0].Trim();
            return string.IsNullOrWhiteSpace(first) ? null : first;
        }
    }

    /// <summary>The resolved client IP as an <see cref="IPAddress"/>, falling back to the socket peer.</summary>
    public static IPAddress? ResolveAddress(HttpContext ctx)
        => IPAddress.TryParse(Resolve(ctx), out var ip) ? ip : ctx.Connection.RemoteIpAddress;
}
