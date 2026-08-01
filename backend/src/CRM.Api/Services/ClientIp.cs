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
        var socket = ctx.Connection.RemoteIpAddress;

        // SECURITY: only trust proxy-set forwarding headers when the IMMEDIATE peer is a trusted
        // proxy (our own nginx on loopback / a private network). A request that reaches the origin
        // directly — bypassing Cloudflare + nginx — arrives from a public socket IP, so its
        // client-supplied CF-Connecting-IP / X-Forwarded-For is attacker-controlled and MUST be
        // ignored; otherwise an attacker spoofs any IP to defeat per-IP rate limiting and the IP
        // allowlist. Fall back to the real socket address in that case.
        if (socket is not null && IsTrustedProxy(socket))
        {
            var forwarded = FromHeader(ctx, "CF-Connecting-IP")
                ?? FromHeader(ctx, "X-Forwarded-For")
                ?? FromHeader(ctx, "X-Real-IP");
            if (forwarded is not null) return forwarded;
        }

        return socket?.ToString() ?? "unknown";

        static string? FromHeader(HttpContext c, string name)
        {
            var raw = c.Request.Headers[name].ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var first = raw.Split(',')[0].Trim();
            return string.IsNullOrWhiteSpace(first) ? null : first;
        }
    }

    /// <summary>True when the socket peer is our own reverse proxy — loopback or an RFC1918 /
    /// unique-local private address. Only then are the forwarding headers trustworthy.</summary>
    private static bool IsTrustedProxy(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        var mapped = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
        if (mapped.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = mapped.GetAddressBytes();
            return b[0] == 10                                   // 10.0.0.0/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                || (b[0] == 127);                               // 127.0.0.0/8
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal
            || (ip.GetAddressBytes() is { Length: 16 } v6 && (v6[0] & 0xFE) == 0xFC); // fc00::/7 unique-local
    }

    /// <summary>The resolved client IP as an <see cref="IPAddress"/>, falling back to the socket peer.</summary>
    public static IPAddress? ResolveAddress(HttpContext ctx)
        => IPAddress.TryParse(Resolve(ctx), out var ip) ? ip : ctx.Connection.RemoteIpAddress;
}
