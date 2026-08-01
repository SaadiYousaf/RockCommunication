import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useSelector } from "react-redux";
import { Link } from "react-router-dom";
import type { RootState } from "../app/store";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Skeleton, cn,
  type IconName, type BadgeTone,
} from "../shared/ui";
import { useDashboardSummaryQuery, useLeaderboardQuery, useWallboardQuery, useUpcomingEventsQuery, useTeamStatusQuery } from "../shared/api/baseApi";
import { useDashboardLayout } from "./useDashboardLayout";
import { usePermission, Perm } from "../shared/auth/permissions";
import type { DashboardStageBucket, DashboardSummary, WorkflowStage, TeamStatusRow, TeamLiveStatus } from "../shared/api/types";
import { STAGE_TONE as stageTone } from "../shared/constants/leadStage";
import type { WallboardSnapshot, AgentLeaderboard } from "../shared/api/types";

const stageOrder: WorkflowStage[] = [
  "New", "Fronted", "Verified", "JrClosed", "Closed", "Validated", "Funded", "Followup", "Winback", "Lost",
];


const stageBar: Record<WorkflowStage, string> = {
  New:       "from-brand-400 to-brand-600",
  Fronted:   "from-brand-400 to-brand-600",
  Verified:  "from-brand-400 to-brand-600",
  JrClosed:  "from-amber-400 to-amber-600",
  Closed:    "from-orange-400 to-orange-600",
  Validated: "from-emerald-400 to-emerald-600",
  Funded:    "from-emerald-500 to-teal-600",
  Followup:  "from-accent-400 to-accent-600",
  Winback:   "from-accent-400 to-accent-600",
  Lost:      "from-rose-400 to-rose-600",
};

