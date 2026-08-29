/**
 * Copy for the dashboard's "Your work" section.
 *
 * Every line answers a question the person signing in is actually asking. No metric names, no queue
 * names, no internal vocabulary — the labels match the navigation exactly, so a tile and the page it
 * opens are unmistakably the same thing.
 */
export const WORK_MSG = {
  sectionTitle: "Your work",

  myLeads: "My Leads",
  myLeadsHint: "Assigned to you",

  availableLeads: "Available Leads",
  availableHint: "Waiting to be picked up",

  callbacksDue: "Callbacks",
  callbacksHint: "Scheduled by you",

  submissions: "Submissions",
  submissionsHint: "Waiting on review",

  allClearTitle: "You're all caught up",
  allClearBody: "Nothing is assigned to you right now. Check Available Leads to pick up new work.",
} as const;
