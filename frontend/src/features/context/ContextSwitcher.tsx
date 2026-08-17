import { useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import type { RootState } from "../../app/store";
import { Icon } from "../../shared/ui";

// Roles that pick a working context — the only ones who see the switcher.
const CONTEXT_ROLES = ["SuperAdmin", "Admin", "CallCenterAdmin"];

/**
 * Top-bar workspace switcher for admins — shows the current scope (call center / agency) and
 * re-opens the context picker so they can change it without logging out. The picker re-issues a
 * scoped session; the store's cache reset (on scope change) refreshes every list under the new scope.
 */
export function ContextSwitcher() {
  const user = useSelector((s: RootState) => s.auth.user);
  const navigate = useNavigate();
  const roles = user?.roles ?? [];
  if (!roles.some((r) => CONTEXT_ROLES.includes(r))) return null;

  const isSuperAdmin = roles.includes("SuperAdmin");
  const label = user?.callCenterName
    ?? (isSuperAdmin ? (user?.agencyName ?? "All agencies") : "All call centers");

  return (
    <button
      type="button" onClick={() => navigate("/select-context")}
      title="Switch workspace" aria-label={`Current workspace: ${label}. Switch workspace.`}
      className="hidden sm:inline-flex items-center gap-2 h-9 max-w-[15rem] rounded-xl border hairline px-2.5 text-sm text-ink-700 hover:border-brand-300 hover:bg-brand-50/40 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
    >
      <Icon name="building" size={15} className="shrink-0 text-brand-600" />
      <span className="truncate">{label}</span>
      <Icon name="chevronsUpDown" size={14} className="shrink-0 text-ink-400" />
    </button>
  );
}
