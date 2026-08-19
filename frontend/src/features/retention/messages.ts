import type { BadgeTone } from "../../shared/ui";

/**
 * Centralized user-facing copy + status metadata for the Retention feature.
 * Keep inline strings out of the component — reference these instead.
 */
export const RETENTION_MSG = {
  eyebrow: "Pipeline",
  title: "Retention",
  description:
    "Policies that need recovery — bad bank, bounced payment (NSF), cancelled, declined or an application error. Work each one and update its status.",

  // Stats
  statOpen: "In retention",
  statOpenHint: "Policies needing recovery",
  statPremium: "Monthly premium",
  statPremiumHint: "At risk across these policies",

  // List
  searchPlaceholder: "Search by customer, carrier or policy…",
  emptyTitle: "Nothing in retention",
  emptyDesc: "No policies currently need recovery — the book is healthy.",
  noMatchTitle: "No matches",
  noMatchDesc: "No policies match your search. Try a different term.",

  // Columns
  colPolicy: "Policy",
  colCustomer: "Customer",
  colCarrier: "Carrier",
  colPremium: "Premium",
  colStatus: "Status",
  colCloser: "Closer",
  colSold: "Sold",

  // Work / resolve modal
  workCta: "Work",
  workTitle: (name: string) => `Work policy — ${name}`,
  currentStatus: "Current status",
  newStatusLabel: "New status",
  noteLabel: "Note",
  noteHelp: "Recorded on the lead's timeline. Required when declining or flagging an application error.",
  notePlaceholder: "What did you do, and what's the outcome?",
  noteRequired: "Please add a note explaining the reason.",
  saveCta: "Save update",
  resolvedOk: "Policy updated",
  resolvedDesc: (name: string) => `${name}'s policy has been updated.`,
  resolveFailed: "Couldn't update the policy",
} as const;

/** ValidatorStatus name → friendly label (never expose the raw enum to users). */
export const RETENTION_STATUS_LABEL: Record<string, string> = {
  BadBank: "Bad Bank",
  Nsf: "NSF (payment bounced)",
  ClientCancelled: "Client Cancelled",
  Decline: "Declined",
  ErrorInApplicationInformation: "Application Error",
  ActivePaid: "Active / Paid",
};
export const retentionStatusLabel = (s: string): string => RETENTION_STATUS_LABEL[s] ?? s;

/** Badge tone per status, for consistent chips. */
export const RETENTION_STATUS_TONE: Record<string, BadgeTone> = {
  BadBank: "danger",
  Nsf: "danger",
  Decline: "danger",
  ErrorInApplicationInformation: "warning",
  ClientCancelled: "neutral",
  ActivePaid: "success",
};
export const retentionStatusTone = (s: string): BadgeTone => RETENTION_STATUS_TONE[s] ?? "neutral";

/**
 * Statuses a retention agent may set. "ActivePaid" = recovered (policy active again); the rest
 * keep it in the worklist with an updated reason. Mirrors the backend RetentionStatuses.Targets.
 */
export const RETENTION_TARGET_STATUSES = [
  "ActivePaid",
  "BadBank",
  "Nsf",
  "ClientCancelled",
  "Decline",
  "ErrorInApplicationInformation",
] as const;
