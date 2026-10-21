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
    /// True when the caller legitimately READS the whole platform rather than one agency: the
    /// unscoped SuperAdmin above, plus the platform-level agents whose work spans tenants by
    /// design — a central Submission Agent and a central Commission Agent.
    ///
    /// These were being treated as "an agency user whose agency is missing", which is the
    /// fail-closed case, so they got an empty screen or a 403 on pages their whole job depends on.
    ///
    /// IMPORTANT: unlike a SuperAdmin, they do NOT bypass the EF global query filter (see
    /// <c>AppDbContext.BypassTenantFilter</c>). A handler that trusts this must therefore also call
    /// <c>IgnoreQueryFilters()</c> and re-add <c>!IsDeleted</c> by hand, or the filter will narrow
    /// the query to an agency that does not exist and return nothing.
    /// </summary>
    public static bool ReadsEveryAgency(ICurrentUser user) =>
        user is not null
        && (WorksAcrossAgencies(user)
            || DomainRoles.IsCentralSubmissionAgent(user.AgencyId, user.Roles)
            || DomainRoles.IsCentralCommissionAgent(user.AgencyId, user.Roles));

    /// <summary>
    /// True for a cross-agency reader who still has the query filter applied to them — i.e. one who
    /// is not a SuperAdmin. Exactly the callers that need <c>IgnoreQueryFilters()</c>.
    /// </summary>
    public static bool NeedsFilterBypass(ICurrentUser user) =>
        ReadsEveryAgency(user) && !WorksAcrossAgencies(user);

    /// <summary>
    /// May this caller reach PEOPLE in another agency — list them, and assign work to them?
    ///
    /// Deliberately NOT the same question as <see cref="ReadsEveryAgency"/>. That one asks whether
    /// a caller's queries span tenants, and a SuperAdmin who has picked a working context has
    /// chosen to be scoped. Authority is not scope: choosing to look at one agency does not remove
    /// the platform operator's power to assign a sale to whoever holds the carrier appointment.
    ///
    /// This exists because the two drifted. The approval screen listed another agency's licence
    /// agents under one rule (any SuperAdmin) and refused to save them under another (an UNSCOPED
    /// SuperAdmin), so a scoped operator was offered a dropdown of names that could never be
    /// assigned — the same contradiction, in a new place, as the bug that started all this.
    /// Both sides now call this. Keep it that way.
    /// </summary>
    public static bool ReachesOtherAgencies(ICurrentUser user) =>
        user is not null
        && (user.Roles.Contains(DomainRoles.SuperAdmin)
            || DomainRoles.IsCentralSubmissionAgent(user.AgencyId, user.Roles)
            || DomainRoles.IsCentralCommissionAgent(user.AgencyId, user.Roles));

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
