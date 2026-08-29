/**
 * Centralized, user-facing copy for the Intake feature (fronter → verifier → closer → submission).
 * One home for every toast / empty-state / error sentence so the same wording isn't duplicated
 * across the intake pages. Never surface internal identifiers here — speak in plain language.
 */
export const INTAKE_MSG = {
  // Form group headings — they name the QUESTION each block answers, not the database tables the
  // fields come from.
  groupCustomer: "Customer",
  groupCustomerHint: "Who you're speaking to.",
  groupContact: "Contact details",
  groupContactHint: "How to reach them, and where they live.",
  groupCompliance: "Consent & source",
  groupComplianceHint: "Proof of when and how this lead consented to be contacted.",

  // Search placeholders (no user-facing copy inline).
  queueSearchPlaceholder: "Search this queue…",
  // ---- Shared across intake pages ----
  exportReadyTitle: "Export ready",
  exportRows: (rows: number) => `${rows} rows downloaded.`,
  noMatches: "No matches in this queue.",
  checkRequiredFields: "Check the required fields and try again.",
  retry: "Try again.",
  /** Outcome fallback when a status has no bespoke sentence — e.g. "Marked NotInterested". */
  marked: (status: string) => `Marked ${status}`,

  // ---- CloseQueuePage ----
  /** What failed to load, for the shared ErrorState ("Couldn't load the closer queue"). */
  closeResourceName: "the closer queue",
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
  /** What failed to load, for the shared ErrorState ("Couldn't load the verifier queue"). */
  verifyResourceName: "the verifier queue",
  verifyEmptyTitle: "Queue is empty",
  verifyEmptyDesc: "New fronted leads will appear here.",
  pickStatus: "Pick a status",
  statusSavedTitle: "Status saved",
  leadSentToCloser: "Lead sent to closer queue",
  saveFailedTitle: "Couldn't save",
  leadUpdatedTitle: "Lead updated",
  checkFieldsAndTryAgain: "Check the fields and try again.",
} as const;
