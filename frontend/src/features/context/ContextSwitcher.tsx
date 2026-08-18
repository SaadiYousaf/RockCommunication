import { useEffect, useRef, useState } from "react";
import { useDispatch, useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import { getErrorDetail } from "../../shared/api/apiError";
import { setAuth } from "../../app/store";
import type { RootState } from "../../app/store";
import { useListCallCentersQuery, useSetContextMutation } from "../../shared/api/baseApi";
import { Icon, type IconName, Spinner, cn, useToast } from "../../shared/ui";

// Roles that pick a working context — the only ones who see the switcher.
const CONTEXT_ROLES = ["SuperAdmin", "Admin", "CallCenterAdmin"];

/**
 * Top-bar workspace switcher. A LIGHTWEIGHT dropdown — an agency admin flips between call centers
 * in one click (no full-screen interruption); a Super Admin gets quick "all agencies" + a link to
 * the full picker; a pinned Call Center Admin just sees their center. Switching re-issues a scoped
 * session inline and the store's cache-reset refreshes every list under the new scope.
 */
export function ContextSwitcher() {
  const user = useSelector((s: RootState) => s.auth.user);
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const [setContext, { isLoading }] = useSetContextMutation();

  const roles = user?.roles ?? [];
  const isSuperAdmin = roles.includes("SuperAdmin");
  const isCallCenterAdmin = !isSuperAdmin && !roles.includes("Admin") && !!user?.callCenterId;
  const canSwitch = roles.some((r) => CONTEXT_ROLES.includes(r));

  // Only an agency admin needs the call-center list, and only once the menu is open.
  const { data: centers, isFetching } = useListCallCentersQuery(undefined, { skip: !open || isSuperAdmin || isCallCenterAdmin });

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false); };
    const onEsc = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onEsc);
    return () => { document.removeEventListener("mousedown", onDoc); document.removeEventListener("keydown", onEsc); };
  }, [open]);

  if (!canSwitch || !user) return null;

  const currentCcId = user.callCenterId ?? null;
  const label = user.callCenterName ?? (isSuperAdmin ? (user.agencyName ?? "All agencies") : "All call centers");

  async function switchTo(callCenterId: string | null) {
    setOpen(false);
    if (callCenterId === currentCcId) return;   // already here — no-op
    try {
      const res = await setContext({ callCenterId }).unwrap();
      if (!res.user) return;
      dispatch(setAuth({ accessToken: res.accessToken, refreshToken: res.refreshToken, user: res.user, contextChosen: true }));
      toast.success("Workspace switched", res.user.callCenterName ?? "Now viewing all call centers");
    } catch (err: unknown) {
      toast.error("Couldn't switch workspace", getErrorDetail(err) ?? "Please try again.");
    }
  }

  // A pinned Call Center Admin can't switch — just show their center.
  if (isCallCenterAdmin) {
    return (
      <span className="hidden sm:inline-flex items-center gap-2 h-9 max-w-[15rem] rounded-xl border hairline px-2.5 text-sm text-ink-600">
        <Icon name="headset" size={15} className="shrink-0 text-brand-600" />
        <span className="truncate">{label}</span>
      </span>
    );
  }

  return (
    <div className="relative hidden sm:block" ref={ref}>
      <button
        type="button" onClick={() => setOpen((o) => !o)}
        title="Switch workspace" aria-haspopup="menu" aria-expanded={open}
        className="inline-flex items-center gap-2 h-9 max-w-[15rem] rounded-xl border hairline px-2.5 text-sm text-ink-700 hover:border-brand-300 hover:bg-brand-50/40 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
      >
        <Icon name="building" size={15} className="shrink-0 text-brand-600" />
        <span className="truncate">{label}</span>
        {isLoading ? <Spinner size={14} /> : <Icon name="chevronsUpDown" size={14} className="shrink-0 text-ink-400" />}
      </button>

      {open && (
        <div role="menu" className="absolute right-0 mt-1.5 w-64 rounded-xl border border-ink-200 bg-white shadow-pop p-1.5 z-50 animate-scale-in origin-top-right">
          <div className="px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-ink-400">Switch workspace</div>

          {isSuperAdmin ? (
            <>
              <MenuItem icon="globe" label="All agencies (platform)" active={!user.agencyName} onClick={() => switchTo(null)} />
              <div className="my-1 h-px bg-ink-100" />
              <button type="button" onClick={() => { setOpen(false); navigate("/select-context"); }}
                className="w-full flex items-center gap-2.5 rounded-lg px-2 py-2 text-sm text-ink-700 hover:bg-ink-50 transition-colors">
                <span className="grid h-6 w-6 place-items-center rounded-md bg-ink-100 text-ink-500"><Icon name="building" size={14} /></span>
                Change agency / call center…
              </button>
            </>
          ) : (
            <div className="max-h-72 overflow-y-auto">
              <MenuItem icon="building" label="All call centers" active={!currentCcId} onClick={() => switchTo(null)} />
              {isFetching && <div className="px-2 py-2 flex justify-center"><Spinner size={16} /></div>}
              {(centers ?? []).filter((c) => c.isActive).map((c) => (
                <MenuItem key={c.id} icon="headset" label={c.name} active={c.id === currentCcId} onClick={() => switchTo(c.id)} />
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function MenuItem({ icon, label, active, onClick }: { icon: IconName; label: string; active: boolean; onClick: () => void }) {
  return (
    <button type="button" role="menuitem" onClick={onClick}
      className={cn("w-full flex items-center gap-2.5 rounded-lg px-2 py-2 text-sm transition-colors",
        active ? "bg-brand-50 text-brand-700 font-medium" : "text-ink-700 hover:bg-ink-50")}>
      <span className={cn("grid h-6 w-6 place-items-center rounded-md", active ? "bg-brand-100 text-brand-600" : "bg-ink-100 text-ink-500")}>
        <Icon name={icon} size={14} />
      </span>
      <span className="truncate">{label}</span>
      {active && <Icon name="check" size={15} className="ml-auto text-brand-600" />}
    </button>
  );
}
