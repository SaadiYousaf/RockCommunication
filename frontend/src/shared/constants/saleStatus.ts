import type { BadgeTone } from "../ui";

/**
 * Display metadata for a sale's derived status.
 *
 * The API sends CAPITALISED values — "Funded" / "Validated" / "Pending" (see ListSales, which
 * derives them from FundedAt / ValidatedAt). Two pages previously kept their own private copies of
 * this map and one of them was keyed in lowercase, so every badge on the Sales list silently fell
 * back to the neutral grey tone. One shared map now, keyed to match the API exactly.
 */
export const SALE_STATUS_TONE: Record<string, BadgeTone> = {
  Funded: "success",
  Validated: "info",
  Pending: "warning",
  Internal: "brand",
  Rejected: "danger",
};

/** What users see for a sale status. */
export const SALE_STATUS_LABEL: Record<string, string> = {
  Funded: "Funded",
  Validated: "Validated",
  Pending: "Pending",
  Internal: "Internal",
  Rejected: "Rejected",
};

/** Friendly sale status; tolerates casing drift from the API rather than silently going grey. */
export const saleStatusLabel = (s: string | null | undefined): string => {
  if (!s) return "—";
  return SALE_STATUS_LABEL[s] ?? SALE_STATUS_LABEL[titleCase(s)] ?? s;
};

/** Badge tone for a sale status, defaulting to neutral for anything unrecognised. */
export const saleStatusTone = (s: string | null | undefined): BadgeTone => {
  if (!s) return "neutral";
  return SALE_STATUS_TONE[s] ?? SALE_STATUS_TONE[titleCase(s)] ?? "neutral";
};

const titleCase = (s: string) => (s ? s[0].toUpperCase() + s.slice(1).toLowerCase() : s);
