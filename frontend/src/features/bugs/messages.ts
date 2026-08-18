/**
 * Centralized user-facing copy for the Bugs feature (the triage board and the "Report a bug"
 * button). Toast, empty-state and confirm strings live here; the generic "Please try again."
 * fallback comes from the shared MESSAGES helper in the components.
 */
export const BUGS_MSG = {
  exportReady: "Export ready",
  rowsDownloaded: (n: number) => `${n} rows downloaded.`,
  retry: "Try again.",

  movedCount: (n: number, label: string) => `Moved ${n} to ${label}`,
  movedTo: (label: string) => `Moved to ${label}`,
  couldntBeUpdated: (n: number) => `${n} couldn't be updated`,
  stillSelected: "They're still selected.",

  noBugsTitle: "No bugs here",
  noBugsFilterDesc: "Nothing matches this filter.",
  noBugsDesc: "Nothing reported yet. Use the “Report a bug” button anytime you hit a problem.",

  updateStatusFailed: "Couldn't update status",
  assignFailed: "Couldn't assign",
  commentFailed: "Couldn't comment",

  bugReported: "Thanks — bug reported",
  bugReportedDesc: "You can track its status under Bugs.",
  submitFailed: "Couldn't submit the report",
} as const;
