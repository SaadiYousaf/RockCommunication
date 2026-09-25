using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Middleware;

/// <summary>
/// The network allowlist, held in memory for a few seconds at a time.
///
/// <see cref="IpAllowlistMiddleware"/> runs on every request and originally re-queried the database
/// each time — on a busy floor that is thousands of pointless round-trips a minute.
///
/// A singleton in the container rather than a static field, deliberately. A static would be shared
/// by every application in the process, which in production is exactly one — but under test it is
/// several, each with its own database, and one host's empty list would be served to another host's
/// requests. That is not only a test problem: it is the kind of shared mutable state that is correct
/// purely by accident of deployment.
/// </summary>
public interface IIpAllowlistCache
{
    Task<string[]> GetAsync(AppDbContext db, CancellationToken ct = default);

    /// <summary>Drop the cached list so a change applies on the very next request.</summary>
    void Invalidate();
}

public class IpAllowlistCache : IIpAllowlistCache
{
    /// <summary>
    /// Short enough that revoking an address bites while the person who revoked it is still looking
    /// at the screen, long enough to take the query off the hot path.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private DateTime _loadedAt = DateTime.MinValue;
    private string[] _entries = Array.Empty<string>();

    public async Task<string[]> GetAsync(AppDbContext db, CancellationToken ct = default)
    {
        Guard.AgainstNull(db);

        lock (_gate)
        {
            if (DateTime.UtcNow - _loadedAt < CacheFor) return _entries;
        }

        var entries = await db.IpAllowlist.AsNoTracking()
            .Select(e => e.CidrOrIp)
            .ToArrayAsync(ct);

        lock (_gate)
        {
            _entries = entries;
            _loadedAt = DateTime.UtcNow;
        }
        return entries;
    }

    public void Invalidate()
    {
        lock (_gate) { _loadedAt = DateTime.MinValue; }
    }
}
