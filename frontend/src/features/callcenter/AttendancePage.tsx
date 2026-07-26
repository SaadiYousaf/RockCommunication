import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { useAttendanceQuery, useAgencyOptionsQuery } from "../../shared/api/baseApi";
import type { AttendanceRow, AttendanceSession } from "../../shared/api/types";
import {
  Avatar, Badge, Card, CardBody, EmptyState, Icon, Input, PageHeader, Select, Skeleton, Stat, cn,
} from "../../shared/ui";
import type { BadgeTone } from "../../shared/ui";

const statusTone: Record<string, BadgeTone> = {
  Available: "success", OnCall: "brand", Break: "warning", Lunch: "warning",
  Training: "info", Meeting: "info", Offline: "neutral",
};

function hm(mins: number): string {
  const m = Math.max(0, Math.round(mins));
  const h = Math.floor(m / 60);
  return h > 0 ? `${h}h ${m % 60}m` : `${m}m`;
}
const dateStr = (offset = 0) => {
  const d = new Date(); d.setDate(d.getDate() + offset);
  return d.toISOString().slice(0, 10);
};
const dt = (s: string | null) => s ? new Date(s).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }) : "—";
const t = (s: string | null) => s ? new Date(s).toLocaleTimeString(undefined, { timeStyle: "short" }) : "—";

/**
 * Attendance — clock-in / break / status timesheets built from AgentSession + AgentStatusLog.
 * Management view: agency Admin, Call Center Admin, SuperAdmin. Rows expand to the per-session
 * status timeline (available / on-call / break / lunch).
 */
export function AttendancePage() {
  const isSuperAdmin = useSelector((s: RootState) => s.auth.user?.roles?.includes("SuperAdmin") ?? false);
  const { data: agencyOptions } = useAgencyOptionsQuery(undefined, { skip: !isSuperAdmin });
  const [agencyId, setAgencyId] = useState("");
  useEffect(() => {
    if (isSuperAdmin && !agencyId && agencyOptions && agencyOptions.length) setAgencyId(agencyOptions[0].id);
  }, [isSuperAdmin, agencyOptions, agencyId]);

  const [from, setFrom] = useState(() => dateStr(-7));
  const [to, setTo] = useState(() => dateStr(1));
  const [range, setRange] = useState<number | null>(7);
  const [sort, setSort] = useState("active");
  function applyRange(days: number) { setFrom(dateStr(-days)); setTo(dateStr(1)); setRange(days); }

  const waitingForAgency = isSuperAdmin && !agencyId;
  const { data, isLoading, isFetching } = useAttendanceQuery(
    { from: new Date(from).toISOString(), to: new Date(to).toISOString(), agencyId: isSuperAdmin ? agencyId : undefined },
    { skip: waitingForAgency },
  );

  const sorted = useMemo(() => {
    const rows = [...(data ?? [])];
    switch (sort) {
      case "name": return rows.sort((a, b) => a.userName.localeCompare(b.userName));
      case "clocked": return rows.sort((a, b) => b.totalClockedMinutes - a.totalClockedMinutes);
      case "break": return rows.sort((a, b) => b.totalBreakMinutes - a.totalBreakMinutes);
      case "oncall": return rows.sort((a, b) => b.totalOnCallMinutes - a.totalOnCallMinutes);
      // "active" — clocked-in first, then name (the server default)
      default: return rows;
    }
  }, [data, sort]);

  const totals = useMemo(() => {
    const rows = data ?? [];
    return {
      people: rows.length,
      clockedIn: rows.filter(r => r.clockedIn).length,
      sessions: rows.reduce((s, r) => s + r.sessionCount, 0),
      hours: rows.reduce((s, r) => s + r.totalClockedMinutes, 0),
    };
  }, [data]);

  return (
    <>
      <PageHeader
        eyebrow="Operations"
        title="Attendance"
        description="Clock-in, breaks and status time for your team. Expand a person to see their session timeline."
        actions={isSuperAdmin ? (
          <Select aria-label="Agency" value={agencyId} onChange={(e) => setAgencyId(e.target.value)} className="w-48">
            {(!agencyOptions || agencyOptions.length === 0) && <option value="">No agencies</option>}
            {(agencyOptions ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </Select>
        ) : undefined}
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-5">
        <Stat label="People" value={totals.people} icon={<Icon name="users" size={16} />} tone="brand" />
        <Stat label="Clocked in now" value={totals.clockedIn} icon={<Icon name="phone" size={16} />} tone="success" />
        <Stat label="Sessions" value={totals.sessions} icon={<Icon name="clock" size={16} />} tone="accent" />
        <Stat label="Total clocked" value={hm(totals.hours)} icon={<Icon name="activity" size={16} />} tone="warning" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex flex-wrap items-end gap-4">
          <div className="flex gap-1.5">
            {[{ l: "Today", d: 0 }, { l: "7 days", d: 7 }, { l: "30 days", d: 30 }].map(p => (
              <button key={p.l} onClick={() => applyRange(p.d)}
                className={cn("px-3 py-1.5 rounded-lg text-xs font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-1",
                  range === p.d ? "bg-brand-600 text-white shadow-sm" : "bg-ink-100 hover:bg-ink-200 text-ink-700")}>
                {p.l}
              </button>
            ))}
          </div>
          <div className="h-9 w-px bg-ink-200 hidden sm:block" />
          <Input label="From" type="date" value={from} onChange={(e) => { setFrom(e.target.value); setRange(null); }} containerClassName="w-40" />
          <Input label="To" type="date" value={to} onChange={(e) => { setTo(e.target.value); setRange(null); }} containerClassName="w-40" />
          <Select label="Sort by" value={sort} onChange={(e) => setSort(e.target.value)} containerClassName="w-44">
            <option value="active">Active first</option>
            <option value="name">Name (A–Z)</option>
            <option value="clocked">Most clocked</option>
            <option value="oncall">Most on-call</option>
            <option value="break">Most break</option>
          </Select>
          {isFetching && <span className="text-xs text-ink-500 pb-2">Updating…</span>}
        </CardBody>
      </Card>

      {waitingForAgency ? (
        <Notice icon="building" title="Pick an agency"
          body={!agencyOptions?.length ? "No agencies exist yet." : "Choose an agency above to view its attendance."} />
      ) : isLoading ? (
        <Card><CardBody className="space-y-2">{[0, 1, 2, 3].map(i => <Skeleton key={i} className="h-14" />)}</CardBody></Card>
      ) : !data || data.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="clock" size={20} />} title="No attendance in this range"
            description="Nobody clocked in during the selected dates. Try a wider range." />
        </CardBody></Card>
      ) : (
        <div className="space-y-2.5">
          {sorted.map((r) => <PersonRow key={r.userId} row={r} />)}
        </div>
      )}
    </>
  );
}

