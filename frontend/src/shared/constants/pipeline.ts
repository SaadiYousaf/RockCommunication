import type { Step } from "../ui";

/**
 * The lead → cash journey, as one shared set of steps so every queue and detail page renders the
 * SAME pipeline and just highlights where it sits. Order matters (left → right).
 *   Intake → Verify → Close → Submit → Fund
 */
export const INTAKE_PIPELINE: Step[] = [
  { key: "intake", label: "Intake" },
  { key: "verify", label: "Verify" },
  { key: "close", label: "Close" },
  { key: "submit", label: "Submit" },
  { key: "fund", label: "Fund" },
];

/** currentIndex into INTAKE_PIPELINE for each queue (the stage that queue is actively working). */
export const PIPELINE_STEP = {
  verify: 1,
  close: 2,
  submit: 3,
} as const;

/**
 * A sale's own lifecycle, for the sale detail page. Derive currentIndex from which timestamps exist:
 *   Created (0) → Sold (1) → Validated (2) → Funded (3)
 */
export const SALE_LIFECYCLE: Step[] = [
  { key: "created", label: "Created" },
  { key: "sold", label: "Sold" },
  { key: "validated", label: "Validated" },
  { key: "funded", label: "Funded" },
];

export function saleLifecycleIndex(sale: { validatedAt?: string | null; fundedAt?: string | null }): number {
  if (sale.fundedAt) return 3;
  if (sale.validatedAt) return 2;
  return 1; // a sale record always exists once we're on its detail page (it's been "Sold")
}
