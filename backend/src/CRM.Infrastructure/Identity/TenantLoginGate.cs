using CRM.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Identity;

/// <summary>
/// Decides whether a user's tenant (their agency, and — for agents — their call
/// center) is currently allowed to sign in. When a SuperAdmin disables an agency
/// or a call center, every user underneath it is locked out of login/refresh,
/// even though their own account is still <c>IsActive</c>.
/// </summary>
public static class TenantLoginGate
{
    /// <summary>Shown to a user whose company/call-center has been disabled.</summary>
    public const string DisabledMessage =
        "Your organization's access has been disabled. Please contact your administrator.";

    /// <summary>
    /// True when the user may sign in. Platform-level users (SuperAdmin and central
    /// Submission Agents) have no real agency (<see cref="System.Guid.Empty"/>) and are
    /// never gated. A missing, soft-deleted, or deactivated agency/call-center fails closed.
    /// </summary>
    public static async Task<bool> IsTenantActiveAsync(
        IApplicationDbContext db, Guid agencyId, Guid? callCenterId, CancellationToken ct = default)
    {
        // Platform-level users (SuperAdmin / central Submission Agents) carry Guid.Empty.
        if (agencyId == Guid.Empty) return true;

        // IgnoreQueryFilters: login runs with no ambient user context, and we deliberately
        // want soft-deleted rows to count as "disabled" too.
        var agency = await db.Agencies.IgnoreQueryFilters()
            .Where(a => a.Id == agencyId)
            .Select(a => new { a.IsActive, a.IsDeleted })
            .FirstOrDefaultAsync(ct);
        if (agency is null || agency.IsDeleted || !agency.IsActive) return false;

        if (callCenterId is { } ccId && ccId != Guid.Empty)
        {
            var cc = await db.CallCenters.IgnoreQueryFilters()
                .Where(c => c.Id == ccId)
                .Select(c => new { c.IsActive, c.IsDeleted })
                .FirstOrDefaultAsync(ct);
            if (cc is null || cc.IsDeleted || !cc.IsActive) return false;
        }

        return true;
    }
}
