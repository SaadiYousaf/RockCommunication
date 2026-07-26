using System.Linq;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Sales.Queries;

/// <summary>
/// Shared sale-visibility policy so the sales LIST and the sale DETAIL never drift. They had:
/// CEO / ProjectManager / QAManager / CallCenterAdmin all hold SalesRead but were scoped to
/// "own closer sales" and saw an empty list; and a License Agent could see their assigned sale
/// in the list yet get a 404 opening it. Callers remain tenant/call-center scoped by the global
/// query filter — this only decides WHICH sales within the caller's tenant they may see.
/// </summary>
internal static class SalesVisibility
{
    /// <summary>Managers / overseers / validators see every sale in their agency.</summary>
    public static bool CanSeeWholeAgency(IReadOnlyList<string> roles) =>
        roles.Contains(DomainRoles.SuperAdmin) || roles.Contains(DomainRoles.Admin)
        || roles.Contains(DomainRoles.CEO) || roles.Contains(DomainRoles.ProgramManager)
        || roles.Contains(DomainRoles.ProjectManager) || roles.Contains(DomainRoles.QAManager)
        || roles.Contains(DomainRoles.TeamLead) || roles.Contains(DomainRoles.Validator)
        || roles.Contains(DomainRoles.CallCenterAdmin);

    /// <summary>A License Agent's "own" sales are the ones assigned to them, not ones they closed.</summary>
    public static bool IsLicenseAgent(IReadOnlyList<string> roles) => roles.Contains(DomainRoles.LicenseAgent);
}
