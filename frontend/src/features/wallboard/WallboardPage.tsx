import { useEffect, useMemo, useState } from "react";
import { useLeaderboardQuery, useWallboardQuery } from "../../shared/api/baseApi";
import { Spinner, type IconName, Icon } from "../../shared/ui";

function ClockNow() {
  const [now, setNow] = useState(new Date());
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(id);
  }, []);
  return (
    <div className="text-right">
      <div className="text-4xl lg:text-5xl font-bold tracking-tight font-mono text-white tabular-nums leading-none">
        {now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
        <span className="text-2xl lg:text-3xl text-white/50 ml-1">
          :{now.getSeconds().toString().padStart(2, "0")}
        </span>
      </div>
      <div className="text-xs text-white/60 uppercase tracking-[0.2em] mt-2">
        {now.toLocaleDateString([], { weekday: "long", month: "short", day: "numeric" })}
      </div>
    </div>
  );
}

function LivePulse() {
  return (
    <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-emerald-500/10 ring-1 ring-emerald-400/30">
      <span className="relative flex h-2 w-2">
        <span className="absolute inset-0 rounded-full bg-emerald-400 animate-ping opacity-75" />
        <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-400" />
      </span>
      <span className="text-[10px] font-semibold uppercase tracking-[0.2em] text-emerald-300">Live</span>
    </div>
  );
}

