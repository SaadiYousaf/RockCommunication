import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";

/**
 * Authority tiers, mirroring CRM.Domain.Enums.Roles.RankOfRole on the server.
 *
 * WHY THIS EXISTS: some endpoints gate on RANK rather than on a permission code — the team-status
 * widget, for example, is "TeamLead and above". The client had no way to ask that question, so it
 * requested those endpoints for everyone and every agent got a 403 all day long: the panel rendered
 * blank and (once failures became visible) raised an error toast. Asking only for what the caller
 * may actually have is the fix; the server check stays exactly where it is.
 *
 * Keep in sync with the server. If the two ever disagree the server still wins — the worst a stale
 * value here can do is hide a panel the user could have seen, never expose one they could not.
 */
const RANKS: Record<string, number> = {
  SuperAdmin: 100,
  CEO: 80,
  Admin: 80,
  ProgramManager: 60,
  CallCenterAdmin: 60,
  TeamLead: 40,
};

/** Everyone else — agents (Fronter/Closer/Validator/…) and any role not listed. */
const DEFAULT_RANK = 10;

/** TeamLead and above: the floor the server uses for "supervisory/management". */
export const SUPERVISOR_RANK = 40;

export function rankOfRole(role: string): number {
  return RANKS[role] ?? DEFAULT_RANK;
}

export function rankOf(roles: readonly string[] | undefined | null): number {
  if (!roles || roles.length === 0) return 0;
  return Math.max(...roles.map(rankOfRole));
}

/** The signed-in user's highest authority tier. */
export function useRank(): number {
  const roles = useSelector((s: RootState) => s.auth.user?.roles);
  return rankOf(roles);
}

/**
 * True for TeamLead and above, or for HR — matching the server's TeamStatus gate
 * (HrAccess.IsHr(user) || RankOf(user.Roles) >= SupervisorRank).
 */
export function useIsSupervisor(): boolean {
  const roles = useSelector((s: RootState) => s.auth.user?.roles);
  if (!roles) return false;
  if (roles.some((r) => r === "HR")) return true;
  return rankOf(roles) >= SUPERVISOR_RANK;
}
