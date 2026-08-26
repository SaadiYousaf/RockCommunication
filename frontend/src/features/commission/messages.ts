import type { BadgeTone } from "../../shared/ui";

/**
 * Centralized user-facing copy + status metadata for the Commission Desk.
 * Keep inline strings out of the components — reference these instead.
 */
export const COMMISSION_MSG = {
  // Sales list
  eyebrow: "Finance",
  title: "Commission Desk",
  description:
    "Submitted sales across every agency and call centre. Set each policy's financial status, reconcile charge-backs, and track what carriers advance.",

  statSales: "Sales",
  statSalesHint: "Matching your filters",
  statPremium: "Premium / mo",
  statPremiumHint: "Total monthly premium",
  statFunded: "Funded",
  statFundedHint: "On the commission ledger",

  searchPlaceholder: "Search customer, phone, policy or carrier…",
  allAgencies: "All agencies",
  allCallCenters: "All call centres",
  allCarriers: "All carriers",
  allStatuses: "All statuses",
  from: "From",
  to: "To",
  clearFilters: "Clear filters",

  emptyTitle: "No sales yet",
  emptyDesc: "No submitted sales are waiting on the commission desk.",
  noMatchTitle: "No matches",
  noMatchDesc: "No sales match these filters. Try widening the date range or clearing a filter.",

  // Row actions
  updateStatus: "Update",
  viewAmounts: "Amounts",

  // Status modal
  statusTitle: (name: string) => `Update status — ${name}`,
  currentStatus: "Current status",
  newStatus: "New status",
  noteLabel: "Note",
  notePlaceholder: "What changed, and why?",
  noteHelp: "Recorded on the lead's timeline.",
  noteRequired: "A note explaining the reason is required.",
  retentionWarning:
    "This hands the policy to the Retention team, who will work to recover it.",
  chargebackWarning:
    "This flips the sale's unpaid commission amounts negative and unlocks them for editing.",
  confirmCta: "Confirm update",
  statusUpdated: "Status updated",
  statusUpdatedDesc: (name: string, status: string) => `${name} is now ${status}.`,
  statusFailed: "Couldn't update the status",

  // Chargeback / amounts modal
  amountsTitle: (name: string) => `Commission amounts — ${name}`,
  amountsIntro: "The money lines on this sale.",
  amountsEditable: "This sale is charged back — unpaid amounts can be edited.",
  amountsLocked: "Amounts become editable once the sale is marked charged back.",
  amountPaidLock: "Already paid out — kept as history and can't be edited.",
  amountSaved: "Amount updated",
  amountFailed: "Couldn't update the amount",
  noAmounts: "No commission lines on this sale yet.",
  colRule: "Line",
  colAgent: "Agent",
  colAmount: "Amount",

  // Advancing (read-only on the list)
  advancingHint:
    "Pulled from the carrier's advancing rule. Edit it under Carrier Rules — it can't be changed per sale.",
  noRule: "No rule",

  // Columns
  colCustomer: "Customer",
  colCarrier: "Carrier",
  colCoverage: "Coverage",
  colPremium: "Premium",
  colAgency: "Agency",
  colCallCenter: "Call centre",
  colStatus: "Status",
  colFunded: "Funded",
  colAdvancing: "Advancing",
  colSold: "Sold",
} as const;

/** Copy for the Carrier Advancing Rules screen. */
export const CARRIER_RULES_MSG = {
  eyebrow: "Finance",
  title: "Carrier Rules",
  description:
    "How each carrier advances commission — the rate it pays and how many months it advances up front. These feed the Commission Desk automatically.",

  newRule: "New rule",
  editRule: "Edit rule",
  carrier: "Carrier",
  carrierPlaceholder: "e.g. ABC Life",
  rate: "Commission rate (%)",
  months: "Advanced months",
  notes: "Notes",
  active: "Active",
  inactiveHint: "Inactive rules stop applying to the sales list but keep their history.",

  searchPlaceholder: "Search by carrier or notes…",
  emptyTitle: "No carrier rules yet",
  emptyDesc: "Add a rule so the Commission Desk can show what each carrier advances.",
  noMatchTitle: "No matches",
  noMatchDesc: "No carrier rules match your search. Try a different carrier name.",

  saved: "Rule saved",
  saveFailed: "Couldn't save the rule",
  deleted: "Rule deleted",
  deleteFailed: "Couldn't delete the rule",
  confirmDelete: (carrier: string) =>
    `Delete the advancing rule for ${carrier}? The Commission Desk will stop showing advancing figures for it.`,
} as const;

