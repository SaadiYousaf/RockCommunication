import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useSelector } from "react-redux";
import {
  useClockInMutation, useClockOutMutation, useListWrapUpCodesQuery,
  useMyRecentCallsQuery, useMySessionQuery, useSetAgentStatusMutation,
} from "../api/baseApi";
import type { RootState } from "../../app/store";
import { Badge, Button, Icon, Tooltip, cn, useToast } from "../ui";

type AgentStatus = "Available" | "OnCall" | "Break" | "Lunch" | "Training" | "Meeting" | "Offline";

const QUICK_STATES: { key: AgentStatus; label: string; icon: "check" | "clock" | "calendar" }[] = [
  { key: "Available", label: "Available", icon: "check" },
  { key: "Break",     label: "Break",     icon: "clock" },
  { key: "Lunch",     label: "Lunch",     icon: "calendar" },
];

const tone: Record<AgentStatus, { dot: string; chip: string; label: string }> = {
  Available: { dot: "bg-emerald-500", chip: "bg-emerald-50 text-emerald-700 ring-emerald-200", label: "text-emerald-600" },
  OnCall:    { dot: "bg-brand-500",   chip: "bg-brand-50 text-brand-700 ring-brand-200",       label: "text-brand-600" },
  Break:     { dot: "bg-amber-500",   chip: "bg-amber-50 text-amber-700 ring-amber-200",       label: "text-amber-600" },
  Lunch:     { dot: "bg-amber-500",   chip: "bg-amber-50 text-amber-700 ring-amber-200",       label: "text-amber-600" },
  Training:  { dot: "bg-ink-400",     chip: "bg-ink-100 text-ink-700 ring-ink-200",            label: "text-ink-600" },
  Meeting:   { dot: "bg-ink-400",     chip: "bg-ink-100 text-ink-700 ring-ink-200",            label: "text-ink-600" },
  Offline:   { dot: "bg-rose-500",    chip: "bg-rose-50 text-rose-700 ring-rose-200",          label: "text-rose-600" },
};

