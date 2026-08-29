namespace CRM.Application.Common.Interfaces;

/// <summary>What a disable/enable actually did, so the caller can report it honestly.</summary>
public record TenantCascadeResult(int CallCentersChanged, int UsersChanged);

/// <summary>
/// What disabling an agency WOULD do, fetched before the admin confirms. The confirmation dialog
/// shows real counts rather than a vague warning, so the operator knows the blast radius up front.
/// </summary>
public record TenantDisableImpact(int CallCenters, int Users, int ActiveSessions);

/// <summary>
/// Owns the agency/call-centre enable-disable transition and everything that must happen with it.
///
/// WHY THIS EXISTS: disabling an agency used to flip one boolean and revoke refresh tokens. Its call
/// centres stayed Active and its users stayed Active — in production that left 6 disabled agencies
/// owning 13 active call centres and 41 active accounts. The cascade has to be one operation, in one
/// transaction, or a half-applied failure leaves a tenant that is disabled in name only.
///
/// It lives in Infrastructure because the users it must switch off are ASP.NET Identity rows, and
/// IApplicationDbContext deliberately exposes no user set. The Application layer talks to this
/// interface instead of reaching for a DbContext.
/// </summary>
public interface ITenantLifecycleService
{
    /// <summary>
    /// Disable or enable an agency, cascading to its call centres and users in one transaction.
    ///
    /// Idempotent: re-running a disable re-sweeps the children, so a partially-applied earlier run
    /// converges instead of leaving orphans. Enabling restores only what the cascade itself turned
    /// off — anything an admin disabled individually stays off.
    /// </summary>
    Task<TenantCascadeResult> SetAgencyActiveAsync(Guid agencyId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Disable or enable a single call centre, cascading to the users pinned to it. Enabling a
    /// centre whose agency is disabled is refused — it would claim an access the agency gate denies.
    /// </summary>
    Task<TenantCascadeResult> SetCallCenterActiveAsync(Guid callCenterId, bool isActive, CancellationToken ct = default);

    /// <summary>Counts shown in the confirmation dialog before an agency is disabled.</summary>
    Task<TenantDisableImpact> GetAgencyDisableImpactAsync(Guid agencyId, CancellationToken ct = default);
}