function PersonRow({ row }: { row: AttendanceRow }) {
  const [open, setOpen] = useState(false);
  return (
    <Card>
      <button type="button" onClick={() => setOpen(o => !o)}
        className="w-full flex items-center gap-3 px-4 py-3 text-left rounded-lg hover:bg-brand-50/30 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-inset">
        <Avatar name={row.userName} size={36} />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="font-semibold text-ink-900 truncate">{row.userName}</span>
            <Badge tone={statusTone[row.currentStatus] ?? "neutral"} variant="soft" dot>
              {row.clockedIn ? row.currentStatus : "Offline"}
            </Badge>
          </div>
          <div className="text-xs text-ink-500 tabular-nums truncate">
            {row.sessionCount} session{row.sessionCount === 1 ? "" : "s"} · first in {dt(row.firstClockIn)}
          </div>
        </div>
        <div className="hidden sm:flex items-center gap-5 text-sm shrink-0">
          <Metric label="Clocked" value={hm(row.totalClockedMinutes)} />
          <Metric label="Available" value={hm(row.totalAvailableMinutes)} tone="text-success-700" />
          <Metric label="On call" value={hm(row.totalOnCallMinutes)} tone="text-brand-700" />
          <Metric label="Break" value={hm(row.totalBreakMinutes)} tone="text-amber-700" />
        </div>
        <Icon name={open ? "chevronUp" : "chevronDown"} size={16} className="text-ink-400 shrink-0" />
      </button>

      {open && (
        <div className="border-t hairline px-4 py-3 space-y-3 bg-ink-50/30">
          {row.sessions.map((s) => <SessionBlock key={s.id} session={s} />)}
        </div>
      )}
    </Card>
  );
}

function Metric({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="text-right">
      <div className={`font-semibold tabular-nums whitespace-nowrap ${tone ?? "text-ink-900"}`}>{value}</div>
      <div className="text-[10px] uppercase tracking-wide text-ink-400">{label}</div>
    </div>
  );
}

function SessionBlock({ session }: { session: AttendanceSession }) {
  return (
    <div className="rounded-lg border hairline bg-white p-3">
      <div className="flex items-center justify-between flex-wrap gap-2 mb-2">
        <div className="text-sm font-medium text-ink-800 inline-flex items-center gap-1.5 tabular-nums whitespace-nowrap">
          <Icon name="clock" size={13} className="text-ink-400" />
          {t(session.clockInAt)} → {session.clockOutAt ? t(session.clockOutAt) : <span className="text-success-700">active</span>}
        </div>
        <div className="text-xs text-ink-500 tabular-nums">
          {hm(session.clockedMinutes)} clocked · {hm(session.availableMinutes)} avail · {hm(session.onCallMinutes)} call · {hm(session.breakMinutes)} break
        </div>
      </div>
      <div className="flex flex-col gap-1">
        {session.segments.map((seg, i) => (
          <div key={i} className="flex items-center gap-2 text-xs">
            <Badge tone={statusTone[seg.status] ?? "neutral"} variant="soft" size="sm">{seg.status}</Badge>
            <span className="text-ink-500 tabular-nums whitespace-nowrap">{t(seg.fromAt)} – {seg.untilAt ? t(seg.untilAt) : "now"}</span>
            <span className="text-ink-400 tabular-nums">{hm(seg.minutes)}</span>
            {seg.reason && <span className="text-ink-500 italic truncate">· {seg.reason}</span>}
          </div>
        ))}
        {session.segments.length === 0 && <span className="text-xs text-ink-400">No status changes recorded.</span>}
      </div>
    </div>
  );
}

function Notice({ icon, title, body }: { icon: string; title: string; body: string }) {
  return (
    <Card><CardBody className="py-12">
      <div className="text-center max-w-md mx-auto">
        <div className="mx-auto mb-4 h-14 w-14 rounded-2xl bg-ink-100 grid place-items-center text-ink-400"><Icon name={icon} size={24} /></div>
        <h3 className="text-base font-semibold text-ink-900 mb-1">{title}</h3>
        <p className="text-sm text-ink-500">{body}</p>
      </div>
    </CardBody></Card>
  );
}