function formatMoney(n: number) {
  if (n >= 1_000_000) return `$${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1000) return `$${(n / 1000).toFixed(1)}K`;
  return `$${Math.round(n).toLocaleString()}`;
}

function pctDelta(current: number, prior: number): { delta: string; trend: "up" | "down" | "flat" } {
  if (prior === 0) {
    if (current === 0) return { delta: "0%", trend: "flat" };
    return { delta: "new", trend: "up" };
  }
  const pct = ((current - prior) / prior) * 100;
  const trend = pct > 0.5 ? "up" : pct < -0.5 ? "down" : "flat";
  return { delta: `${pct >= 0 ? "+" : ""}${pct.toFixed(1)}%`, trend };
}

function timeAgo(iso: string) {
  const diff = Date.now() - new Date(iso).getTime();
  const m = Math.floor(diff / 60000);
  if (m < 1) return "just now";
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  const d = Math.floor(h / 24);
  return `${d}d ago`;
}

// =============================================================================
// Configurable-widget registry
// -----------------------------------------------------------------------------
// The single source of truth for what the user can show / hide / reorder on the
// landing dashboard. Hero + KpiStrip are intentionally NOT here — they stay fixed.
// `colSpan` maps to how wide the cell sits in a 3-column xl grid, preserving each
// widget's current visual size while letting them reflow when reordered/hidden.
// =============================================================================

type WidgetColSpan = 1 | 2 | 3;
interface WidgetDef {
  id: string;
  label: string;
  colSpan: WidgetColSpan;
}

const DASHBOARD_WIDGETS: readonly WidgetDef[] = [
  { id: "upcoming-events", label: "Upcoming events",  colSpan: 3 },
  { id: "team-status",     label: "Team status",       colSpan: 3 },
  { id: "pipeline",        label: "Pipeline overview", colSpan: 2 },
  { id: "floor",           label: "Floor health",      colSpan: 1 },
  { id: "activity",        label: "Recent activity",   colSpan: 2 },
  { id: "leaderboard",     label: "Top performers",    colSpan: 1 },
  { id: "quick-actions",   label: "Quick actions",     colSpan: 3 },
] as const;

const WIDGET_BY_ID: Record<string, WidgetDef> = Object.fromEntries(
  DASHBOARD_WIDGETS.map((w) => [w.id, w]),
);
const DEFAULT_WIDGET_ORDER: readonly string[] = DASHBOARD_WIDGETS.map((w) => w.id);

// Literal strings so Tailwind's JIT scanner keeps these classes in the build.
const COL_SPAN_CLASS: Record<WidgetColSpan, string> = {
  1: "",
  2: "xl:col-span-2",
  3: "xl:col-span-3",
};

const COL_SPAN_LABEL: Record<WidgetColSpan, string> = {
  1: "Compact",
  2: "Wide",
  3: "Full width",
};

export function Dashboard() {
  const auth = useSelector((s: RootState) => s.auth);
  const userName = auth.user?.userName ?? "there";
  const role = auth.user?.roles?.[0] ?? null;

  // Wallboard + leaderboard are the two most expensive endpoints (backend N+1s).
  // Only supervisory users get them polled — a floor agent should not poll these.
  const canSeeSupervision = usePermission(Perm.SupervisorView);

  // Poll so the KPI strip, pipeline, activity feed and open-callback count stay near-real-time
  // without a manual refresh (mirrors the wallboard/leaderboard cadence below).
  const { data, isLoading, isError, refetch } = useDashboardSummaryQuery(undefined, { pollingInterval: 30_000 });
  const { data: leaders } = useLeaderboardQuery("today", {
    pollingInterval: 60_000,
    skip: !canSeeSupervision,
  });
  const { data: wall } = useWallboardQuery(undefined, {
    pollingInterval: 30_000,
    skip: !canSeeSupervision,
  });

  // Per-browser dashboard layout (which widgets show, and in what order).
  const layout = useDashboardLayout(DEFAULT_WIDGET_ORDER);
  const [customizing, setCustomizing] = useState(false);
  const [dragId, setDragId] = useState<string | null>(null);
  const [dragOverId, setDragOverId] = useState<string | null>(null);

  // Maps each widget id to its already-existing section component (props unchanged).
  const widgetNodes: Record<string, ReactNode> = {
    "upcoming-events": <UpcomingEventsCard />,
    "team-status": <TeamStatusCard />,
    "pipeline": <PipelineCard data={data} loading={isLoading} />,
    "floor": <FloorCard wall={wall} loading={isLoading} />,
    "activity": <ActivityCard data={data} loading={isLoading} />,
    "leaderboard": <LeaderboardCard leaders={leaders} loading={isLoading} />,
    "quick-actions": <QuickActions />,
  };

  const handleDrop = (targetId: string) => {
    if (dragId && dragId !== targetId) layout.reorder(dragId, targetId);
    setDragId(null);
    setDragOverId(null);
  };

  return (
    <>
      <Hero userName={userName} role={role} data={data} loading={isLoading} onRefresh={refetch} />

      {isError && (
        <Card className="mb-6">
          <CardBody>
            <EmptyState
              icon={<Icon name="error" size={20} />}
              title="Couldn't load dashboard"
              description="The dashboard service is unavailable. Make sure the backend is running."
              action={<Button onClick={() => refetch()}>Retry</Button>}
            />
          </CardBody>
        </Card>
      )}

      {/* KPI strip */}
      <KpiStrip data={data} loading={isLoading} />

      {/* Customize toolbar */}
      <div className="flex items-center justify-between gap-3 mb-3">
        <div className="flex items-center gap-1.5 min-w-0">
          <span className="text-xs font-semibold uppercase tracking-wider text-ink-500">Your dashboard</span>
          <InfoHint title="Customize your dashboard" side="right">
            Show, hide and reorder your dashboard widgets — saved to this browser.
          </InfoHint>
        </div>
        <Button
          variant={customizing ? "primary" : "outline"}
          size="sm"
          leftIcon={<Icon name="cog" size={14} />}
          onClick={() => setCustomizing((v) => !v)}
          aria-expanded={customizing}
          aria-label={customizing ? "Close dashboard customization" : "Customize dashboard"}
        >
          {customizing ? "Done" : "Customize"}
        </Button>
      </div>

      {customizing && (
        <CustomizePanel layout={layout} onClose={() => setCustomizing(false)} />
      )}

      {/* Configurable widget grid */}
      {layout.visibleOrder.length === 0 ? (
        <Card className="mb-5">
          <CardBody>
            <EmptyState
              tone="neutral"
              icon={<Icon name="layers" size={20} />}
              title="All widgets are hidden"
              description="Your dashboard is empty. Re-enable widgets in Customize, or restore the default layout."
              action={
                <Button variant="outline" size="sm" leftIcon={<Icon name="refresh" size={14} />} onClick={layout.reset}>
                  Reset to default
                </Button>
              }
            />
          </CardBody>
        </Card>
      ) : (
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-5 mb-5">
          {layout.visibleOrder.map((id) => {
            const w = WIDGET_BY_ID[id];
            if (!w) return null;
            return (
              <div
                key={id}
                className={cn(
                  COL_SPAN_CLASS[w.colSpan],
                  "min-w-0",
                  customizing && "relative rounded-2xl ring-2 ring-brand-200/70 ring-offset-2 cursor-move transition-shadow",
                  customizing && dragOverId === id && dragId !== id && "ring-brand-500",
                  customizing && dragId === id && "opacity-60",
                )}
                draggable={customizing}
                onDragStart={customizing ? () => setDragId(id) : undefined}
                onDragOver={customizing ? (e) => { e.preventDefault(); setDragOverId(id); } : undefined}
                onDragLeave={customizing ? () => setDragOverId((cur) => (cur === id ? null : cur)) : undefined}
                onDrop={customizing ? (e) => { e.preventDefault(); handleDrop(id); } : undefined}
                onDragEnd={customizing ? () => { setDragId(null); setDragOverId(null); } : undefined}
              >
                {customizing && (
                  <div className="absolute -top-2.5 left-3 z-10 inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-brand-600 text-white text-[10px] font-semibold shadow-sm">
                    <Icon name="moreV" size={11} /> Drag to reorder
                  </div>
                )}
                {/* Block interaction with the widget's own links while dragging. */}
                <div className={cn(customizing && "pointer-events-none")}>
                  {widgetNodes[id]}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </>
  );
}

// =============================================================================
// Customize panel — show/hide toggles + reorder controls for every widget
// =============================================================================

function CustomizePanel({
  layout, onClose,
}: {
  layout: ReturnType<typeof useDashboardLayout>;
  onClose: () => void;
}) {
  return (
    <Card className="mb-4">
      <CardHeader
        title="Customize dashboard"
        subtitle="Show, hide and drag or nudge widgets into the order you want"
        action={
          <div className="flex items-center gap-1.5">
            <Button variant="ghost" size="sm" leftIcon={<Icon name="refresh" size={14} />} onClick={layout.reset}>
              Reset to default
            </Button>
            <Button variant="ghost" size="icon" onClick={onClose} aria-label="Close customization panel">
              <Icon name="x" size={16} />
            </Button>
          </div>
        }
        bordered
      />
      <CardBody className="space-y-1">
        {layout.order.map((id, idx) => {
          const w = WIDGET_BY_ID[id];
          if (!w) return null;
          const hidden = layout.isHidden(id);
          return (
            <div
              key={id}
              className={cn(
                "flex items-center gap-3 rounded-xl px-3 py-2 ring-1 ring-inset transition-colors",
                hidden ? "bg-ink-50/50 ring-ink-100" : "bg-white ring-ink-200 hover:ring-ink-300",
              )}
            >
              <Button
                variant={hidden ? "ghost" : "outline"}
                size="sm"
                leftIcon={<Icon name={hidden ? "eyeOff" : "eye"} size={14} />}
                onClick={() => layout.toggle(id)}
                aria-pressed={!hidden}
                aria-label={hidden ? `Show ${w.label}` : `Hide ${w.label}`}
                className="w-24 justify-start"
              >
                {hidden ? "Hidden" : "Shown"}
              </Button>

              <div className={cn("flex-1 min-w-0", hidden && "opacity-50")}>
                <div className="text-sm font-semibold text-ink-900 truncate">{w.label}</div>
                <div className="text-[11px] text-ink-500">{COL_SPAN_LABEL[w.colSpan]}</div>
              </div>

              <div className="flex items-center gap-1 shrink-0">
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => layout.move(id, -1)}
                  disabled={idx === 0}
                  aria-label={`Move ${w.label} up`}
                >
                  <Icon name="chevronUp" size={16} />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => layout.move(id, 1)}
                  disabled={idx === layout.order.length - 1}
                  aria-label={`Move ${w.label} down`}
                >
                  <Icon name="chevronDown" size={16} />
                </Button>
              </div>
            </div>
          );
        })}
      </CardBody>
    </Card>
  );
}

// =============================================================================
// Hero — branded greeting + clock + primary actions
// =============================================================================

function Hero({
  userName, role, data, loading, onRefresh,
}: {
  userName: string; role: string | null;
  data?: DashboardSummary; loading: boolean;
  onRefresh: () => void;
}) {
  const [now, setNow] = useState(new Date());
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(id);
  }, []);

  const greeting = (() => {
    const h = now.getHours();
    if (h < 12) return "Good morning";
    if (h < 18) return "Good afternoon";
    return "Good evening";
  })();

  const dateStr = now.toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" });
  const timeStr = now.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });

  // 1–3 actionable focus items, derived from the summary.
  type Focus = { tone: "brand" | "warning" | "success" | "info"; icon: IconName; title: string; sub: string; to: string };
  const focuses: Focus[] = useMemo(() => {
    if (!data) return [];
    const arr: Focus[] = [];
    if (data.openCallbacks > 0) arr.push({
      tone: "warning", icon: "calendar",
      title: `${data.openCallbacks} callback${data.openCallbacks === 1 ? "" : "s"} due`,
      sub: "Reach out before the day gets busy.",
      to: "/callbacks",
    });
    if (data.activeLeads > 0) arr.push({
      tone: "brand", icon: "list",
      title: `${data.activeLeads} active leads`,
      sub: "Move them forward — every stage matters.",
      to: "/leads",
    });
    if (Number(data.salesThisWeek) > 0) arr.push({
      tone: "success", icon: "briefcase",
      title: `${formatMoney(Number(data.salesThisWeek))} booked this week`,
      sub: "Validate and fund pending deals.",
      to: "/sales",
    });
    if (arr.length === 0) arr.push({
      tone: "info", icon: "plus",
      title: "Your floor is quiet", sub: "Import or create your first lead.",
      to: "/leads",
    });
    return arr.slice(0, 3);
  }, [data]);

  return (
    <div
      className="relative overflow-hidden rounded-3xl mb-5 ring-1 ring-brand-100/80"
      style={{
        background:
          "radial-gradient(ellipse at top right, rgba(60,114,105,0.20), transparent 55%), " +
          "radial-gradient(ellipse at bottom left, rgba(178,133,53,0.10), transparent 55%), " +
          "linear-gradient(135deg, #f0f9ff 0%, #ffffff 55%, #f5f3ff 100%)",
      }}
    >
      {/* Light dot grid for tactile surface */}
      <div
        className="pointer-events-none absolute inset-0 opacity-50"
        style={{
          backgroundImage: "radial-gradient(rgba(60,114,105,0.10) 1px, transparent 1px)",
          backgroundSize: "22px 22px",
        }}
      />

      <div className="relative p-6 md:p-8">
        <div className="flex items-start justify-between flex-wrap gap-6 mb-6">
          <div className="min-w-0">
            <div className="flex items-center gap-2.5 mb-3">
              <span className="inline-flex items-center gap-2 px-2.5 py-1 rounded-full bg-emerald-50 ring-1 ring-emerald-200">
                <span className="relative flex h-1.5 w-1.5">
                  <span className="absolute inset-0 rounded-full bg-emerald-500 animate-ping opacity-75" />
                  <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-emerald-500" />
                </span>
                <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-emerald-700">Live</span>
              </span>
              <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-brand-700">
                {greeting}
              </span>
            </div>
            <h1 className="text-3xl md:text-4xl font-bold tracking-tight text-ink-900 text-balance">
              Welcome back, {userName}
            </h1>
            <p className="text-ink-500 text-sm mt-1.5">
              {role ? `${role} · ` : ""}{dateStr} · {timeStr}
            </p>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <button
              onClick={onRefresh}
              className="inline-flex items-center gap-2 px-3.5 h-10 rounded-xl bg-white hover:bg-ink-50 ring-1 ring-ink-200 text-sm font-medium text-ink-700 shadow-xs transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
            >
              <Icon name="refresh" size={15} /> Refresh
            </button>
            <Link
              to="/leads"
              className="inline-flex items-center gap-2 px-4 h-10 rounded-xl bg-gradient-to-b from-brand-500 to-brand-600 hover:from-brand-500 hover:to-brand-700 text-white text-sm font-semibold shadow-glow transition-all"
            >
              <Icon name="plus" size={15} /> New lead
            </Link>
          </div>
        </div>

        {/* Focus strip */}
        {loading ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-24 rounded-xl" />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            {focuses.map((f, i) => (
              <Link
                key={i}
                to={f.to}
                className="group relative overflow-hidden rounded-xl p-4 bg-white/85 backdrop-blur hover:bg-white ring-1 ring-ink-200/70 hover:ring-brand-300 hover:shadow-card-hover transition-all"
              >
                <div className="flex items-start gap-3">
                  <div className={`h-9 w-9 rounded-lg grid place-items-center shrink-0 ring-1 ring-inset ${focusToneCls(f.tone)}`}>
                    <Icon name={f.icon} size={16} />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-semibold text-ink-900 leading-snug">{f.title}</div>
                    <div className="text-xs text-ink-500 mt-0.5">{f.sub}</div>
                  </div>
                  <Icon name="chevronRight" size={16} className="text-ink-300 group-hover:text-brand-600 group-hover:translate-x-0.5 transition-all" />
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function focusToneCls(tone: "brand" | "warning" | "success" | "info") {
  return ({
    brand:   "bg-brand-50 text-brand-600 ring-brand-100",
    warning: "bg-amber-50 text-amber-600 ring-amber-100",
    success: "bg-emerald-50 text-emerald-600 ring-emerald-100",
    info:    "bg-brand-50 text-brand-600 ring-brand-100",
  })[tone];
}

// =============================================================================
// KPI strip — 4 metrics with deltas + sparklines
// =============================================================================

function KpiStrip({ data, loading }: { data?: DashboardSummary; loading: boolean }) {
  if (loading || !data) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4 mb-5">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="surface p-5">
            <Skeleton className="h-3 w-20 mb-3" />
            <Skeleton className="h-8 w-32 mb-3" />
            <Skeleton className="h-6 w-24" />
          </div>
        ))}
      </div>
    );
  }

  const leadDelta  = pctDelta(data.leadsLast7Days, data.leadsPrior7Days);
  const salesDelta = pctDelta(data.salesThisWeek, data.salesPrior7Days);

  // Synthetic 7-point trends from prior/current totals — gives the eye a shape
  // even before we wire up real time-series for these widgets.
  const sparkLead  = synthesizeTrend(data.leadsPrior7Days, data.leadsLast7Days);
  const sparkSales = synthesizeTrend(data.salesPrior7Days, data.salesThisWeek);

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4 mb-5">
      <KpiTile
        to="/leads" label="Active Leads" value={data.activeLeads.toLocaleString()}
        delta={leadDelta.delta} trend={leadDelta.trend} icon="list" tone="brand" sparkline={sparkLead}
      />
      <KpiTile
        to="/sales" label="Sales This Week" value={formatMoney(Number(data.salesThisWeek))}
        delta={salesDelta.delta} trend={salesDelta.trend} icon="briefcase" tone="success" sparkline={sparkSales}
      />
      <KpiTile
        to="/kpis"
        label={<span className="inline-flex items-center gap-1">Conversion<InfoHint title="Conversion" side="bottom">The share of your leads that turned into sales.</InfoHint></span>}
        value={`${data.conversionRate}%`}
        delta={data.conversionRate >= 15 ? "Healthy" : "Needs focus"}
        trend={data.conversionRate >= 15 ? "up" : "down"} icon="chart" tone="accent"
        sparkline={[14, 13, 16, 15, 17, 16, data.conversionRate]}
      />
      <KpiTile
        to="/callbacks" label="Open Callbacks" value={data.openCallbacks.toLocaleString()}
        delta={data.openCallbacks === 0 ? "All clear" : "Pending"}
        trend={data.openCallbacks === 0 ? "up" : "flat"} icon="calendar" tone="warning"
      />
    </div>
  );
}

function synthesizeTrend(prior: number, current: number): number[] {
  const start = Math.max(0, prior / 7);
  const end = Math.max(0, current / 7);
  return Array.from({ length: 7 }, (_, i) => {
    const t = i / 6;
    const wobble = Math.sin(i * 1.3) * Math.max(start, end) * 0.08;
    return Math.max(0, start + (end - start) * t + wobble);
  });
}

const kpiToneMap: Record<"brand" | "success" | "accent" | "warning",
  { iconBg: string; iconText: string; spark: string; ring: string }> = {
  brand:   { iconBg: "bg-brand-50",   iconText: "text-brand-600",   spark: "#3c7269", ring: "ring-brand-100" },
  success: { iconBg: "bg-emerald-50", iconText: "text-emerald-600", spark: "#10b981", ring: "ring-emerald-100" },
  accent:  { iconBg: "bg-accent-50",  iconText: "text-accent-600",  spark: "#b28535", ring: "ring-accent-100" },
  warning: { iconBg: "bg-amber-50",   iconText: "text-amber-600",   spark: "#f59e0b", ring: "ring-amber-100" },
};

function KpiTile({
  to, label, value, delta, trend, icon, tone, sparkline,
}: {
  to: string; label: ReactNode; value: string; delta: string;
  trend: "up" | "down" | "flat"; icon: IconName;
  tone: "brand" | "success" | "accent" | "warning";
  sparkline?: number[];
}) {
  const t = kpiToneMap[tone];
  const trendColor = trend === "up" ? "text-emerald-700 bg-emerald-50" : trend === "down" ? "text-rose-700 bg-rose-50" : "text-ink-600 bg-ink-100";
  return (
    <Link
      to={to}
      className="group surface relative overflow-hidden p-5 block hover:shadow-card-hover hover:border-ink-300 transition-all"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="section-title">{label}</div>
        <div className={`h-9 w-9 rounded-lg grid place-items-center ring-1 ring-inset ${t.iconBg} ${t.iconText} ${t.ring}`}>
          <Icon name={icon} size={16} />
        </div>
      </div>
      <div className="mt-3 flex items-baseline gap-2">
        <div className="text-3xl font-bold tracking-tight tabular-nums text-ink-900 leading-none">{value}</div>
        <div className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md text-[11px] font-semibold ${trendColor}`}>
          {trend === "up" && <Icon name="trendUp" size={11} />}
          {trend === "down" && <Icon name="trendDown" size={11} />}
          {trend === "flat" && <span>—</span>}
          {delta}
        </div>
      </div>
      {sparkline && sparkline.length > 1 && (
        <div className="mt-3 -mx-1">
          <Sparkline values={sparkline} color={t.spark} />
        </div>
      )}
      <Icon name="chevronRight" size={14} className="absolute bottom-4 right-4 text-ink-300 group-hover:text-brand-500 group-hover:translate-x-0.5 transition-all" />
    </Link>
  );
}

