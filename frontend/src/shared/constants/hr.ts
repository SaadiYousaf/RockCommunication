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

export const OFFER_STATUSES = [
  "Applied", "Interviewed", "Offered", "Accepted", "Training", "Declined", "Rejected", "OnHold", "Hired",
] as const;
export const OFFER_TONE: Record<string, "success" | "warning" | "info" | "danger" | "neutral"> = {
  Applied: "neutral", Interviewed: "info", Offered: "info", Accepted: "success", Training: "info", Hired: "success",
  OnHold: "warning", Declined: "danger", Rejected: "danger",
};

export const ATTENDANCE_STATUSES = ["Present", "Absent", "Late", "HalfDay", "Leave", "Ncns"] as const;
export type AttendanceStatus = (typeof ATTENDANCE_STATUSES)[number];

/** Badge tone per attendance status. */
export const ATTENDANCE_TONE: Record<string, "success" | "warning" | "info" | "danger" | "neutral"> = {
  Present: "success", Late: "warning", HalfDay: "warning", Leave: "info", Absent: "danger", Ncns: "danger",
};

/** Display-name overrides for enum values whose raw name isn't reader-friendly. */
const LABELS: Record<string, string> = {
  SubmissionAgent: "Submission Agent",
  OfficeBoy: "Office Boy",
  HalfDay: "Half day",
  Ncns: "NCNS",
  OnHold: "On hold",
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
