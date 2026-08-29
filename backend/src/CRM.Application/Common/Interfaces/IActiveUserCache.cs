namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Drops the per-request account-status cache for a user so the very next request re-reads the
/// database instead of trusting a stale "this account is fine" entry.
///
/// The cache itself lives with the API middleware that owns it; Infrastructure cannot reference the
/// API project, so the capability is expressed here. Without this, deactivating someone leaves them
/// working for up to the cache TTL — which is precisely the window a tenant shutdown must not have.
/// </summary>
public interface IActiveUserCache
{
    void Invalidate(Guid userId);

    /// <summary>Bulk form — a tenant cascade invalidates every affected account at once.</summary>
    void Invalidate(IEnumerable<Guid> userIds);
}
