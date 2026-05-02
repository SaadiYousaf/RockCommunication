using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Sockets;

namespace CRM.Api.Middleware;

public class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;

    public IpAllowlistMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, AppDbContext db)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) { await _next(ctx); return; }
        if (IPAddress.IsLoopback(ip)) { await _next(ctx); return; }

        var entries = await db.IpAllowlist.AsNoTracking().Select(e => e.CidrOrIp).ToListAsync();
        if (entries.Count == 0) { await _next(ctx); return; }

        if (entries.Any(e => Match(ip, e)))
        {
            await _next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsync("IP not allowed.");
    }

    private static bool Match(IPAddress ip, string entry)
    {
        if (entry.Contains('/'))
        {
            var parts = entry.Split('/');
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
