/**
 * Centralized, user-facing copy for the Sales feature. One home for the toast / confirm / empty-state
 * / error sentences shown to users, so the same wording isn't duplicated or drifting across pages.
 *
 * RULE: never surface internal identifiers (permission codes, role names, GUIDs). Speak plainly about
 * what the user can/can't do; lean on the shared MESSAGES helpers for read-only / permission copy.
 */
export const SALES_MSG = {
  // Shared across sales pages
  exportReadyTitle: "Export ready",
  rowsDownloaded: (count: number) => `${count} rows downloaded.`,

  // CommissionsPage
  notAuthenticated: "Not authenticated",
  exportPayrollNoPermission: "You don't have permission to export payroll (need Payroll access).",
  exportFailedStatus: (status: number) => `Export failed (${status}).`,
  exportFailedTitle: "Export failed",
  payrollCsvDownloading: "The payroll CSV has started downloading.",
  runPayrollConfirmTitle: "Run payroll for this period?",
  runPayrollConfirmBody: (from: string, to: string) =>
    `This finalizes commissions earned between ${from} and ${to} and freezes them as paid. Make sure the date range is right before continuing.`,
  runPayrollConfirmLabel: "Run payroll",
  payrollRunCreatedTitle: "Payroll run created",
  payrollRunPeriod: (from: string, to: string) => `Period ${from} → ${to}`,
  createPayrollRunFailed: "Couldn't create payroll run",
  noCommissionsTitle: "No commissions in this range",
  noCommissionsBody: "Earn commissions by closing and funding sales, then check back here.",
  commissionsSearchPlaceholder: "Search by rule, note or status…",
  noCommissionsMatchTitle: "No matches",
  noCommissionsMatchBody: "No commissions match your search. Try a different term.",
  noPayrollRunsTitle: "No payroll runs yet",
  noPayrollRunsBody: "Run payroll to summarize and freeze commissions for a period.",

  // LicenseAgentQueuePage
  noSalesAssignedTitle: "No sales assigned yet",
  noSalesAssignedBody:
    "When a submission agent assigns a sale to you, it'll appear here — and you'll get a notification.",
  assignedSalesSearchPlaceholder: "Search by customer, phone, carrier or policy…",
  noAssignedSalesMatchTitle: "No matches",
  noAssignedSalesMatchBody: "No assigned sales match your search. Try a different term.",

  // SaleDetailPage
  saleNotFoundTitle: "Sale not found",
  saleNotFoundBody: "It may have been removed, or you may not have access to it.",
  noCommissionLinesTitle: "No commission lines yet",
  noCommissionLinesBody: "These are created when the sale is approved and funded.",
  licenseAgentAssigned: "License agent assigned",
  licenseAgentCleared: "License agent cleared",
  updateLicenseAgentFailed: "Couldn't update license agent",

  // SalesPage
  bankDetailsRequiredTitle: "Bank details required",
  bankDetailsRequiredBody:
    "Enter the routing and account number so Lyons can validate the account.",
  bankFlagged198: "Lyons flagged the account (198) — recording attached.",
  bankCleared103: "Lyons cleared the account (103).",
  saleRecordedTitle: "Sale recorded",
  saleRecordedBody: (premium: number, carrier: string, bankMsg: string) =>
    `$${premium.toFixed(2)}/mo · ${carrier} · ${bankMsg}`,
  verificationRecordingNeededTitle: "Verification recording needed",
  recordSaleFailed: "Couldn't record sale",
  rejectSaleConfirmTitle: "Reject this sale?",
  rejectSaleConfirmBody:
    "The sale will be marked rejected in QA and won't move on to funding. Reject only if it fails validation.",
  rejectSaleConfirmLabel: "Reject sale",
  saleApproved: "Sale approved",
  saleRejected: "Sale rejected",
  validateSaleFailed: "Couldn't validate sale",
  saleFunded: "Sale funded",
  fundSaleFailed: "Couldn't fund sale",
  leadSelectedTitle: "Lead selected",
  leadSelectedBody: (name: string) => `${name} — now fill in the sale above.`,
  noClosedLeadsTitle: "No closed leads",
  noClosedLeadsBody: "Closed leads will appear here once they're transitioned to Closed.",
  noSalesMatchTitle: "No sales match",
  noSalesMatchBody: "Try a different filter or date range.",
  salesSearchPlaceholder: "Search by customer, phone, carrier or policy…",
  noSalesSearchMatchTitle: "No matches",
  noSalesSearchMatchBody: "No sales on this page match your search. Try a different term.",
} as const;
