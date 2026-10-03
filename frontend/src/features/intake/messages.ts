/**
 * Centralized, user-facing copy for the Intake feature (fronter → verifier → closer → submission).
 * One home for every toast / empty-state / error sentence so the same wording isn't duplicated
 * across the intake pages. Never surface internal identifiers here — speak in plain language.
 */
export const INTAKE_MSG = {
  // The submission queue's agent filter. One control covers all three roles a submitted sale
  // carries, because "show me everything involving this person" is the actual question.
  agentFilterLabel: "Agent",
  agentFilterAll: "All agents",
  agentFilterHint:
    "Shows every sale this person is involved in — whether they closed it, are submitting it, or are the agent of record. The number is how many sales that is.",

  closingDescription:
    "Complete the application. 'Complete and Sold' validates banking via Lyons and creates the sale.",
  policyReadOnlyDescription: "The application and policy recorded for this lead.",
  policyLockedReason: (stage: string) =>
    `This lead has already been closed and is now ${stage}, so the application is a record and can't be edited. Ask an administrator if something needs correcting.`,
  backToLead: "Back to lead",

  // ---- The Add Lead form, which serves two different jobs ----
  // A Fronter is starting the pipeline: their lead goes on to a Verifier. A Closer adding their own
  // lead has already done that conversation, so it lands in their own work instead. Same form, and
  // the copy has to say which of the two is about to happen.
  addLeadFronterEyebrow: "Fronter intake",
  addLeadFronterTitle: "Get Yourself Protected — Lead Intake",
  addLeadFronterDescription:
    "Capture the prospect's Jornaya details. All fields are required.",
  addLeadFronterSubmit: "Submit to verifier",
  addLeadFronterSubtitle: "Final-expense intake",

  addLeadCloserEyebrow: "Add your own lead",
  addLeadCloserTitle: "Add a lead to your pipeline",
  addLeadCloserDescription:
    "For a prospect you've already spoken to. The lead is added straight to your own leads, ready to close — it doesn't go to a verifier.",
  addLeadCloserSubmit: "Add to my leads",
  addLeadCloserSubtitle: "Goes straight to you",

  addLeadTitle: "New lead",
  typingOnlyBadge: "No copying",
  typingOnlyHint:
    "These details can't be copied out of the CRM — they include bank and identity information. You can still paste a value in.",

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
