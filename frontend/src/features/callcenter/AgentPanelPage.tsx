import { useEffect, useMemo, useState } from "react";
import {
  useClockInMutation, useClockOutMutation, useListWrapUpCodesQuery,
  useMyRecentCallsQuery, useMySessionQuery, useSetAgentStatusMutation, useWrapUpCallMutation,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, PageHeader,
  Select, Skeleton, Table, TBody, TD, TH, THead, TR, useToast, type IconName,
} from "../../shared/ui";

type AgentStatus = "Available" | "OnCall" | "Break" | "Lunch" | "Training" | "Meeting" | "Offline";

const STATUS_OPTIONS: { key: Exclude<AgentStatus, "OnCall" | "Offline">; icon: IconName }[] = [
  { key: "Available", icon: "check" },
  { key: "Break",     icon: "clock" },
  { key: "Lunch",     icon: "calendar" },
  { key: "Training",  icon: "star" },
  { key: "Meeting",   icon: "users" },
];

const statusTone: Record<string, "success" | "info" | "warning" | "neutral" | "danger"> = {
  Available: "success",
  OnCall:    "info",
  Break:     "warning",
  Lunch:     "warning",
  Training:  "neutral",
  Meeting:   "neutral",
  Offline:   "danger",
};

function formatDuration(ms: number) {
  const sec = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(sec / 3600);
  const m = Math.floor((sec % 3600) / 60);
  const s = sec % 60;
  return `${h.toString().padStart(2, "0")}:${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
}

export function AgentPanelPage() {
  const { data: rawSession, error: sessionError, refetch: refetchSession, isLoading: sessionLoading } = useMySessionQuery(undefined, {
    pollingInterval: 30_000,
    refetchOnMountOrArgChange: true,
    refetchOnFocus: true,
    refetchOnReconnect: true,
  });
  // 404 / null / any error from the session endpoint => "not clocked in".
  // RTK Query keeps the previous successful response on error, so we explicitly
  // null it out when the latest fetch failed to avoid showing a phantom shift.
  const session = sessionError ? null : rawSession;
  const { data: recentCalls, isLoading: callsLoading } = useMyRecentCallsQuery(20);
  const { data: codes } = useListWrapUpCodesQuery();
  const [clockIn, { isLoading: clockingIn }] = useClockInMutation();
  const [clockOut, { isLoading: clockingOut }] = useClockOutMutation();
  const [setStatus, { isLoading: changingStatus }] = useSetAgentStatusMutation();
  const [wrapUp, { isLoading: wrappingUp }] = useWrapUpCallMutation();
  const toast = useToast();

  const [reason, setReason] = useState("");
  const [, setNow] = useState(Date.now()); // tick for live timer

  useEffect(() => {
    if (!session) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [session]);

  const unwrapped = recentCalls?.find((c) => c.endedAt && !c.wrapUpCode);
  const currentStatus: AgentStatus = (session?.currentStatus as AgentStatus) ?? "Offline";

  const stats = useMemo(() => {
    const calls = recentCalls ?? [];
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const todays = calls.filter((c) => new Date(c.initiatedAt) >= today);
    const sales = todays.filter((c) => c.wrapUpCode && codes?.find((cc) => cc.code === c.wrapUpCode && cc.isSale));
    return {
      total: todays.length,
      sales: sales.length,
      pending: calls.filter((c) => c.endedAt && !c.wrapUpCode).length,
    };
  }, [recentCalls, codes]);

  async function changeStatus(status: string) {
    try {
      await setStatus({ status, reason: reason || undefined }).unwrap();
      toast.success("Status updated", `You're now ${status}.`);
      setReason("");
    } catch (err: any) {
      const detail: string = err?.data?.detail ?? "Try again.";
      // Self-heal: if backend says we're not clocked in, our cached session is stale.
      // Refetch and let the UI render the empty state with a fresh Clock-in CTA.
      if (/not clocked in/i.test(detail)) {
        await refetchSession();
        toast.warning("Session expired", "Your shift was ended remotely. Please clock in again.");
      } else {
        toast.error("Couldn't update status", detail);
      }
    }
  }

  async function doClockIn() {
    try { await clockIn().unwrap(); await refetchSession(); toast.success("Clocked in", "Welcome back."); }
    catch (err: any) { toast.error("Clock-in failed", err?.data?.detail ?? "Try again."); }
  }
  async function doClockOut() {
    try {
      await clockOut().unwrap();
      await refetchSession();
      toast.success("Clocked out", "Have a good one.");
    } catch (err: any) {
      const detail: string = err?.data?.detail ?? "Try again.";
      // If the backend says we're not clocked in, our cache was stale — sync it.
      if (/not clocked in/i.test(detail)) {
        await refetchSession();
        toast.warning("No active shift", "Your shift was already ended elsewhere.");
      } else {
        toast.error("Clock-out failed", detail);
      }
    }
  }

  return (
    <>
      <PageHeader
        title="Agent control panel"
        description="Your live workspace for status, calls, and wrap-ups."
        actions={
          session ? (
            <Button variant="danger" loading={clockingOut} onClick={doClockOut} leftIcon={<Icon name="logout" size={16} />}>
              Clock out
            </Button>
          ) : (
            <Button variant="success" loading={clockingIn} onClick={doClockIn} leftIcon={<Icon name="phone" size={16} />}>
              Clock in
            </Button>
          )
        }
      />

      {/* Status hero */}
      {sessionLoading ? (
        <Card className="mb-6"><CardBody><Skeleton className="h-24" /></CardBody></Card>
      ) : !session ? (
        <Card className="mb-6">
          <CardBody>
            <EmptyState
              icon={<Icon name="phone" size={20} />}
              title="You're off the clock"
              description="Clock in to start receiving calls and tracking your shift."
              action={
                <Button variant="success" loading={clockingIn} onClick={doClockIn} leftIcon={<Icon name="phone" size={16} />}>
                  Clock in
                </Button>
              }
            />
          </CardBody>
        </Card>
      ) : (
        <Card className="mb-6 overflow-hidden">
          <div className="relative bg-gradient-to-br from-ink-900 via-ink-950 to-brand-950 text-white p-6">
            <div className="absolute inset-0 bg-grid opacity-20" />
            <div className="relative grid grid-cols-1 lg:grid-cols-3 gap-6 items-center">
              <div className="flex items-center gap-4">
                <StatusOrb status={currentStatus} />
                <div>
                  <div className="text-[10px] uppercase tracking-wider text-white/60">Current status</div>
                  <div className="text-2xl font-semibold">{currentStatus}</div>
                  {session.currentReason && (
                    <div className="text-xs text-white/70 mt-0.5">{session.currentReason}</div>
                  )}
                </div>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <HeroStat
                  label="Shift time"
                  value={formatDuration(Date.now() - new Date(session.clockInAt).getTime())}
                  icon="clock"
                />
                <HeroStat
                  label="Calls today"
                  value={String(stats.total)}
                  icon="phone"
                />
              </div>
              <div className="text-sm space-y-1.5 lg:text-right">
                <div className="text-white/70 text-xs">Clocked in</div>
                <div className="font-medium">{new Date(session.clockInAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</div>
                <div className="text-white/60 text-[11px]">{new Date(session.clockInAt).toLocaleDateString()}</div>
              </div>
            </div>
          </div>

          <CardBody>
            <div className="flex flex-col sm:flex-row gap-3 items-stretch sm:items-end">
              <Input
                label="Status reason (optional)"
                placeholder="e.g. Coffee break"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                containerClassName="flex-1"
              />
            </div>
            <div className="mt-3 flex flex-wrap gap-2">
              {STATUS_OPTIONS.map((opt) => {
                const active = currentStatus === opt.key;
                return (
                  <Button
                    key={opt.key}
                    variant={active ? "primary" : "outline"}
                    size="sm"
                    leftIcon={<Icon name={opt.icon} size={14} />}
                    loading={changingStatus && active}
                    onClick={() => changeStatus(opt.key)}
                  >{opt.key}</Button>
                );
              })}
            </div>
          </CardBody>
        </Card>
      )}

      {/* Today's stats */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-6">
        <SmallStat label="Calls today"     value={stats.total}   icon="phone"    tone="bg-brand-50 text-brand-600" />
        <SmallStat label="Sales today"     value={stats.sales}   icon="briefcase" tone="bg-emerald-50 text-emerald-600" />
        <SmallStat label="Pending wrap-ups" value={stats.pending} icon="clock"    tone="bg-amber-50 text-amber-600" />
      </div>

      {/* Wrap-up alert */}
      {unwrapped && codes && (
        <Card className="mb-6 border-amber-300 ring-1 ring-amber-200/60">
          <CardHeader
            title={
              <span className="flex items-center gap-2 text-amber-700">
                <Icon name="clock" size={18} /> Wrap-up required
              </span>
            }
            subtitle={
              <span>
                You can't go Available until you wrap up call{" "}
                <span className="font-mono text-ink-800">{unwrapped.providerCallId ?? unwrapped.id.slice(0, 8)}</span>.
              </span>
            }
          />
          <CardBody>
            <WrapUpForm
              callId={unwrapped.id}
              codes={codes}
              loading={wrappingUp}
              onSubmit={async (vals) => {
                try {
                  await wrapUp(vals).unwrap();
                  await refetchSession();
                  toast.success("Wrap-up saved");
                } catch (err: any) {
                  toast.error("Couldn't save wrap-up", err?.data?.detail ?? "Try again.");
                }
              }}
            />
          </CardBody>
        </Card>
      )}

      {/* Recent calls */}
      <Card>
        <CardHeader title="Recent calls" subtitle="Your last 20 calls" />
        <CardBody className="pt-0 px-0">
          {callsLoading ? (
            <div className="px-5 pb-5 space-y-2">
              {[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}
            </div>
          ) : !recentCalls || recentCalls.length === 0 ? (
            <div className="px-5 pb-5">
              <EmptyState
                icon={<Icon name="phone" size={20} />}
                title="No calls yet"
                description="Calls will appear here once you start your shift."
              />
            </div>
          ) : (
            <Table className="border-0 shadow-none rounded-none">
              <THead>
                <TR>
                  <TH>Started</TH>
                  <TH>Direction</TH>
                  <TH>Status</TH>
                  <TH>Wrap-up</TH>
                  <TH className="text-right">Recording</TH>
                </TR>
              </THead>
              <TBody>
                {recentCalls.map((c) => (
                  <TR key={c.id}>
                    <TD className="text-ink-600">{new Date(c.initiatedAt).toLocaleString()}</TD>
                    <TD>
                      <Badge tone={c.direction === "Inbound" ? "info" : "brand"} variant="soft">
                        {c.direction}
                      </Badge>
                    </TD>
                    <TD>
                      <Badge tone={statusTone[c.status] ?? "neutral"} variant="soft" dot>
                        {c.status}
                      </Badge>
                    </TD>
                    <TD>
                      {c.wrapUpCode
                        ? <span className="text-ink-700 font-mono text-xs">{c.wrapUpCode}</span>
                        : <Badge tone="warning" variant="soft">Pending</Badge>}
                    </TD>
                    <TD className="text-right">
                      {c.recordingUrl ? (
                        <a
                          href={c.recordingUrl} target="_blank" rel="noreferrer"
                          className="inline-flex items-center gap-1.5 text-brand-600 hover:text-brand-700 text-sm font-medium"
                        >
                          <Icon name="phone" size={14} /> Listen
                        </a>
                      ) : <span className="text-ink-400">—</span>}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>
    </>
  );
}

function StatusOrb({ status }: { status: AgentStatus }) {
  const tone = statusTone[status] ?? "neutral";
  const colorMap: Record<string, string> = {
    success: "bg-emerald-400",
    info:    "bg-brand-400",
    warning: "bg-amber-400",
    neutral: "bg-ink-400",
    danger:  "bg-rose-400",
  };
  return (
    <div className="relative h-14 w-14 grid place-items-center">
      <span className={`absolute inset-0 rounded-full opacity-30 animate-pulse-ring ${colorMap[tone]}`} />
      <span className={`absolute inset-2 rounded-full ${colorMap[tone]}`} />
      <span className="absolute inset-3 rounded-full bg-white/90" />
      <span className={`relative h-3 w-3 rounded-full ${colorMap[tone]}`} />
    </div>
  );
}

function HeroStat({ label, value, icon }: { label: string; value: string; icon: IconName }) {
  return (
    <div className="rounded-xl bg-white/5 ring-1 ring-white/10 p-3.5 backdrop-blur">
      <div className="flex items-center gap-2 text-white/70 text-[10px] uppercase tracking-wider">
        <Icon name={icon} size={12} /> {label}
      </div>
      <div className="text-xl font-semibold mt-1 font-mono">{value}</div>
    </div>
  );
}

function SmallStat({ label, value, icon, tone }: { label: string; value: number; icon: IconName; tone: string }) {
  return (
    <div className="surface p-4 flex items-center gap-3">
      <div className={`h-10 w-10 rounded-lg grid place-items-center ${tone}`}>
        <Icon name={icon} size={18} />
      </div>
      <div>
        <div className="text-xs font-medium text-ink-500 uppercase tracking-wide">{label}</div>
        <div className="text-xl font-semibold text-ink-900">{value}</div>
      </div>
    </div>
  );
}

function WrapUpForm({
  callId, codes, loading, onSubmit,
}: {
  callId: string;
  codes: any[];
  loading: boolean;
  onSubmit: (vals: { callId: string; wrapUpCode: string; notes?: string }) => Promise<void>;
}) {
  const [code, setCode] = useState(codes[0]?.code ?? "");
  const [notes, setNotes] = useState("");
  return (
    <form
      className="grid grid-cols-1 sm:grid-cols-[200px_1fr_auto] gap-3 items-end"
      onSubmit={(e) => {
        e.preventDefault();
        if (!code) return;
        onSubmit({ callId, wrapUpCode: code, notes: notes || undefined });
      }}
    >
      <Select label="Disposition" value={code} onChange={(e) => setCode(e.target.value)}>
        {codes.map((c) => <option key={c.code} value={c.code}>{c.label}</option>)}
      </Select>
      <Input
        label="Notes" placeholder="Anything noteworthy from the call"
        value={notes} onChange={(e) => setNotes(e.target.value)}
      />
      <Button type="submit" loading={loading} leftIcon={<Icon name="check" size={16} />}>
        Save wrap-up
      </Button>
    </form>
  );
}