/** Copy for the Commission Desk dashboard. */
export const COMMISSION_DASH_MSG = {
  eyebrow: "Finance",
  title: "Commission Dashboard",
  description: "Expected advance and actuals by agency and call centre for the selected month.",
  month: "Month",
  totalSales: "Sales",
  totalPremium: "Premium / mo",
  expectedAdvance: "Expected advance",
  expectedAdvanceHint: "From the carrier advancing rules",
  funded: "On the ledger",
  fundedHint: "Sum of commission lines",
  chargedBack: "Charged back",
  byAgency: "By agency",
  byCallCenter: "By call centre",
  colName: "Name",
  colSales: "Sales",
  colPremium: "Premium",
  colExpected: "Expected advance",
  colFunded: "Funded",
  colChargedBack: "Charged back",
  emptyTitle: "Nothing this month",
  emptyDesc: "No sales were submitted in the selected month.",
} as const;

/** ValidatorStatus name → friendly label (never expose the raw enum to users). */
export const COMMISSION_STATUS_LABEL: Record<string, string> = {
  Completed: "Awaiting review",
  Approved: "Approved",
  ActivePaid: "Active / Paid",
  ChargedBack: "Charged Back",
  Nsf: "NSF (payment bounced)",
  BadBank: "Bad Bank",
  Decline: "Declined",
  ClientCancelled: "Cancelled",
  NoUpdateInCommission: "No commission update",
  ErrorInApplicationInformation: "Application Error",
};
export const commissionStatusLabel = (s: string): string => COMMISSION_STATUS_LABEL[s] ?? s;

export const COMMISSION_STATUS_TONE: Record<string, BadgeTone> = {
  ActivePaid: "success",
  Approved: "success",
  ChargedBack: "danger",
  Nsf: "danger",
  BadBank: "danger",
  Decline: "danger",
  ClientCancelled: "neutral",
  Completed: "info",
  NoUpdateInCommission: "warning",
  ErrorInApplicationInformation: "warning",
};
export const commissionStatusTone = (s: string): BadgeTone => COMMISSION_STATUS_TONE[s] ?? "neutral";

/** Statuses the Commission Agent may set (mirrors the backend CommissionDeskStatuses.Settable). */
export const COMMISSION_SETTABLE_STATUSES = [
  "Approved",
  "ActivePaid",
  "ChargedBack",
  "Nsf",
  "Decline",
  "ClientCancelled",
  "BadBank",
] as const;

/** Statuses that hand the policy to Retention (mirrors CommissionDeskStatuses.MovesToRetention). */
export const MOVES_TO_RETENTION = ["Nsf", "BadBank", "ClientCancelled", "Decline"] as const;
export const goesToRetention = (s: string): boolean =>
  (MOVES_TO_RETENTION as readonly string[]).includes(s);

/** Financially significant changes that need an explicit confirmation step. */
export const NEEDS_CONFIRMATION = ["ChargedBack", ...MOVES_TO_RETENTION] as const;
export const needsConfirmation = (s: string): boolean =>
  (NEEDS_CONFIRMATION as readonly string[]).includes(s);

/** Copy for the Commission Rates screen (what each role earns per sale). */
export const COMMISSION_RATES_MSG = {
  eyebrow: "Finance",
  title: "Commission Rates",
  description:
    "What each role earns on a sale. Set an amount to override the platform default for your agency, or turn a rate off entirely. These drive every commission line the system creates.",

  searchPlaceholder: "Search by rate or who earns it…",
  noMatchTitle: "No matches",
  noMatchDesc: "No commission rate matches your search.",

  colRate: "Rate",
  colEarnedBy: "Earned by",
  colAmount: "Amount",
  colThreshold: "Applies above",
  colEnabled: "Active",

  amountHint: "What this rate pays per sale. Leave blank to use the platform default.",
  thresholdHint: "This rate only pays when the monthly premium reaches this figure.",
  enabledHint: "Turn a rate off to stop it creating commission lines. Existing lines are untouched.",
  usingDefault: (amount: number) => `Default ${amount}`,
  noThreshold: "Always applies",

  save: "Save",
  saved: "Rate saved",
  savedDesc: (label: string) => `${label} has been updated.`,
  saveFailed: "Couldn't save the rate",
  changesNote: "Changes apply to sales closed from now on — commission already recorded is not recalculated.",
} as const;
