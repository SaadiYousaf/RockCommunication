using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Common.Authorization;

/// <summary>
/// Decides, from a caller's roles, whether they may see ALL records within their tenant scope
/// (managers/leadership) or only their OWN assigned records (front-line agents). This is a
/// distinct axis from tenant isolation: the query filter already limits rows to the caller's
/// agency + call center; this narrows a browse/list further to the agent's own work.
///
/// Deliberately a pure function of roles (no state) so it is trivially testable and can be
/// called from any handler without a new dependency.
/// </summary>
public static class AccessScope
{
    /// <summary>Roles that oversee a call center / agency and legitimately see everyone's records.</summary>
    private static readonly HashSet<string> Elevated = new(StringComparer.OrdinalIgnoreCase)
    {
        DomainRoles.SuperAdmin, DomainRoles.Admin, DomainRoles.CEO, DomainRoles.ProgramManager,
        DomainRoles.ProjectManager, DomainRoles.TechLead, DomainRoles.QAManager, DomainRoles.TeamLead,
    };

    /// <summary>
    /// True when the caller may see every record in their tenant scope; false when they should
    /// be restricted to records assigned to them. Absence of any role fails closed (own-only).
    /// </summary>
    public static bool SeesAllRecords(IReadOnlyList<string>? roles)
        => roles is not null && roles.Any(Elevated.Contains);
}