function Sparkline({ values, color }: { values: number[]; color: string }) {
  const w = 100, h = 28;
  const min = Math.min(...values), max = Math.max(...values);
  const range = max - min || 1;
  const step = w / (values.length - 1);
  const pts = values.map((v, i) => `${i * step},${h - ((v - min) / range) * h}`);
  const id = `spark-${color.replace("#", "")}`;
  return (
    <svg viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" className="w-full h-7">
      <defs>
        <linearGradient id={id} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.30" />
          <stop offset="100%" stopColor={color} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={`M ${pts.join(" L ")} L ${w},${h} L 0,${h} Z`} fill={`url(#${id})`} />
      <path d={`M ${pts.join(" L ")}`} stroke={color} strokeWidth="1.5" fill="none" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

// =============================================================================
// Pipeline funnel — proper bar visualization, not 5 plain tiles
// =============================================================================

function PipelineCard({ data, loading }: { data?: DashboardSummary; loading: boolean }) {
  return (
    <Card className="xl:col-span-2 overflow-hidden">
      <CardHeader
        title={<span className="inline-flex items-center gap-1">Pipeline overview<InfoHint title="Pipeline stages" side="bottom">Where each active lead sits in the sales flow: New, Fronted, Verified, Closed, Validated, Funded, then Followup, Winback or Lost.</InfoHint></span>}
        subtitle="Stage distribution across all active leads"
        action={
          <Link to="/leads">
            <Button variant="ghost" size="sm" rightIcon={<Icon name="arrowRight" size={14} />}>
              View all
            </Button>
          </Link>
        }
        bordered
      />
      <CardBody>
        {loading ? (
          <div className="space-y-3">
            {[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-10 rounded-lg" />)}
          </div>
        ) : !data || data.pipeline.length === 0 ? (
          <EmptyState
            icon={<Icon name="list" size={20} />}
            title="No leads yet"
            description="Once leads are created, you'll see their pipeline distribution here."
          />
        ) : (
          <PipelineFunnel pipeline={data.pipeline} />
        )}
      </CardBody>
    </Card>
  );
}

function PipelineFunnel({ pipeline }: { pipeline: DashboardStageBucket[] }) {
  const sorted = [...pipeline].sort((a, b) =>
    stageOrder.indexOf(a.stage) - stageOrder.indexOf(b.stage)
  );
  const total = sorted.reduce((s, b) => s + b.count, 0) || 1;
  const maxCount = Math.max(...sorted.map((b) => b.count)) || 1;

  return (
    <div className="space-y-2">
      {sorted.map((b) => {
        const pct = (b.count / total) * 100;
        const widthPct = (b.count / maxCount) * 100;
        const tone = stageTone[b.stage];
        return (
          <Link
            key={b.stage}
            to={`/leads?stage=${b.stage}`}
            className="group block rounded-lg p-3 hover:bg-ink-50/60 transition-colors -mx-2"
          >
            <div className="flex items-center gap-4">
              <div className="w-32 shrink-0">
                <Badge tone={tone} variant="soft" dot>{b.stage}</Badge>
              </div>
              <div className="flex-1 min-w-0">
                <div className="relative h-9 bg-ink-100/70 rounded-lg overflow-hidden">
                  <div
                    className={`absolute inset-y-0 left-0 rounded-lg bg-gradient-to-r ${stageBar[b.stage]} transition-all duration-500 group-hover:opacity-90`}
                    style={{ width: `${widthPct}%` }}
                  />
                  <div className="relative h-full flex items-center px-3 gap-2">
                    <span className="text-sm font-bold tabular-nums text-white drop-shadow-[0_1px_1px_rgba(0,0,0,0.25)]">
                      {b.count}
                    </span>
                    <span className="text-[11px] font-medium text-white/85 drop-shadow-[0_1px_1px_rgba(0,0,0,0.25)]">
                      {pct.toFixed(0)}% of pipeline
                    </span>
                  </div>
                </div>
              </div>
              <Icon name="chevronRight" size={14} className="text-ink-300 group-hover:text-brand-600 group-hover:translate-x-0.5 transition-all" />
            </div>
          </Link>
        );
      })}
    </div>
  );
}

// =============================================================================
// Floor card — agents on the floor, today's call stats
// =============================================================================

function FloorCard({ wall, loading }: { wall?: WallboardSnapshot; loading: boolean }) {
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title="Floor health"
        subtitle="Real-time call center status"
        action={
          <Link to="/wallboard">
            <Button variant="ghost" size="sm" rightIcon={<Icon name="arrowRight" size={14} />}>
              Wallboard
            </Button>
          </Link>
        }
        bordered
      />
      <CardBody className="space-y-4">
        {loading || !wall ? (
          <>
            <Skeleton className="h-16 rounded-lg" />
            <Skeleton className="h-16 rounded-lg" />
            <Skeleton className="h-16 rounded-lg" />
          </>
        ) : (
          <>
            <FloorRow icon="users" tone="brand" label="Clocked in" value={wall.agentsClockedIn} sub={`${wall.agentsAvailable} available`} />
            <FloorRow icon="phoneCall" tone="success" label="On call" value={wall.agentsOnCall} sub={`${wall.agentsOnBreak} on break`} />
            <FloorRow icon="inbox" tone="warning" label="Queue" value={wall.callsWaitingNow} sub={`Longest wait ${wall.longestWaitSeconds}s`} />
            <div className="pt-3 border-t hairline grid grid-cols-2 gap-3">
              <MiniStat label="Calls today"  value={wall.callsAnsweredToday} icon="phoneIn" />
              <MiniStat label="Sales closed" value={wall.salesClosedToday}   icon="briefcase" />
            </div>
          </>
        )}
      </CardBody>
    </Card>
  );
}

function FloorRow({
  icon, label, value, sub, tone,
}: {
  icon: IconName; label: string; value: number; sub: string;
  tone: "brand" | "success" | "warning";
}) {
  const tones: Record<string, string> = {
    brand: "bg-brand-50 text-brand-600 ring-brand-100",
    success: "bg-emerald-50 text-emerald-600 ring-emerald-100",
    warning: "bg-amber-50 text-amber-600 ring-amber-100",
  };
  return (
    <div className="flex items-center gap-3">
      <div className={`h-10 w-10 rounded-xl grid place-items-center ring-1 ring-inset ${tones[tone]}`}>
        <Icon name={icon} size={18} />
      </div>
      <div className="flex-1 min-w-0">
        <div className="text-xs text-ink-500">{label}</div>
        <div className="text-lg font-bold tabular-nums text-ink-900 leading-tight">
          {(value ?? 0).toLocaleString()}
        </div>
      </div>
      <div className="text-xs text-ink-500 text-right max-w-[7rem] tabular-nums">{sub}</div>
    </div>
  );
}

function MiniStat({ icon, label, value }: { icon: IconName; label: string; value: number }) {
  return (
    <div className="rounded-lg p-3 bg-ink-50/60 ring-1 ring-inset ring-ink-100">
      <div className="flex items-center gap-1.5 text-[10px] uppercase tracking-wider font-semibold text-ink-500 mb-1">
        <Icon name={icon} size={12} /> {label}
      </div>
      <div className="text-xl font-bold tabular-nums text-ink-900">{(value ?? 0).toLocaleString()}</div>
    </div>
  );
}

// =============================================================================
// Recent activity — feed with avatars
// =============================================================================

function ActivityCard({ data, loading }: { data?: DashboardSummary; loading: boolean }) {
  return (
    <Card className="xl:col-span-2 overflow-hidden">
      <CardHeader
        title="Recent activity"
        subtitle="Latest stage changes across the team"
        bordered
      />
      <CardBody className="pt-0">
        {loading ? (
          <ul className="space-y-3 pt-4">
            {[0, 1, 2, 3, 4].map((i) => (
              <li key={i} className="flex items-center gap-3">
                <Skeleton className="h-9 w-9 rounded-full" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-4 w-3/4" />
                  <Skeleton className="h-3 w-1/3" />
                </div>
              </li>
            ))}
          </ul>
        ) : !data || data.recentActivity.length === 0 ? (
          <div className="py-6">
            <EmptyState
              icon={<Icon name="inbox" size={20} />}
              title="No recent activity"
              description="Lead transitions and updates will show up here."
            />
          </div>
        ) : (
          <ul className="divide-y divide-ink-100/70">
            {data.recentActivity.map((a) => {
              const tone = stageTone[a.toStage];
              return (
                <li key={`${a.leadId}-${a.occurredAt}`}>
                  <Link
                    to={`/leads/${a.leadId}`}
                    className="flex items-center gap-3 py-3 px-1 -mx-1 rounded-lg hover:bg-ink-50/70 transition-colors"
                  >
                    <Avatar name={a.userName ?? a.leadName} size={36} />
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 flex-wrap min-w-0">
                        <span className="font-semibold text-ink-900 truncate">{a.leadName}</span>
                        <Badge tone={tone} variant="soft" dot className="shrink-0">{a.toStage}</Badge>
                      </div>
                      <div className="text-xs text-ink-500 mt-0.5 flex items-center gap-1.5 flex-wrap">
                        <span className="text-ink-600">{a.fromStage}</span>
                        <Icon name="arrowRight" size={11} className="text-ink-400" />
                        <span className="text-ink-700 font-medium">{a.toStage}</span>
                        <span className="text-ink-300">·</span>
                        <span className="whitespace-nowrap tabular-nums">{timeAgo(a.occurredAt)}</span>
                        {a.userName && (
                          <>
                            <span className="text-ink-300">·</span>
                            <span className="truncate">by {a.userName}</span>
                          </>
                        )}
                      </div>
                    </div>
                    <Icon name="chevronRight" size={14} className="text-ink-300 shrink-0" />
                  </Link>
                </li>
              );
            })}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

// =============================================================================
// Leaderboard — top performers today
// =============================================================================

function LeaderboardCard({ leaders, loading }: { leaders?: AgentLeaderboard[]; loading: boolean }) {
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title="Top performers"
        subtitle="Today's leaderboard"
        action={
          <Link to="/wallboard">
            <Button variant="ghost" size="sm" rightIcon={<Icon name="arrowRight" size={14} />}>
              All
            </Button>
          </Link>
        }
        bordered
      />
      <CardBody className="pt-0">
        {loading ? (
          <ul className="space-y-3 pt-4">
            {[0, 1, 2].map((i) => (
              <li key={i} className="flex items-center gap-3">
                <Skeleton className="h-9 w-9 rounded-full" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-4 w-2/3" />
                  <Skeleton className="h-3 w-1/3" />
                </div>
              </li>
            ))}
          </ul>
        ) : !leaders || leaders.length === 0 ? (
          <div className="py-6">
            <EmptyState
              icon={<Icon name="briefcase" size={20} />}
              title="No sales today"
              description="Be the first one on the board."
            />
          </div>
        ) : (
          <ul className="divide-y divide-ink-100/70">
            {leaders.slice(0, 5).map((u, i: number) => {
              const premium = Number(u.premiumToday ?? 0);
              return (
                <li key={u.userId} className="flex items-center gap-3 py-3">
                  <Medal rank={i + 1} />
                  <Avatar name={u.userName} size={32} />
                  <div className="flex-1 min-w-0">
                    <div className="font-semibold text-ink-900 truncate text-sm">{u.userName}</div>
                    <div className="text-[11px] text-ink-500 tabular-nums">
                      {u.salesToday ?? 0} sales · {formatMoney(premium)}
                    </div>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

function Medal({ rank }: { rank: number }) {
  const styles =
    rank === 1 ? "bg-gradient-to-br from-amber-300 to-amber-500 text-amber-950 shadow-[0_4px_12px_-4px_rgba(251,191,36,0.5)]" :
    rank === 2 ? "bg-gradient-to-br from-ink-200 to-ink-400 text-ink-800" :
    rank === 3 ? "bg-gradient-to-br from-orange-400 to-orange-600 text-orange-950" :
                 "bg-ink-100 text-ink-600";
  return (
    <div className={`h-7 w-7 rounded-full grid place-items-center font-bold text-[11px] shrink-0 ${styles}`}>
      {rank}
    </div>
  );
}

// =============================================================================
// Upcoming events — what's coming up (callbacks + birthdays + trainings)
// =============================================================================
// Backed by /api/dashboard/upcoming-events, which decides per-role what the caller may see:
// their own scheduled callbacks (everyone), plus staff birthdays & candidate trainings (HR/managers).
// This card just renders whatever the API returns, so the auth boundary stays on the server.

const eventStyle: Record<string, { icon: IconName; tile: string }> = {
  callback: { icon: "phone",     tile: "bg-brand-50 text-brand-600 ring-brand-100" },
  training: { icon: "userCheck", tile: "bg-accent-50 text-accent-600 ring-accent-100" },
  birthday: { icon: "sparkles",  tile: "bg-emerald-50 text-emerald-600 ring-emerald-100" },
  meeting:  { icon: "users",     tile: "bg-sky-50 text-sky-600 ring-sky-100" },
};
const fallbackEventStyle = { icon: "calendar" as IconName, tile: "bg-ink-100 text-ink-500 ring-ink-200" };

/** Whole calendar days (local) from today to the given instant; negative if already past. */
function daysFromToday(iso: string): number {
  const start = new Date(); start.setHours(0, 0, 0, 0);
  const target = new Date(iso); target.setHours(0, 0, 0, 0);
  return Math.round((target.getTime() - start.getTime()) / 86_400_000);
}
function whenBadge(iso: string): { label: string; tone: BadgeTone } {
  const d = daysFromToday(iso);
  if (d <= 0) return { label: "Today", tone: "warning" };
  if (d === 1) return { label: "Tomorrow", tone: "brand" };
  if (d <= 7) return { label: `In ${d} days`, tone: "neutral" };
  return { label: new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric" }), tone: "neutral" };
}

/** Small muted footer telling users how fresh a widget's data is. */
function RefreshNote({ children }: { children: ReactNode }) {
  return (
    <div className="mt-3 pt-2.5 border-t hairline flex items-center gap-1.5 text-[11px] text-ink-400">
      <Icon name="refresh" size={11} /> {children}
    </div>
  );
}

/** Details page an upcoming-event row opens when clicked, by type. */
function eventHref(type: string): string {
  switch (type) {
    case "training": return "/hr/interviews";
    case "birthday": return "/hr/employees";
    case "meeting": return "/calendar";
    case "callback": default: return "/callbacks";
  }
}

function UpcomingEventsCard() {
  // Poll so newly-scheduled callbacks/trainings surface without a refresh.
  const { data: events, isLoading } = useUpcomingEventsQuery(14, { pollingInterval: 60_000 });

  return (
    <Card className="mb-5 overflow-hidden">
      <CardHeader
        title={
          <span className="inline-flex items-center gap-1">
            Upcoming events
            <InfoHint title="Upcoming events" side="bottom">
              The next 14 days at a glance — your scheduled callbacks and meetings, plus staff birthdays
              and candidate trainings (for HR &amp; managers). Reminders for these are also sent automatically.
            </InfoHint>
          </span>
        }
        subtitle="Callbacks, meetings, birthdays and trainings coming up"
        action={
          <Link to="/callbacks">
            <Button variant="ghost" size="sm" rightIcon={<Icon name="arrowRight" size={14} />}>Callbacks</Button>
          </Link>
        }
        bordered
      />
      <CardBody>
        {isLoading ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-2">
            {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 rounded-lg" />)}
          </div>
        ) : !events || events.length === 0 ? (
          <EmptyState
            icon={<Icon name="calendar" size={20} />}
            title="Nothing scheduled"
            description="No callbacks, meetings, birthdays or trainings in the next 14 days."
          />
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-1">
            {events.map((e, i) => {
              const st = eventStyle[e.type] ?? fallbackEventStyle;
              const w = whenBadge(e.whenUtc);
              return (
                <Link key={i} to={eventHref(e.type)}
                  className="group flex items-center gap-3 py-2.5 -mx-2 px-2 rounded-lg hover:bg-ink-50/70 transition-colors">
                  <div className={`h-9 w-9 rounded-lg grid place-items-center ring-1 ring-inset shrink-0 ${st.tile}`}>
                    <Icon name={st.icon} size={16} />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium text-ink-900 truncate">{e.title}</div>
                    {e.subtitle && <div className="text-xs text-ink-500 truncate">{e.subtitle}</div>}
                  </div>
                  <Badge tone={w.tone} variant="soft" className="shrink-0 whitespace-nowrap tabular-nums">{w.label}</Badge>
                  <Icon name="chevronRight" size={14} className="text-ink-300 group-hover:text-brand-600 group-hover:translate-x-0.5 transition-all shrink-0" />
                </Link>
              );
            })}
          </div>
        )}
        <RefreshNote>Birthday &amp; training reminders are sent once a day; callbacks appear as they're scheduled.</RefreshNote>
      </CardBody>
    </Card>
  );
}

// =============================================================================
// Team status — attendance + live work state for the viewer's call centre
// =============================================================================
// Backed by /api/dashboard/team-status, which gates to HR + supervisory/management and scopes
// per role (call-centre-pinned → own centre; agency-level → whole agency). The card just renders
// what the API returns, so the auth boundary stays on the server. Two badges per person:
// attendance (today's HR mark) and live work state (from the wallboard/supervisor presence source).

const attendanceTone: Record<string, BadgeTone> = {
  Present: "success",
  Late: "warning",
  HalfDay: "warning",
  Absent: "danger",
  Ncns: "danger",
  Leave: "info",
};
const attendanceLabel: Record<string, string> = {
  Present: "Present", Absent: "Absent", Late: "Late",
  HalfDay: "Half day", Leave: "Leave", Ncns: "NCNS",
};

const liveMeta: Record<TeamLiveStatus, { tone: BadgeTone; label: string }> = {
  OnCall:     { tone: "brand",   label: "On call" },
  Available:  { tone: "success", label: "Available" },
  OnBreak:    { tone: "warning", label: "On break" },
  ClockedOut: { tone: "neutral", label: "Clocked out" },
};
const livePresence: Record<TeamLiveStatus, "online" | "busy" | "away" | "offline"> = {
  OnCall: "busy", Available: "online", OnBreak: "away", ClockedOut: "offline",
};

function TeamStatusCard() {
  // Refresh hourly so attendance/status stays current without hammering the endpoint.
  // 60s so the live work-state column (On call/Available/On break) tracks the floor, not an hour behind.
  const { data, isLoading, isError } = useTeamStatusQuery(undefined, { pollingInterval: 60_000 });

  // Group by call centre so a multi-centre viewer (agency-level) sees clear sections.
  const groups = useMemo(() => {
    const map = new Map<string, TeamStatusRow[]>();
    for (const r of data ?? []) {
      const key = r.callCenterName ?? "Unassigned";
      const arr = map.get(key);
      if (arr) arr.push(r);
      else map.set(key, [r]);
    }
    return Array.from(map, ([name, rows]) => ({ name, rows }));
  }, [data]);
  const multi = groups.length > 1;

  return (
    <Card className="mb-5 overflow-hidden">
      <CardHeader
        title={
          <span className="inline-flex items-center gap-1">
            Team status
            <InfoHint title="Team status" side="bottom">
              Everyone in your call centre today. The first badge is their <b>attendance</b>
              {" "}(Present, Late, Absent, Leave, Half day, NCNS — or Unmarked). The second is their
              {" "}<b>live work state</b> right now: On call, Available, On break or Clocked out.
            </InfoHint>
          </span>
        }
        subtitle="Attendance and live floor state, today"
        action={
          <Link to="/team">
            <Button variant="ghost" size="sm" rightIcon={<Icon name="arrowRight" size={14} />}>Team</Button>
          </Link>
        }
        bordered
      />
      <CardBody>
        {isLoading ? (
          <ul className="space-y-3">
            {[0, 1, 2, 3, 4].map((i) => (
              <li key={i} className="flex items-center gap-3">
                <Skeleton className="h-9 w-9 rounded-full" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-4 w-1/3" />
                  <Skeleton className="h-3 w-1/4" />
                </div>
                <Skeleton className="h-5 w-16 rounded-full" />
                <Skeleton className="h-5 w-20 rounded-full" />
              </li>
            ))}
          </ul>
        ) : isError || !data || data.length === 0 ? (
          <EmptyState
            icon={<Icon name="users" size={20} />}
            title="No team data"
            description="No employees to show for your call centre today."
          />
        ) : (
          <div className="space-y-5">
            {groups.map((g) => (
              <div key={g.name}>
                {multi && (
                  <div className="section-title mb-2 flex items-center gap-1.5">
                    <Icon name="building" size={13} /> {g.name}
                    <span className="text-ink-400 font-normal">· {g.rows.length}</span>
                  </div>
                )}
                <ul className="divide-y divide-ink-100/70">
                  {g.rows.map((r) => <TeamStatusRowItem key={r.employeeId} row={r} />)}
                </ul>
              </div>
            ))}
          </div>
        )}
        <RefreshNote>Updates every minute. Attendance comes from today's HR marks; live status reflects the floor in near real time.</RefreshNote>
      </CardBody>
    </Card>
  );
}

function TeamStatusRowItem({ row }: { row: TeamStatusRow }) {
  const live = liveMeta[row.liveStatus] ?? liveMeta.ClockedOut;
  const att = row.attendanceStatus
    ? {
        tone: attendanceTone[row.attendanceStatus] ?? "neutral",
        label: attendanceLabel[row.attendanceStatus] ?? row.attendanceStatus,
      }
    : { tone: "neutral" as BadgeTone, label: "Unmarked" };
  const meta = [row.agentCode, row.designation].filter(Boolean).join(" · ");
  return (
    <li>
      <Link to="/team" className="group flex items-center gap-3 py-2.5 px-1 -mx-1 rounded-lg hover:bg-ink-50/70 transition-colors">
        <Avatar name={row.fullName} size={36} presence={livePresence[row.liveStatus]} />
        <div className="min-w-0 flex-1">
          <div className="text-sm font-semibold text-ink-900 truncate">{row.fullName}</div>
          {meta && <div className="text-xs text-ink-500 truncate">{meta}</div>}
        </div>
        <div className="flex items-center gap-1.5 shrink-0 flex-wrap justify-end">
          <Badge tone={att.tone} variant="soft" dot>{att.label}</Badge>
          <Badge tone={live.tone} variant="soft" dot>{live.label}</Badge>
        </div>
        <Icon name="chevronRight" size={14} className="text-ink-300 group-hover:text-brand-600 group-hover:translate-x-0.5 transition-all shrink-0" />
      </Link>
    </li>
  );
}

// =============================================================================
// Quick actions
// =============================================================================

function QuickActions() {
  const items: { to: string; label: string; description: string; icon: IconName; tone: string }[] = [
    { to: "/leads",     label: "Leads",       description: "Manage your pipeline and transition leads.", icon: "list",      tone: "bg-brand-50 text-brand-600 ring-brand-100" },
    { to: "/agent",     label: "Agent Panel", description: "Take live calls and dispositions.",           icon: "phone",     tone: "bg-emerald-50 text-emerald-600 ring-emerald-100" },
    { to: "/callbacks", label: "Callbacks",   description: "Scheduled customer follow-ups.",              icon: "calendar",  tone: "bg-amber-50 text-amber-600 ring-amber-100" },
    { to: "/sales",     label: "Sales",       description: "Record, validate and fund deals.",            icon: "briefcase", tone: "bg-accent-50 text-accent-600 ring-accent-100" },
    { to: "/kpis",      label: "KPIs",        description: "Performance dashboards and metrics.",          icon: "chart",     tone: "bg-brand-50 text-brand-600 ring-brand-100" },
    { to: "/2fa",       label: "Enable 2FA",  description: "Add an extra layer of account security.",     icon: "shield",    tone: "bg-rose-50 text-rose-600 ring-rose-100" },
  ];
  return (
    <Card>
      <CardHeader title="Quick actions" subtitle="Jump to what matters most" bordered />
      <CardBody className="pt-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          {items.map((q) => (
            <Link
              key={q.to} to={q.to}
              className="group flex items-start gap-3 p-4 rounded-xl border hairline hover:border-brand-300 hover:shadow-card hover:bg-brand-50/30 transition-all"
            >
              <div className={`h-10 w-10 rounded-lg grid place-items-center ring-1 ring-inset ${q.tone}`}>
                <Icon name={q.icon} size={18} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-sm font-semibold text-ink-900 flex items-center gap-1.5">
                  {q.label}
                  <Icon name="arrowRight" size={13} className="opacity-0 -translate-x-1 group-hover:opacity-100 group-hover:translate-x-0 transition-all text-brand-600" />
                </div>
                <p className="text-xs text-ink-500 mt-0.5 leading-relaxed">{q.description}</p>
              </div>
            </Link>
          ))}
        </div>
      </CardBody>
    </Card>
  );
}
