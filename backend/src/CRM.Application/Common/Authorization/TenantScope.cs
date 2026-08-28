using CRM.Application.Common.Interfaces;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Common.Authorization;

/// <summary>
/// Answers one question: does this caller work across agencies, or inside exactly one?
///
/// WHY THIS EXISTS: handlers were written as <c>Where(x =&gt; x.AgencyId == _user.AgencyId)</c>, which
/// reads as correct but silently breaks for a SuperAdmin. A SuperAdmin has NO agency of their own —
/// their token carries <see cref="Guid.Empty"/> — so that comparison matches nothing and the record
/// comes back as "not found" even though it plainly exists. Opening any lead as a platform admin
/// returned a 404.
///
/// This mirrors <c>AppDbContext.BypassTenantFilter</c> exactly, so the hand-written predicate and the
/// global query filter can never disagree about who sees what. Keep the two in step.
/// </summary>
public static class TenantScope
{
    /// <summary>
    /// True only for a SuperAdmin who has NOT chosen a working context (POST /api/auth/context).
    /// Once they pick an agency their token carries it, and they are scoped like anyone else — which
    /// is the whole point of the context switcher.
    /// </summary>
    public static bool WorksAcrossAgencies(ICurrentUser user) =>
        user is not null
        && user.Roles.Contains(DomainRoles.SuperAdmin)
        && (user.AgencyId is null || user.AgencyId == Guid.Empty);

    /// <summary>
    /// The agency this caller is confined to, or null when they legitimately span agencies.
    ///
    /// Throws for a NON-SuperAdmin with no agency: that is a malformed token, and the safe response
    /// is to refuse rather than fall through to an unfiltered query.
    /// </summary>
    public static Guid? ConfinedTo(ICurrentUser user)
    {
        if (WorksAcrossAgencies(user)) return null;
        if (user.AgencyId is { } id && id != Guid.Empty) return id;
        throw new Common.Exceptions.ForbiddenAccessException();
    }
}
