import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import { Link } from "react-router-dom";
import type { RootState } from "../app/store";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Skeleton,
  type IconName,
} from "../shared/ui";
import { useDashboardSummaryQuery, useLeaderboardQuery, useWallboardQuery } from "../shared/api/baseApi";
import type { DashboardStageBucket, DashboardSummary, WorkflowStage } from "../shared/api/types";

const stageOrder: WorkflowStage[] = [
  "New", "Fronted", "Verified", "JrClosed", "Closed", "Validated", "Funded", "Followup", "Winback", "Lost",
];

const stageTone: Record<WorkflowStage, "brand" | "info" | "warning" | "neutral" | "success" | "danger"> = {
  New: "brand", Fronted: "info", Verified: "info", JrClosed: "warning",
  Closed: "warning", Validated: "success", Funded: "success",
  Followup: "neutral", Winback: "neutral", Lost: "danger",
};

const stageBar: Record<WorkflowStage, string> = {
  New:       "from-brand-400 to-brand-600",
  Fronted:   "from-sky-400 to-sky-600",
  Verified:  "from-cyan-400 to-cyan-600",
  JrClosed:  "from-amber-400 to-amber-600",
  Closed:    "from-orange-400 to-orange-600",
  Validated: "from-emerald-400 to-emerald-600",
  Funded:    "from-emerald-500 to-teal-600",
  Followup:  "from-violet-400 to-violet-600",
  Winback:   "from-fuchsia-400 to-fuchsia-600",
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

export function Dashboard() {
  const auth = useSelector((s: RootState) => s.auth);
  const userName = auth.user?.userName ?? "there";
  const role = auth.user?.roles?.[0] ?? null;

  const { data, isLoading, isError, refetch } = useDashboardSummaryQuery();
  const { data: leaders } = useLeaderboardQuery("today", { pollingInterval: 60_000 });
  const { data: wall } = useWallboardQuery(undefined, { pollingInterval: 30_000 });

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

      {/* Main grid: pipeline funnel (2/3) + side rail (1/3) */}
      <div className="grid grid-cols-1 xl:grid-cols-3 gap-5 mb-5">
        <PipelineCard data={data} loading={isLoading} />
        <FloorCard wall={wall} loading={isLoading} />
      </div>

      {/* Lower grid: activity feed + leaderboard */}
      <div className="grid grid-cols-1 xl:grid-cols-3 gap-5 mb-5">
        <ActivityCard data={data} loading={isLoading} />
        <LeaderboardCard leaders={leaders} loading={isLoading} />
      </div>

      <QuickActions />
    </>
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
          "radial-gradient(ellipse at top right, rgba(14,165,233,0.20), transparent 55%), " +
          "radial-gradient(ellipse at bottom left, rgba(139,92,246,0.10), transparent 55%), " +
          "linear-gradient(135deg, #f0f9ff 0%, #ffffff 55%, #f5f3ff 100%)",
      }}
    >
      {/* Light dot grid for tactile surface */}
      <div
        className="pointer-events-none absolute inset-0 opacity-50"
        style={{
          backgroundImage: "radial-gradient(rgba(14,165,233,0.10) 1px, transparent 1px)",
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
            <h1 className="text-3xl md:text-4xl font-bold tracking-tight text-ink-900">
              Welcome back, {userName}
            </h1>
            <p className="text-ink-500 text-sm mt-1.5">
              {role ? `${role} · ` : ""}{dateStr} · {timeStr}
            </p>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <button
              onClick={onRefresh}
              className="inline-flex items-center gap-2 px-3.5 h-10 rounded-xl bg-white hover:bg-ink-50 ring-1 ring-ink-200 text-sm font-medium text-ink-700 shadow-xs transition-colors"
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
              <div key={i} className="h-24 rounded-xl bg-white/60 ring-1 ring-ink-200/70" />
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
    info:    "bg-cyan-50 text-cyan-600 ring-cyan-100",
  })[tone];
}

// =============================================================================
// KPI strip — 4 metrics with deltas + sparklines
// =============================================================================

function KpiStrip({ data, loading }: { data?: DashboardSummary; loading: boolean }) {
  if (loading || !data) {
    return (
      <div className="grid grid-cols-2 xl:grid-cols-4 gap-4 mb-5">
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
    <div className="grid grid-cols-2 xl:grid-cols-4 gap-4 mb-5">
      <KpiTile
        to="/leads" label="Active Leads" value={data.activeLeads.toLocaleString()}
        delta={leadDelta.delta} trend={leadDelta.trend} icon="list" tone="brand" sparkline={sparkLead}
      />
      <KpiTile
        to="/sales" label="Sales This Week" value={formatMoney(Number(data.salesThisWeek))}
        delta={salesDelta.delta} trend={salesDelta.trend} icon="briefcase" tone="success" sparkline={sparkSales}
      />
      <KpiTile
        to="/kpis" label="Conversion" value={`${data.conversionRate}%`}
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
  brand:   { iconBg: "bg-brand-50",   iconText: "text-brand-600",   spark: "#6366f1", ring: "ring-brand-100" },
  success: { iconBg: "bg-emerald-50", iconText: "text-emerald-600", spark: "#10b981", ring: "ring-emerald-100" },
  accent:  { iconBg: "bg-violet-50",  iconText: "text-violet-600",  spark: "#8b5cf6", ring: "ring-violet-100" },
  warning: { iconBg: "bg-amber-50",   iconText: "text-amber-600",   spark: "#f59e0b", ring: "ring-amber-100" },
};

function KpiTile({
  to, label, value, delta, trend, icon, tone, sparkline,
}: {
  to: string; label: string; value: string; delta: string;
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
        title="Pipeline overview"
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

function FloorCard({ wall, loading }: { wall: any; loading: boolean }) {
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
      <div className="text-xs text-ink-500 text-right max-w-[7rem]">{sub}</div>
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
                        <span>{timeAgo(a.occurredAt)}</span>
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

function LeaderboardCard({ leaders, loading }: { leaders: any; loading: boolean }) {
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
            {leaders.slice(0, 5).map((u: any, i: number) => {
              const premium = Number(u.premiumToday ?? 0);
              return (
                <li key={u.userId} className="flex items-center gap-3 py-3">
                  <Medal rank={i + 1} />
                  <Avatar name={u.userName} size={32} />
                  <div className="flex-1 min-w-0">
                    <div className="font-semibold text-ink-900 truncate text-sm">{u.userName}</div>
                    <div className="text-[11px] text-ink-500">
                      {u.salesToday ?? u.sales ?? 0} sales · {formatMoney(premium)}
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
// Quick actions
// =============================================================================

function QuickActions() {
  const items: { to: string; label: string; description: string; icon: IconName; tone: string }[] = [
    { to: "/leads",     label: "Leads",       description: "Manage your pipeline and transition leads.", icon: "list",      tone: "bg-brand-50 text-brand-600 ring-brand-100" },
    { to: "/agent",     label: "Agent Panel", description: "Take live calls and dispositions.",           icon: "phone",     tone: "bg-emerald-50 text-emerald-600 ring-emerald-100" },
    { to: "/callbacks", label: "Callbacks",   description: "Scheduled customer follow-ups.",              icon: "calendar",  tone: "bg-amber-50 text-amber-600 ring-amber-100" },
    { to: "/sales",     label: "Sales",       description: "Record, validate and fund deals.",            icon: "briefcase", tone: "bg-violet-50 text-violet-600 ring-violet-100" },
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
