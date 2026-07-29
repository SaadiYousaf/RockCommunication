// Mirrors the backend HR enums (serialized as their string names). Centralised so pages never
// hardcode option lists.

export const DESIGNATIONS = [
  "Fronter", "Verifier", "Closer", "SubmissionAgent", "TeamLead", "HR", "OfficeBoy", "Manager", "Other",
] as const;
export type Designation = (typeof DESIGNATIONS)[number];

export const GENDERS = ["Male", "Female", "Other"] as const;
export const MARITAL_STATUSES = ["Single", "Married", "Divorced", "Widowed"] as const;
export const EMPLOYMENT_STATUSES = [
  "Probation", "Permanent", "Contract", "Intern", "Resigned", "Terminated",
] as const;
export const GUARDIAN_RELATIONSHIPS = [
  "Father", "Mother", "Spouse", "Brother", "Sister", "Son", "Daughter", "Guardian", "Other",
] as const;

/** Display-name overrides for enum values whose raw name isn't reader-friendly. */
const LABELS: Record<string, string> = {
  SubmissionAgent: "Submission Agent",
  OfficeBoy: "Office Boy",
};
export const hrLabel = (v: string | null | undefined): string => (v ? LABELS[v] ?? v : "—");

/** Badge tone for an employment status (green = active, amber = transitional, rose = ended). */
export const EMPLOYMENT_TONE: Record<string, "success" | "warning" | "danger" | "neutral"> = {
  Permanent: "success",
  Probation: "warning",
  Contract: "warning",
  Intern: "warning",
  Resigned: "danger",
  Terminated: "danger",
};