function formatDuration(ms: number) {
  const sec = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(sec / 3600);
  const m = Math.floor((sec % 3600) / 60);
  const s = sec % 60;
  return `${h.toString().padStart(2, "0")}:${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
}

/**
 * Persistent agent shift bar — sits below the header on every authenticated page
 * for users who have the `agent` module. Lets agents:
 *  - See their current state + shift timer at a glance, no matter what page
 *  - Switch state (Available / Break / Lunch) without navigating to /agent
 *  - See call counts for the day
 *  - Get nudged to wrap up pending calls
 *  - Clock in / out
 *
 * Hidden entirely for users who don't have call-center access.
 */
export function AgentStatusBar() {
  const auth = useSelector((s: RootState) => s.auth);
  const userModules = auth.user?.modules ?? [];
  const isAdmin = (auth.user?.roles ?? []).includes("Admin");
  const hasAgentModule = isAdmin || userModules.includes("agent");
  const toast = useToast();

  // Hooks must always run (rules of hooks); we just guard the render below.
  const sessionQuery = useMySessionQuery(undefined, {
    skip: !auth.accessToken || !hasAgentModule,
    pollingInterval: 60_000,
    refetchOnFocus: true,
  });
  const callsQuery = useMyRecentCallsQuery(20, { skip: !auth.accessToken || !hasAgentModule });
  const codesQuery = useListWrapUpCodesQuery(undefined, { skip: !auth.accessToken || !hasAgentModule });

  const [clockIn, { isLoading: clockingIn }] = useClockInMutation();
  const [clockOut, { isLoading: clockingOut }] = useClockOutMutation();
  const [setStatus, { isLoading: changingStatus }] = useSetAgentStatusMutation();

  const session = sessionQuery.error ? null : sessionQuery.data;
  const recentCalls = callsQuery.data;
  const codes = codesQuery.data;

  const currentStatus: AgentStatus = (session?.currentStatus as AgentStatus) ?? "Offline";
  const t = tone[currentStatus] ?? tone.Offline;

  // Live tick for the shift timer.
  const [, setNow] = useState(0);
  useEffect(() => {
    if (!session) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [session]);

  const stats = useMemo(() => {
    const calls = recentCalls ?? [];
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const todays = calls.filter((c) => new Date(c.initiatedAt) >= today);
    const sales = todays.filter(
      (c) => c.wrapUpCode && (codes ?? []).find((cc) => cc.code === c.wrapUpCode && cc.isSale),
    );
    const pending = calls.filter((c) => c.endedAt && !c.wrapUpCode).length;
    return { total: todays.length, sales: sales.length, pending };
  }, [recentCalls, codes]);

  const [collapsed, setCollapsed] = useState(false);

  // Render guards — must come AFTER all hook calls.
  if (!auth.accessToken) return null;
  if (!hasAgentModule) return null;

  // Off the clock — slim CTA bar.
  if (!session) {
    return (
      <div className="sticky top-16 z-20 bg-white border-b hairline">
        <div className="max-w-[1600px] mx-auto px-6 py-2 flex items-center gap-3">
          <span className="h-2 w-2 rounded-full bg-rose-500" />
          <span className="text-sm text-ink-700">You're <span className="font-semibold">off the clock</span>.</span>
          <span className="text-xs text-ink-500 hidden sm:inline">Clock in to start receiving calls.</span>
          <div className="flex-1" />
          <Button
            size="sm"
            variant="success"
            loading={clockingIn}
            onClick={async () => {
              try {
                await clockIn().unwrap();
                await sessionQuery.refetch();
                toast.success("Clocked in", "Welcome back.");
              } catch (err: any) {
                toast.error("Clock-in failed", err?.data?.detail ?? "Try again.");
              }
            }}
            leftIcon={<Icon name="phone" size={14} />}
          >
            Clock in
          </Button>
        </div>
      </div>
    );
  }

  // On shift — full bar.
  const shiftMs = Date.now() - new Date(session.clockInAt).getTime();

  async function changeState(next: AgentStatus) {
    try {
      await setStatus({ status: next }).unwrap();
      toast.success(`Now ${next}`, "");
    } catch (err: any) {
      toast.error("Couldn't update status", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <div className="sticky top-16 z-20 bg-white/90 backdrop-saturate-160 border-b hairline">
      <div className="max-w-[1600px] mx-auto px-6 py-2 flex items-center gap-4 flex-wrap">
        {/* Status orb + current state */}
        <Link to="/agent" className="flex items-center gap-2.5 hover:opacity-80 transition-opacity">
          <span className="relative inline-flex h-2.5 w-2.5">
            <span className={cn("absolute inset-0 rounded-full opacity-60 animate-pulse-ring", t.dot)} />
            <span className={cn("relative h-2.5 w-2.5 rounded-full", t.dot)} />
          </span>
          <span className={cn("text-sm font-semibold", t.label)}>{currentStatus}</span>
        </Link>

        {/* Shift timer */}
        <Tooltip content={`Clocked in at ${new Date(session.clockInAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`}>
          <div className="hidden sm:inline-flex items-center gap-1.5 text-xs text-ink-600">
            <Icon name="clock" size={12} className="text-ink-400" />
            <span className="font-mono">{formatDuration(shiftMs)}</span>
          </div>
        </Tooltip>

        {/* Quick state buttons */}
        {!collapsed && (
          <div className="hidden md:flex items-center gap-1 ml-2">
            {QUICK_STATES.map((s) => {
              const active = currentStatus === s.key;
              const disabled = currentStatus === "OnCall"; // can't change state mid-call
              return (
                <Tooltip key={s.key} content={disabled ? "Finish your call first" : `Set to ${s.label}`}>
                  <button
                    onClick={() => !disabled && changeState(s.key)}
                    disabled={disabled || changingStatus}
                    className={cn(
                      "inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium transition-colors",
                      "border",
                      active
                        ? `${tone[s.key].chip} ring-1 ring-inset border-transparent`
                        : "border-ink-200 text-ink-600 hover:bg-ink-50 hover:text-ink-900",
                      (disabled || changingStatus) && "opacity-50 cursor-not-allowed",
                    )}
                  >
                    <Icon name={s.icon} size={12} />
                    <span className="hidden lg:inline">{s.label}</span>
                  </button>
                </Tooltip>
              );
            })}
          </div>
        )}

        <div className="flex-1" />

        {/* Pending wrap-up nudge */}
        {stats.pending > 0 && (
          <Link
            to="/agent"
            className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200 hover:bg-amber-100 transition-colors"
          >
            <Icon name="clock" size={12} />
            {stats.pending} wrap-up{stats.pending === 1 ? "" : "s"} pending
          </Link>
        )}

        {/* Today's calls + sales */}
        <Tooltip content="Calls today">
          <div className="hidden sm:inline-flex items-center gap-1.5 text-xs text-ink-600">
            <Icon name="phone" size={12} className="text-ink-400" />
            <span className="font-semibold text-ink-900">{stats.total}</span>
          </div>
        </Tooltip>
        <Tooltip content="Sales today">
          <div className="hidden sm:inline-flex items-center gap-1.5 text-xs text-ink-600">
            <Icon name="briefcase" size={12} className="text-emerald-500" />
            <span className="font-semibold text-ink-900">{stats.sales}</span>
          </div>
        </Tooltip>

        {/* OnCall pulse */}
        {currentStatus === "OnCall" && (
          <Badge tone="brand" variant="solid" dot>On call</Badge>
        )}

        <div className="h-4 w-px bg-ink-200 hidden md:block" />

        {/* Clock out */}
        <Tooltip content="End your shift">
          <button
            onClick={async () => {
              try {
                await clockOut().unwrap();
                await sessionQuery.refetch();
                toast.success("Clocked out", "Have a good one.");
              } catch (err: any) {
                toast.error("Clock-out failed", err?.data?.detail ?? "Try again.");
              }
            }}
            disabled={clockingOut}
            className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium text-rose-600 hover:bg-rose-50 transition-colors disabled:opacity-50"
          >
            <Icon name="logout" size={12} />
            <span className="hidden md:inline">Clock out</span>
          </button>
        </Tooltip>

        {/* Collapse toggle (visible on small viewports too) */}
        <button
          onClick={() => setCollapsed((c) => !c)}
          className="hidden md:inline-flex items-center text-ink-400 hover:text-ink-700 transition-colors"
          title={collapsed ? "Show controls" : "Hide controls"}
          aria-label={collapsed ? "Show controls" : "Hide controls"}
        >
          <Icon name={collapsed ? "menu" : "x"} size={14} />
        </button>
      </div>
    </div>
  );
}
