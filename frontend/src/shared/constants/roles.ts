import type { BadgeTone } from "../ui";

/** Every assignable role name (mirrors the backend Roles constants). */
export const ALL_ROLES = [
  "Admin", "ProgramManager", "TeamLead",
  "Fronter", "Verifier",
  "JrCloser", "Closer", "Validator", "SelfValidator",
  "LicenseAgent",
  "Followups", "Correspondence", "Winbacks",
] as const;

export type RoleName = (typeof ALL_ROLES)[number];

/** Display-name overrides (internal role → what the UI shows). */
export const ROLE_LABELS: Record<string, string> = {
  Validator: "Submission Agent",
  LicenseAgent: "License Agent",
};
export const roleLabel = (r: string): string => ROLE_LABELS[r] ?? r;

/** Badge tone per role, for consistent role chips across the app. */
export const ROLE_TONES: Record<string, BadgeTone> = {
  Admin: "danger", ProgramManager: "danger", TeamLead: "warning",
  Closer: "success", JrCloser: "success", SelfValidator: "success",
  Validator: "info", LicenseAgent: "info", Fronter: "brand", Verifier: "brand",
  Followups: "neutral", Correspondence: "neutral", Winbacks: "neutral",
};

/** Roles that manage / oversee (see all records in scope). */
export const MANAGER_ROLES = ["Admin", "ProgramManager"] as const;
/** Roles with elevated document / admin privileges. */
export const DOCUMENT_ADMIN_ROLES = ["Admin", "ProgramManager", "SuperAdmin"] as const;

/** True if any of the caller's roles is a manager role. */
export const isManager = (roles: readonly string[]): boolean =>
  roles.some((r) => (MANAGER_ROLES as readonly string[]).includes(r));

/**
 * Authority tier for a role — higher outranks lower. Mirrors the backend Roles.RankOfRole so the
 * UI can show which users the caller is allowed to manage (reset password / edit roles). A user's
 * rank is the MAX across their roles.
 */
const ROLE_RANK: Record<string, number> = {
  SuperAdmin: 100, CEO: 80, Admin: 80, ProgramManager: 60, CallCenterAdmin: 60, TeamLead: 40,
};
export const rankOf = (roles: readonly string[]): number =>
  roles.reduce((max, r) => Math.max(max, ROLE_RANK[r] ?? 10), 0);

/**
 * Can a caller manage a target, per the rank rule? SuperAdmin manages anyone; otherwise you may
 * manage only accounts strictly below your rank, or yourself. Never a SuperAdmin target.
 */
export const canManageUser = (
  caller: { id: string; roles: readonly string[] },
  target: { id: string; roles: readonly string[] },
): boolean => {
  if (caller.roles.includes("SuperAdmin")) return true;
  if (target.id === caller.id) return true;
  if (target.roles.includes("SuperAdmin")) return false;
  return rankOf(target.roles) < rankOf(caller.roles);
};
