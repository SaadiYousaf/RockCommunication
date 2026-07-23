import type { BadgeTone } from "../ui";

/** Every assignable role name (mirrors the backend Roles constants). */
export const ALL_ROLES = [
  "Admin", "ProgramManager", "TeamLead",
  "Fronter", "Verifier",
  "JrCloser", "Closer", "Validator", "SelfValidator",
  "Followups", "Correspondence", "Winbacks",
] as const;

export type RoleName = (typeof ALL_ROLES)[number];

/** Display-name overrides (internal role → what the UI shows). */
export const ROLE_LABELS: Record<string, string> = {
  Validator: "Submission Agent",
};
export const roleLabel = (r: string): string => ROLE_LABELS[r] ?? r;

/** Badge tone per role, for consistent role chips across the app. */
export const ROLE_TONES: Record<string, BadgeTone> = {
  Admin: "danger", ProgramManager: "danger", TeamLead: "warning",
  Closer: "success", JrCloser: "success", SelfValidator: "success",
  Validator: "info", Fronter: "brand", Verifier: "brand",
  Followups: "neutral", Correspondence: "neutral", Winbacks: "neutral",
};

/** Roles that manage / oversee (see all records in scope). */
export const MANAGER_ROLES = ["Admin", "ProgramManager"] as const;
/** Roles with elevated document / admin privileges. */
export const DOCUMENT_ADMIN_ROLES = ["Admin", "ProgramManager", "SuperAdmin"] as const;

/** True if any of the caller's roles is a manager role. */
export const isManager = (roles: readonly string[]): boolean =>
  roles.some((r) => (MANAGER_ROLES as readonly string[]).includes(r));
