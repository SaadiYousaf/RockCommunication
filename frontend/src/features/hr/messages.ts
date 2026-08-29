/**
 * Centralized user-facing copy for the HR feature (Employees, Attendance, Interviews, Payroll,
 * Social Media, image uploads). One home for toast, confirm-dialog, empty-state and error-banner
 * strings so the same wording isn't duplicated across pages.
 *
 * Short UI labels (buttons, headers, placeholders) stay in the components — this holds messages.
 */
export const HR_MSG = {
  payrollResourceName: "payroll",
  payrollAgencyFilter: "Filter by agency",
  payrollAllAgencies: "All agencies",
  payrollAllCallCentres: "All call centres",
  // Search placeholders (no user-facing copy inline).
  interviewSearchPlaceholder: "Search candidate, phone, position…",
  // Search placeholders (kept here so no user-facing copy lives inline).
  socialSearchPlaceholder: "Search platform, notes…",
  employeeSearchPlaceholder: "Search name, agent ID, phone, email…",
  // ── Shared toast fragments ────────────────────────────────────────────────
  saveFailed: "Couldn't save",
  removeFailed: "Couldn't remove",
  retry: "Try again.",
  checkRequiredFields: "Check the required fields and try again.",
  exportReady: "Export ready",
  rowsDownloaded: (n: number) => `${n} rows downloaded.`,
  noMatchesTitle: "No matches",
  noEmployeeSearchMatchesDesc: "No employee matches your search.",
  removeLabel: "Remove",
  deleteLabel: "Delete",
  stillSelectedRetry: "They're still selected — try again.",

  // ── Employees ─────────────────────────────────────────────────────────────
  imported: (n: number) => `Imported ${n} employee${n === 1 ? "" : "s"}`,
  alreadyUpToDate: "Already up to date",
  importedDesc: "Created records from your user accounts — fill in the rest of each profile.",
  allUsersHaveRecords: "Every user already has an employee record.",
  importFailed: "Couldn't import",
  employeeUpdated: "Employee updated",
  employeeAdded: "Employee added",
  removeEmployeeTitle: "Remove employee?",
  removeEmployeeDesc: (name: string) => `Remove ${name}'s record? This can't be undone.`,
  employeeRemoved: "Employee removed",
  removeEmployeesTitle: (n: number) => `Remove ${n} ${n === 1 ? "employee" : "employees"}?`,
  removeEmployeesDesc: "This removes the selected employee records. This can't be undone.",
  removedEmployees: (n: number) => `Removed ${n} ${n === 1 ? "employee" : "employees"}`,
  employeesEmptyTitle: "No employees yet",
  noEmployeeMatchesDesc: "No employee matches your filters.",
  employeesEmptyDesc: "Import your existing user accounts to get started, or add records manually.",

  // ── Employee image uploads ────────────────────────────────────────────────
  uploadFailedSize: "Upload failed — use an image under 10 MB.",
  uploadFailedRetry: "Upload failed. Try again.",

  // ── Attendance ────────────────────────────────────────────────────────────
  attendanceSearchPlaceholder: "Search name, agent ID, call centre…",
  noEmployeesTitle: "No employees",
  addEmployeesFirst: "Add employees in HR → Employees first.",
  applyFailed: "Couldn't apply",
  applyAllFailed: "Couldn't apply to all",
  fillFailed: "Couldn't fill",
  setToConfirmTitle: (n: number, label: string) => `Set ${n} to "${label}"?`,
  setToConfirmDesc: (n: number, label: string, date: string) =>
    `This sets the ${n} selected ${n === 1 ? "employee" : "employees"} to "${label}" for ${date}.`,
  setToConfirmLabel: (label: string) => `Set to ${label}`,
  setCountToLabel: (n: number, label: string) => `Set ${n} to ${label}`,
  setEveryoneTitle: (label: string) => `Set everyone to "${label}"?`,
  setEveryoneDesc: (n: number, label: string, date: string) =>
    `This sets all ${n} ${n === 1 ? "employee" : "employees"} to "${label}" for ${date}, overwriting any status already marked. You can still fine-tune individuals afterwards.`,
  setAllConfirmLabel: (label: string) => `Set all to ${label}`,
  everyoneUpdatedDesc: "Every employee on this date was updated.",
  markedPresentFromClockIns: (n: number) => `Marked ${n} present from clock-ins`,
  noNewClockIns: "No new clock-ins",
  clockInsDesc: "Anyone who clocked in that day was marked Present.",

  // ── Interviews ────────────────────────────────────────────────────────────
  interviewUpdated: "Interview updated",
  interviewAdded: "Interview added",
  deleteInterviewTitle: "Delete interview?",
  deleteInterviewDesc: (name: string) => `Remove ${name}'s record?`,
  interviewRemoved: "Interview removed",
  movedToStatus: (n: number, label: string) => `Moved ${n} to ${label}`,
  updateStatusFailed: "Couldn't update status",
  deleteInterviewsTitle: (n: number) => `Delete ${n} ${n === 1 ? "interview" : "interviews"}?`,
  deleteInterviewsDesc: "This removes the selected candidate records. This can't be undone.",
  deletedInterviews: (n: number) => `Deleted ${n} ${n === 1 ? "interview" : "interviews"}`,
  couldntBeRemoved: (n: number) => `${n} couldn't be removed`,
  interviewsEmptyTitle: "No interviews yet",
  noCandidateMatchesDesc: "No candidate matches your filters.",
  interviewsEmptyDesc: "Add a candidate to start tracking the hiring pipeline.",

  // ── Payroll ───────────────────────────────────────────────────────────────
  payrollSearchPlaceholder: "Search name, agent ID, call centre…",
  payrollSaved: "Payroll saved",
  nameMonth: (name: string, month: string) => `${name} — ${month}`,
  generateSlipFailed: "Couldn't generate slip",
  generateSlipDesc: "Try again in a moment.",
  slipDownloaded: "Slip downloaded",
  downloadSlipFailed: "Couldn't download the slip",
  downloadSlipDesc: "Check your connection and try again.",
  nothingToApply: "Nothing to apply",
  nothingToApplyDesc: "Set at least one field or an option.",
  nothingToUpdate: "Nothing to update",
  nothingToUpdateDesc: "Every selected row is finalized (locked).",
  markedPresent: "Marked present",
  paySaved: "pay saved",
  updatedEmployees: (n: number) => `Updated ${n} ${n === 1 ? "employee" : "employees"}`,
  finalizedRowsSkipped: (n: number) => `${n} finalized row${n === 1 ? "" : "s"} skipped.`,
  couldntBeSaved: (n: number) => `${n} couldn't be saved`,
  payrollEmptyDesc: "Add employees in HR → Employees first (or import from users).",
  deductionRulesSaved: "Deduction rules saved",
  saveRulesFailed: "Couldn't save rules",
  saveRulesPermissionDesc: "You may not have permission for this call centre.",

  // ── Social Media ──────────────────────────────────────────────────────────
  reportUpdated: "Report updated",
  reportAdded: "Report added",
  deleteReportTitle: "Delete report?",
  deleteReportDesc: (date: string) => `Remove the ${date} report?`,
  reportRemoved: "Report removed",
  deleteReportsTitle: (n: number) => `Delete ${n} ${n === 1 ? "report" : "reports"}?`,
  deleteReportsDesc: "This removes the selected reports. This can't be undone.",
  deletedReports: (n: number) => `Deleted ${n} ${n === 1 ? "report" : "reports"}`,
  reportsEmptyTitle: "No reports yet",
  noReportMatchesDesc: "No report matches your search.",
  reportsEmptyDesc: "Log a daily report of posts and queries answered.",
} as const;
