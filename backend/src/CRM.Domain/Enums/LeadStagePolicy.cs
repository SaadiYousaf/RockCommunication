using System.Collections.Generic;
using System.Linq;

namespace CRM.Domain.Enums;

/// <summary>
/// Single source of truth for valid lead workflow-stage transitions. Both the single
/// (TransitionLead) and bulk (BulkUpdateLeads) transition paths derive their rules from here;
/// the frontend mirrors this map in shared/constants/leadStage.ts.
/// </summary>
public static class LeadStagePolicy
{
    public static readonly IReadOnlyDictionary<WorkflowStage, WorkflowStage[]> Allowed =
        new Dictionary<WorkflowStage, WorkflowStage[]>
        {
            [WorkflowStage.New]       = new[] { WorkflowStage.Fronted, WorkflowStage.Lost },
            [WorkflowStage.Fronted]   = new[] { WorkflowStage.Verified, WorkflowStage.Lost, WorkflowStage.Followup },
            [WorkflowStage.Verified]  = new[] { WorkflowStage.JrClosed, WorkflowStage.Closed, WorkflowStage.Lost, WorkflowStage.Followup },
            [WorkflowStage.JrClosed]  = new[] { WorkflowStage.Closed, WorkflowStage.Lost, WorkflowStage.Followup },
            [WorkflowStage.Closed]    = new[] { WorkflowStage.Validated, WorkflowStage.Lost },
            [WorkflowStage.Validated] = new[] { WorkflowStage.Funded, WorkflowStage.Lost },
            [WorkflowStage.Funded]    = new[] { WorkflowStage.Followup },
            [WorkflowStage.Followup]  = new[] { WorkflowStage.Fronted, WorkflowStage.Verified, WorkflowStage.Closed, WorkflowStage.Winback, WorkflowStage.Lost },
            [WorkflowStage.Winback]   = new[] { WorkflowStage.Fronted, WorkflowStage.Lost },
            [WorkflowStage.Lost]      = new[] { WorkflowStage.Winback },
        };

    public static bool CanTransition(WorkflowStage from, WorkflowStage to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>
    /// The role whose WORK QUEUE owns a lead once it reaches this stage (i.e. who should be
    /// notified that new work landed), or null when the stage isn't a queued hand-off. A lead
    /// arriving at Fronted needs verifying, Verified needs closing, Closed needs submitting.
    /// </summary>
    public static string? QueueOwnerRole(WorkflowStage stage) => stage switch
    {
        WorkflowStage.Fronted  => Roles.Verifier,
        WorkflowStage.Verified => Roles.Closer,
        WorkflowStage.JrClosed => Roles.Closer,
        WorkflowStage.Closed   => Roles.Validator,
        _ => null,
    };
}
