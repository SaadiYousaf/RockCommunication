/**
 * Centralized user-facing copy for the Workflows feature.
 * Keep inline message strings out of the page — reference these instead.
 */
export const WORKFLOWS_MSG = {
  // Search placeholders (no user-facing copy inline).
  searchPlaceholder: "Search by name, event, description…",
  /** What failed to load, for the shared ErrorState ("Couldn't load workflow rules"). */
  resourceName: "workflow rules",
  // Rule mutations
  ruleUpdated: "Rule updated",
  ruleCreated: "Rule created",
  saveRuleFailed: "Couldn't save rule",
  ruleDeleted: "Rule deleted",
  deleteRuleFailed: "Couldn't delete",

  // Rules empty states
  noRulesMatchTitle: "No rules match",
  noRulesMatchDesc: "Try a different search.",
  noRulesTitle: "No workflow rules yet",
  noRulesDesc: "Create automations: e.g. on every lead.created with score >= 50, assign-agent.",

  // Executions empty state
  noExecutionsTitle: "No executions yet",
  noExecutionsDesc: "Executions appear after a rule matches a triggering event.",
} as const;
