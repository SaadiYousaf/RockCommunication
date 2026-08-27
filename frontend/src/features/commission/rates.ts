/**
 * The commission rules the engine pays out, described in plain language.
 *
 * The backend keys each rule by an internal code (e.g. "closer-flat-rate"); those codes must never
 * reach the screen. This table maps each one to a human label, who earns it, when it applies, and
 * the platform default that is used when an agency hasn't overridden the amount.
 *
 * Mirrors CRM.Domain.Common.CommissionRuleNames + the rules in CommissionEngine.cs — keep in sync.
 */
export interface CommissionRateMeta {
  /** Internal rule code — sent to the API, never rendered. */
  key: string;
  label: string;
  /** Which role is paid when this rule fires. */
  earnedBy: string;
  /** One line on when the rule applies. */
  description: string;
  /** Platform default amount (used when an agency leaves the amount blank). */
  defaultAmount: number;
  /** Platform default threshold, when the rule only pays above a minimum. */
  defaultThreshold?: number;
  /** True when the rule supports a threshold at all — the input is hidden otherwise. */
  supportsThreshold?: boolean;
}

export const COMMISSION_RATES: CommissionRateMeta[] = [
  {
    key: "closer-flat-rate",
    label: "Closer flat rate",
    earnedBy: "Closer",
    description: "Paid to the closer on every sale they close.",
    defaultAmount: 75,
  },
  {
    key: "high-premium-kicker",
    label: "High premium kicker",
    earnedBy: "Closer",
    description: "Extra on top of the flat rate when the monthly premium reaches the threshold.",
    defaultAmount: 25,
    defaultThreshold: 200,
    supportsThreshold: true,
  },
  {
    key: "jr-closer-split",
    label: "Junior closer split",
    earnedBy: "Junior Closer",
    description: "Paid to a junior closer who assisted on the call.",
    defaultAmount: 30,
  },
  {
    key: "validator-bonus",
    label: "Submission agent bonus",
    earnedBy: "Submission Agent",
    description: "Paid when the sale is validated. Not paid on internal sales.",
    defaultAmount: 15,
  },
  {
    key: "license-agent-approval",
    label: "License agent approval",
    earnedBy: "License Agent",
    description: "Paid to the licensed agent assigned at approval. Not paid on internal sales.",
    defaultAmount: 50,
  },
  {
    key: "team-lead-override",
    label: "Team lead override",
    earnedBy: "Team Lead",
    description: "Paid to the closer's team lead, when the closer belongs to a team that has one.",
    defaultAmount: 10,
  },
];

/** Internal rule code -> the label users see. Falls back to the code only if a rule is unknown. */
const RATE_LABEL_BY_KEY: Record<string, string> =
  Object.fromEntries(COMMISSION_RATES.map((r) => [r.key, r.label]));

/**
 * Friendly name for a commission line. Commission entries are stored under their internal rule
 * code ("closer-flat-rate"), which must never reach the screen.
 */
export function commissionRuleLabel(ruleName: string): string {
  if (RATE_LABEL_BY_KEY[ruleName]) return RATE_LABEL_BY_KEY[ruleName];
  // Unknown rule (e.g. one added backend-side before this table caught up): de-kebab it so the
  // user still sees words rather than a raw code.
  return ruleName
    .split("-")
    .map((w) => (w ? w[0].toUpperCase() + w.slice(1) : w))
    .join(" ");
}

/** Which role earns a given rule, for context next to the amount. Empty when unknown. */
export function commissionRuleEarnedBy(ruleName: string): string {
  return COMMISSION_RATES.find((r) => r.key === ruleName)?.earnedBy ?? "";
}
