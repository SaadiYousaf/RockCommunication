using System.Net;
using System.Net.Sockets;
using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Persistence.Seed;

/// <summary>
/// Networks declared in configuration, ensured on every start.
///
/// The allowlist is otherwise only editable by a signed-in SuperAdmin, which is a problem exactly
/// when it matters: a wrong or stale entry locks everyone out, and the person who would fix it has
/// to already be able to reach the application. SuperAdmin's exemption is the first way back in;
/// this is the second, and it works from a deploy rather than from a browser.
///
/// ADDITIVE AND IDEMPOTENT. An address already on the list is left exactly as it is, so this never
/// disturbs an entry someone added by hand. Nothing is ever removed — taking a line out of the
/// config does not revoke it, and an entry removed in the UI while still listed here comes back on
/// the next restart. To drop one for good, delete it from both.
/// </summary>
public static class IpAllowlistSeeder
{
    /// <summary>One declared network. The note is what an administrator sees in the list.</summary>
    public sealed class Entry
    {
        public string? CidrOrIp { get; set; }
        public string? Note { get; set; }
    }

    public static async Task RunAsync(
        AppDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        var declared = config.GetSection("Security:IpAllowlist").Get<Entry[]>() ?? Array.Empty<Entry>();
        if (declared.Length == 0) return;

        // One read of what is already there. Compared case-insensitively because IPv6 is written in
        // either case and "2402:AD80::/64" and "2402:ad80::/64" are the same network.
        var existing = (await db.IpAllowlist.IgnoreQueryFilters().Select(e => e.CidrOrIp).ToListAsync(ct))
            .Select(v => v.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var entry in declared)
        {
            var value = entry?.CidrOrIp?.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;

            // A malformed entry would never match any address, so it cannot let anyone in — but it
            // would sit in the list looking as though it had. Refuse it loudly instead.
            if (!IsParseable(value))
            {
                logger.LogError(
                    "Security:IpAllowlist entry {Entry} is not an address or CIDR range and was ignored. " +
                    "Nobody is admitted by it.", value);
                continue;
            }

            if (!existing.Add(value)) continue;        // already present — leave it alone

            db.IpAllowlist.Add(new IpAllowlistEntry
            {
                // Platform-owned, like every entry a SuperAdmin adds: they have no agency of their own.
                AgencyId = null,
                CidrOrIp = value,
                Note = string.IsNullOrWhiteSpace(entry!.Note) ? "Added from configuration" : entry.Note!.Trim(),
            });
            added++;
        }

        if (added == 0) return;
        await db.SaveChangesAsync(ct);

        // Worth a warning rather than information: this widens who can reach the platform, and the
        // operator should be able to find out why from the log alone.
        logger.LogWarning("Approved {Count} network(s) from configuration (Security:IpAllowlist).", added);
    }

    /// <summary>
    /// Mirrors the middleware's own parsing, so anything accepted here is something that can
    /// actually match a client. Keep the two in step.
    /// </summary>
    private static bool IsParseable(string entry)
    {
        var slash = entry.IndexOf('/');
        if (slash < 0) return IPAddress.TryParse(entry, out _);

        var parts = entry.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var network)) return false;
        if (!int.TryParse(parts[1], out var prefix)) return false;

        var max = network.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        return prefix >= 0 && prefix <= max;
    }
}