export function WallboardPage() {
  const { data: w, isLoading } = useWallboardQuery(undefined, { pollingInterval: 5_000 });
  const { data: leaders } = useLeaderboardQuery("today", { pollingInterval: 30_000 });

  const answerRate = useMemo(() => {
    if (!w) return 0;
    const total = (w.callsAnsweredToday ?? 0) + (w.callsAbandonedToday ?? 0);
    return total === 0 ? 100 : Math.round((w.callsAnsweredToday / total) * 100);
  }, [w]);

  const utilization = useMemo(() => {
    if (!w || !w.agentsClockedIn) return 0;
    return Math.round((w.agentsOnCall / w.agentsClockedIn) * 100);
  }, [w]);

  const topPremium = useMemo(() => {
    if (!leaders?.length) return 0;
    return Math.max(...leaders.map((u) => Number(u.premiumToday ?? 0)));
  }, [leaders]);

  return (
    <div
      className="-m-6 lg:-m-8 xl:-m-10 2xl:-m-12 min-h-[calc(100vh-4rem)] text-white relative overflow-hidden"
      style={{
        background:
          "radial-gradient(ellipse at top, rgba(31,126,255,0.18), transparent 55%), radial-gradient(ellipse at bottom right, rgba(168,85,247,0.12), transparent 50%), linear-gradient(to bottom right, #05080f, #0a0f1c, #0d1424)",
      }}
    >
      <div
        className="pointer-events-none absolute inset-0"
        style={{
          backgroundImage:
            "linear-gradient(rgba(255,255,255,0.02) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.02) 1px, transparent 1px)",
          backgroundSize: "48px 48px",
        }}
      />

      <div className="relative max-w-[1800px] mx-auto p-8 lg:p-10">
        {/* Header */}
        <div className="flex items-start justify-between mb-10 pb-6 border-b border-white/5">
          <div className="flex items-center gap-5">
            <div className="h-14 w-14 rounded-2xl bg-gradient-to-br from-brand-500 to-accent-600 grid place-items-center shadow-[0_0_40px_rgba(31,126,255,0.4)]">
              <Icon name="chart" size={26} />
            </div>
            <div>
              <div className="flex items-center gap-3 mb-1">
                <h1 className="text-3xl lg:text-4xl font-bold tracking-tight text-white">Floor Wallboard</h1>
                <LivePulse />
              </div>
              <p className="text-sm text-white/50">Real-time floor performance · refreshes every 5 seconds</p>
            </div>
          </div>
          <ClockNow />
        </div>

        {isLoading || !w ? (
          <div className="grid place-items-center py-20"><Spinner size={32} /></div>
        ) : (
          <>
            {/* Hero KPI strip */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 mb-8">
              <HeroTile
                label="Answer Rate"
                value={`${answerRate}%`}
                sublabel={`${w.callsAnsweredToday} answered · ${w.callsAbandonedToday} abandoned`}
                tone="emerald"
                progress={answerRate}
              />
              <HeroTile
                label="Agent Utilization"
                value={`${utilization}%`}
                sublabel={`${w.agentsOnCall} of ${w.agentsClockedIn} agents on call`}
                tone="brand"
                progress={utilization}
              />
              <HeroTile
                label="Sales Closed Today"
                value={String(w.salesClosedToday)}
                sublabel={`${w.leadsCreatedToday} leads created`}
                tone="amber"
              />
            </div>

            {/* Agents */}
            <SectionHeading label="Agents" right={`${w.agentsClockedIn} on the floor`} />
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-10">
              <BigTile label="Clocked In"  value={w.agentsClockedIn} icon="users"   accent="brand"   total={w.agentsClockedIn} />
              <BigTile label="Available"   value={w.agentsAvailable} icon="check"   accent="emerald" total={w.agentsClockedIn} />
              <BigTile label="On Call"     value={w.agentsOnCall}    icon="phone"   accent="amber"   total={w.agentsClockedIn} />
              <BigTile label="On Break"    value={w.agentsOnBreak}   icon="clock"   accent="ink"     total={w.agentsClockedIn} />
            </div>

            {/* Today */}
            <SectionHeading label="Today" />
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-10">
              <BigTile label="Calls Answered"   value={w.callsAnsweredToday}  icon="phone"     accent="emerald" />
              <BigTile label="Calls Abandoned"  value={w.callsAbandonedToday} icon="x"         accent="rose" />
              <BigTile label="Leads Created"    value={w.leadsCreatedToday}   icon="list"      accent="brand" />
              <BigTile label="Sales Closed"     value={w.salesClosedToday}    icon="briefcase" accent="emerald" />
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-10">
              {/* Queue */}
              <div className="lg:col-span-1">
                <SectionHeading label="Queue" />
                <div className="grid grid-cols-2 gap-4">
                  <BigTile label="Waiting"          value={w.callsWaitingNow}    icon="inbox" accent="amber" />
                  <BigTile label="Longest Wait"     value={`${w.longestWaitSeconds}s`} icon="clock" accent="rose" />
                </div>
              </div>

              {/* Leaderboard */}
              <div className="lg:col-span-2">
                <SectionHeading label="Top Performers — Today" />
                <div className="bg-white/[0.03] rounded-2xl backdrop-blur ring-1 ring-white/10 overflow-hidden">
                  <div className="grid grid-cols-12 px-6 py-3 text-white/40 text-[10px] uppercase tracking-[0.2em] border-b border-white/5">
                    <div className="col-span-1">#</div>
                    <div className="col-span-5">Agent</div>
                    <div className="col-span-2 text-right">Sales</div>
                    <div className="col-span-4 text-right">Premium</div>
                  </div>
                  <div className="divide-y divide-white/5">
                    {leaders?.map((u, i: number) => {
                      const premium = Number(u.premiumToday ?? 0);
                      const pct = topPremium > 0 ? (premium / topPremium) * 100 : 0;
                      return (
                        <div
                          key={u.userId}
                          className="relative grid grid-cols-12 items-center px-6 py-4 hover:bg-white/[0.02] transition-colors"
                        >
                          <div
                            className="absolute inset-y-0 left-0 bg-gradient-to-r from-brand-500/10 to-transparent pointer-events-none"
                            style={{ width: `${pct}%` }}
                          />
                          <div className="col-span-1 relative"><Medal rank={i + 1} /></div>
                          <div className="col-span-5 relative">
                            <div className="font-semibold text-white text-base">{u.userName}</div>
                            {u.callsToday > 0 && (
                              <div className="text-xs text-white/40">{u.callsToday} calls</div>
                            )}
                          </div>
                          <div className="col-span-2 text-right relative">
                            <span className="text-2xl font-bold text-emerald-300 font-mono tabular-nums">{u.salesToday ?? 0}</span>
                          </div>
                          <div className="col-span-4 text-right relative">
                            <span className="text-xl font-mono text-brand-300 tabular-nums">${premium.toLocaleString()}</span>
                          </div>
                        </div>
                      );
                    })}
                    {(!leaders || leaders.length === 0) && (
                      <div className="px-6 py-16 text-center text-white/40">
                        <Icon name="briefcase" size={32} className="mx-auto mb-2 opacity-50" />
                        <div className="text-sm">No sales yet today.</div>
                        <div className="text-xs mt-1 text-white/30">First closer gets the crown.</div>
                      </div>
                    )}
                  </div>
                </div>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function SectionHeading({ label, right }: { label: string; right?: string }) {
  return (
    <div className="flex items-center gap-3 mb-4">
      <h2 className="text-[11px] font-bold tracking-[0.3em] uppercase text-white/50">{label}</h2>
      <div className="flex-1 h-px bg-gradient-to-r from-white/15 to-transparent" />
      {right && <span className="text-[11px] text-white/40 uppercase tracking-wider">{right}</span>}
    </div>
  );
}

const accentClasses: Record<string, { glow: string; text: string; ring: string; bar: string }> = {
  brand:   { glow: "from-brand-400 to-brand-600",     text: "from-brand-300 to-brand-500",       ring: "ring-brand-400/20",   bar: "bg-brand-400" },
  emerald: { glow: "from-emerald-400 to-emerald-600", text: "from-emerald-300 to-emerald-500",   ring: "ring-emerald-400/20", bar: "bg-emerald-400" },
  amber:   { glow: "from-amber-400 to-amber-600",     text: "from-amber-300 to-amber-500",       ring: "ring-amber-400/20",   bar: "bg-amber-400" },
  rose:    { glow: "from-rose-400 to-rose-600",       text: "from-rose-300 to-rose-500",         ring: "ring-rose-400/20",    bar: "bg-rose-400" },
  ink:     { glow: "from-ink-400 to-ink-600",         text: "from-ink-200 to-ink-400",           ring: "ring-white/10",       bar: "bg-ink-400" },
};

function BigTile({
  label, value, icon, accent, total,
}: {
  label: string; value: number | string; icon: IconName; accent: keyof typeof accentClasses; total?: number;
}) {
  const a = accentClasses[accent];
  const numericValue = typeof value === "number" ? value : null;
  const pct = total && numericValue != null && total > 0 ? Math.round((numericValue / total) * 100) : null;
  return (
    <div className={`relative overflow-hidden rounded-2xl bg-white/[0.03] ring-1 ${a.ring} backdrop-blur p-5 group hover:bg-white/[0.05] transition-all`}>
      <div className={`absolute -top-16 -right-16 w-40 h-40 rounded-full bg-gradient-to-br ${a.glow} opacity-20 blur-3xl group-hover:opacity-30 transition-opacity`} />
      <div className="relative flex items-start justify-between mb-3">
        <div className="text-[10px] font-semibold tracking-[0.2em] uppercase text-white/60">{label}</div>
        <div className={`h-8 w-8 rounded-lg bg-gradient-to-br ${a.glow} bg-opacity-20 grid place-items-center text-white/90 shadow-[0_0_16px_rgba(255,255,255,0.05)]`}>
          <Icon name={icon} size={14} />
        </div>
      </div>
      <div className={`text-5xl lg:text-6xl font-bold tracking-tight font-mono bg-gradient-to-br ${a.text} bg-clip-text text-transparent tabular-nums leading-none`}>
        {typeof value === "number" ? value.toLocaleString() : value}
      </div>
      {pct != null && (
        <div className="relative mt-4">
          <div className="h-1 rounded-full bg-white/5 overflow-hidden">
            <div
              className={`h-full ${a.bar} transition-all duration-700`}
              style={{ width: `${Math.min(100, pct)}%` }}
            />
          </div>
          <div className="text-[10px] text-white/40 mt-1.5 font-mono">{pct}% of clocked-in</div>
        </div>
      )}
    </div>
  );
}

function HeroTile({
  label, value, sublabel, tone, progress,
}: {
  label: string; value: string; sublabel: string; tone: "brand" | "emerald" | "amber"; progress?: number;
}) {
  const a = accentClasses[tone];
  return (
    <div className={`relative overflow-hidden rounded-2xl bg-gradient-to-br from-white/[0.05] to-white/[0.02] ring-1 ${a.ring} backdrop-blur p-6`}>
      <div className={`absolute -top-20 -right-20 w-56 h-56 rounded-full bg-gradient-to-br ${a.glow} opacity-25 blur-3xl`} />
      <div className="relative">
        <div className="text-[10px] font-semibold tracking-[0.25em] uppercase text-white/60 mb-2">{label}</div>
        <div className={`text-6xl font-bold tracking-tight font-mono bg-gradient-to-br ${a.text} bg-clip-text text-transparent tabular-nums leading-none`}>
          {value}
        </div>
        <div className="text-xs text-white/50 mt-3">{sublabel}</div>
        {progress != null && (
          <div className="mt-4 h-1.5 rounded-full bg-white/5 overflow-hidden">
            <div
              className={`h-full ${a.bar} transition-all duration-700`}
              style={{ width: `${Math.min(100, progress)}%` }}
            />
          </div>
        )}
      </div>
    </div>
  );
}

function Medal({ rank }: { rank: number }) {
  const styles =
    rank === 1 ? "bg-gradient-to-br from-amber-300 to-amber-500 text-amber-950 shadow-[0_0_20px_rgba(251,191,36,0.5)]" :
    rank === 2 ? "bg-gradient-to-br from-ink-200 to-ink-400 text-ink-800" :
    rank === 3 ? "bg-gradient-to-br from-orange-400 to-orange-600 text-orange-950" :
                 "bg-white/10 text-white/70";
  return (
    <div className={`h-10 w-10 rounded-full grid place-items-center font-bold text-lg ${styles}`}>
      {rank}
    </div>
  );
}
