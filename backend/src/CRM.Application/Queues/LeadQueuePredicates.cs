using CRM.Domain.Entities;
using CRM.Domain.Enums;
using DomainRoles = CRM.Domain.Enums.Roles;
using System.Linq.Expressions;

namespace CRM.Application.Queues;

/// <summary>
/// THE invariant behind every lead list, in one place.
///
/// <c>Lead.AssignedUserId</c> means "the one person whose job it is to work this lead right now".
/// NULL means "nobody has taken it — it belongs to the shared pool for its stage".
///
/// A lead is therefore in exactly ONE actionable place: one person's My Leads, or one pool. The two
/// predicates below contradict each other on a single column (assigned vs null), so no lead can ever
/// satisfy both.
///
/// WHY THIS FILE EXISTS: the pool query filtered only on Stage and the personal query filtered only
/// on AssignedUserId, so any verified lead that was also assigned to you appeared in both — in
/// production, 43 of the 53 leads in the Closer Queue already belonged to a named person, and every
/// closer saw all of them as claimable work. Badge and list drifted apart for the same reason. Both
/// now read from here.
/// </summary>
public static class LeadQueuePredicates
{
    /// <summary>
    /// Stages where the assignee personally still has something to do.
    ///
    /// Closed is excluded: once a deal is sold it belongs to Sales and the validator, not to the
    /// closer's working list — counting it kept a sold deal in two badges at once. New is retained
    /// because a directly-assigned lead starts there.
    /// </summary>
    public static readonly WorkflowStage[] MineActive =
    {
        WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified,
        WorkflowStage.JrClosed, WorkflowStage.Followup,
    };

    /// <summary>Leads this user personally owns and still has work to do on.</summary>
    public static Expression<Func<Lead, bool>> Mine(Guid userId) =>
        l => l.AssignedUserId == userId && MineActive.Contains(l.Stage);

    /// <summary>Leads waiting at this stage for someone to claim them.</summary>
    public static Expression<Func<Lead, bool>> Pool(WorkflowStage stage) =>
        l => l.Stage == stage && l.AssignedUserId == null;

    /// <summary>
    /// The pools this caller may work, derived from which role owns each stage rather than from a
    /// hardcoded role list — so a JrCloser, who holds identical permissions to a Closer, is no
    /// longer locked out of closer work by a string comparison.
    /// </summary>
    public static IReadOnlyList<WorkflowStage> PoolsFor(IEnumerable<string> roles, bool seesEverything)
    {
        var held = roles as IReadOnlyCollection<string> ?? roles.ToList();
        var owned = new List<WorkflowStage>();

        foreach (var stage in new[] { WorkflowStage.Fronted, WorkflowStage.Verified })
        {
            var owner = LeadStagePolicy.QueueOwnerRole(stage);
            if (owner is null) continue;
            if (seesEverything || Works(held, owner)) owned.Add(stage);
        }
        return owned;
    }

    /// <summary>
    /// Does this caller do the work a pool is waiting for?
    ///
    /// A JrCloser does closer work — same permissions, same modules — but the stage policy names the
    /// owner as "Closer", so a plain string match left JrClosers with no queue at all and no way to
    /// reach closer work. Equivalences are declared here rather than duplicated at each call site.
    /// </summary>
    private static bool Works(IReadOnlyCollection<string> roles, string ownerRole)
    {
        if (roles.Contains(ownerRole, StringComparer.OrdinalIgnoreCase)) return true;
        return ownerRole.Equals(DomainRoles.Closer, StringComparison.OrdinalIgnoreCase)
            && roles.Contains(DomainRoles.JrCloser, StringComparer.OrdinalIgnoreCase);
    }
}
