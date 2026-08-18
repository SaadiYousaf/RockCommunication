/**
 * Centralized, user-facing copy for the Intake feature (fronter → verifier → closer → submission).
 * One home for every toast / empty-state / error sentence so the same wording isn't duplicated
 * across the intake pages. Never surface internal identifiers here — speak in plain language.
 */
export const INTAKE_MSG = {
  // ---- Shared across intake pages ----
  exportReadyTitle: "Export ready",
  exportRows: (rows: number) => `${rows} rows downloaded.`,
  noMatches: "No matches in this queue.",
  checkRequiredFields: "Check the required fields and try again.",
  retry: "Try again.",
  /** Outcome fallback when a status has no bespoke sentence — e.g. "Marked NotInterested". */
  marked: (status: string) => `Marked ${status}`,

  // ---- CloseQueuePage ----
  leadAddedTitle: "Lead added",
  leadAddedDesc: (name: string) => `${name} → your closer queue`,
  addLeadFailedTitle: "Couldn't add lead",
  closeEmptyTitle: "No verified leads",
  closeEmptyDesc: "Verified leads will appear here. Use “Add lead” to start one yourself.",

  // ---- ClosingApplicationPage ----
  selectCloserStatus: "Select a closer status",
  appSubmittedTitle: "Application submitted",
  saleCreated: "Sale created (Lyons cleared the account).",
  submitFailedTitle: "Couldn't submit",
  checkRequiredAndBank: "Check the required fields and bank details.",
  leadNotFoundTitle: "Lead not found",
  leadNotFoundDesc: "It may have been removed, or you may not have access to it.",

  // ---- IntakeFormPage ----
  leadSubmittedTitle: "Lead submitted",
  leadSubmittedDesc: (name: string) => `${name} → verifier queue`,

  // ---- ValidateQueuePage (submission queue) ----
  validateEmptyTitle: "No sales to submit",
  validateEmptyDesc: "Sales appear here as soon as a closer completes one.",
  copiedTitle: "Copied",
  copiedDesc: "Lead details copied to clipboard.",
  copyFailedTitle: "Couldn't copy",
  copyFailedDesc: "Your browser blocked clipboard access.",
  statusUpdatedTitle: "Status updated",
  statusUpdatedDesc: (name: string, label: string) => `${name} → ${label}`,
  updateFailedTitle: "Couldn't update",

  // ---- VerifyQueuePage ----
  verifyEmptyTitle: "Queue is empty",
  verifyEmptyDesc: "New fronted leads will appear here.",
  pickStatus: "Pick a status",
  statusSavedTitle: "Status saved",
  leadSentToCloser: "Lead sent to closer queue",
  saveFailedTitle: "Couldn't save",
  leadUpdatedTitle: "Lead updated",
  checkFieldsAndTryAgain: "Check the fields and try again.",
} as const;
