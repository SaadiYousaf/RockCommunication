import { MESSAGES } from "../../shared/constants/messages";

/**
 * Centralized, user-facing copy for the Leads feature (list, detail, my queue, search, troubleshoot).
 * One home for every toast / confirm / empty-state / permission sentence so the same wording isn't
 * duplicated. Never surface internal identifiers (permission codes, raw roles, GUIDs) — where a
 * message concerns access, it composes the shared MESSAGES helpers so the plain-language phrasing
 * stays consistent app-wide.
 */
export const LEADS_MSG = {
  // Search placeholders (no user-facing copy inline).
  myQueueSearchPlaceholder: "Search my queue by name, phone, or email…",
  leadsSearchPlaceholder: "Search by name, phone, or email…",
  // ---- Shared across leads pages ----
  exportReadyTitle: "Export ready",
  exportRows: (rows: number) => `${rows} rows downloaded.`,
  retry: "Try again.",
  dialFailedTitle: "Couldn't dial",
  callingTitle: "Calling…",

  // ---- LeadDetailPage ----
  leadNotFoundTitle: "Lead not found",
  leadNotFoundDesc:
    "This lead doesn't exist or you don't have access to it. Check the link, or head back to Leads.",
  callBlockedTitle: "Call blocked",
  complianceCheckFailed: "Compliance check failed.",
  cantVerifyComplianceTitle: "Can't verify compliance",
  cantVerifyComplianceDesc: "The DNC/TCPA check is unavailable — the call was not placed.",
  placeCallFailedTitle: "Couldn't place call",
  moveConfirmTitle: (stage: string) => `Move to ${stage}?`,
  moveConfirmDesc: "This takes the lead off the active pipeline.",
  moveConfirmLabel: (stage: string) => `Move to ${stage}`,
  stageUpdatedTitle: "Stage updated",
  movedTo: (stage: string) => `Moved to ${stage}.`,
  moveStageFailedTitle: "Couldn't move stage",
  transitionNotAllowed: "That transition isn't allowed.",
  jornayaVerifiedTitle: "Jornaya verified",
  jornayaVerifiedDesc: "The lead's consent token is now on file.",
  verifyJornayaFailedTitle: "Couldn't verify Jornaya",
  dispositionSetTitle: "Disposition set",
  setDispositionFailedTitle: "Couldn't set disposition",
  notesNotSavedTitle: "Notes not saved",
  notesNotSavedDesc: "Your changes weren't saved — try again.",
  smsSentTitle: "SMS sent",
  smsNotSentTitle: "SMS not sent",
  callbackScheduledTitle: "Callback scheduled",
  scheduleCallbackFailedTitle: "Couldn't schedule callback",
  voicemailDroppedTitle: "Voicemail dropped",
  dropVoicemailFailedTitle: "Couldn't drop voicemail",
  /** Placeholder shown in the notes box when the user may view but not edit notes. */
  notesReadOnly: "You have read-only access to notes",
  /** Tooltip on the read-only notes box — plain language, no permission code exposed. */
  notesNoPermission: MESSAGES.noPermission("edit notes on this lead"),
  noCallsTitle: "No calls yet",
  noCallsDesc: "Calls appear here once you dial this lead.",
  noActivityTitle: "No activity yet",
  noActivityDesc: "Actions on this lead will show up here.",
  leadIdCopiedTitle: "Lead ID copied",
  leadIdCopiedDesc: "Paste it into the sale form.",

  // ---- LeadSearchPage ----
  callingDesc: "Watch the dock for status.",
  searchEmptyStartTitle: "Start typing to search",
  searchEmptyStartDesc:
    "Search across all leads by phone fragment, email, or partial name. Results appear as you type.",
  searchNoMatchTitle: "No leads match",
  searchNoMatchDesc: "Try a shorter fragment or remove a filter.",
  noDuplicatesTitle: "No duplicates",
  noDuplicatesDesc: "Your database is clean — no two leads share the same phone number.",

  // ---- LeadTroubleshootPage ----
  pickLeadTitle: "Pick a lead to diagnose",
  pickLeadDesc: "Paste a lead ID below or open this page from a lead's detail screen.",
  diagnosticsFailedTitle: "Couldn't load diagnostics",
  diagnosticsFailedDesc: "The lead may not exist or you may not have permission to see it.",
  noEnrollmentsTitle: "No enrollments",
  noEnrollmentsDesc: "This lead isn't currently in any automated cadence.",
  troubleshootNoCallsDesc: "Once an agent dials this lead it'll show up here.",
  noMatchingRulesTitle: "No matching rules",
  noMatchingRulesDesc: "No workflow rules are configured for the events this lead would emit.",

  // ---- LeadsPage ----
  leadTransitionedTitle: "Lead transitioned",
  leadTransitionedDesc: (name: string, stage: string) => `${name} → ${stage}`,
  transitionFailedTitle: "Couldn't transition",
  assignedTitle: "Assigned",
  updatedSkipped: (updated: number, skipped: number, extra = "") =>
    `${updated} updated · ${skipped} skipped${extra}`,
  bulkErrors: (count: number) => `, ${count} errors`,
  enrolledTitle: "Enrolled in cadence",
  enrolledDesc: (updated: number, skipped: number) => `${updated} enrolled · ${skipped} already in`,
  bulkFailedTitle: "Bulk action failed",
  notAuthenticated: "Not authenticated",
  exportFailed: "Export failed",
  csvDownloaded: "Your CSV has downloaded.",
  leadCreatedTitle: "Lead created",
  createLeadFailedTitle: "Couldn't create lead",
  leadsEmptyFilterTitle: "No leads match your filter",
  leadsEmptyTitle: "No leads yet",
  leadsEmptyFilterDesc: "Try clearing your search or picking a different stage.",
  leadsEmptyDesc: "Get started by creating your first lead.",

  // ---- MyQueuePage ----
  dispositionSavedTitle: "Disposition saved",
  dispositionSavedDesc: (name: string, disposition: string) => `${name} → ${disposition}`,
  queueUpdateFailedTitle: "Couldn't update",
  queueEmptyTitle: "No leads in your queue",
  queueNoMatchTitle: "Nothing matches your filter",
  queueEmptyDesc: "Leads assigned to you will appear here. Speak to your team lead about pulling some.",
  queueNoMatchDesc: "Try clearing the search or switching the tab.",
} as const;
