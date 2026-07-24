import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { useCoachAgentMutation, useForceAgentStatusMutation, useLiveAgentsQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, Input, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast, cn,
} from "../../shared/ui";

const statusTone: Record<string, "success" | "info" | "warning" | "neutral" | "danger"> = {
  Available: "success", OnCall: "info", Break: "warning", Lunch: "warning",
  Training: "neutral", Meeting: "neutral", Offline: "danger",
};

function parseDuration(d: string): number {
  if (!d) return 0;
  const parts = d.split(":");
  if (parts.length < 3) return 0;
  const days = parts[0].includes(".") ? parseInt(parts[0].split(".")[0]) : 0;
  const hours = parseInt(parts[0].includes(".") ? parts[0].split(".")[1] : parts[0]);
  return days * 86400 + hours * 3600 + parseInt(parts[1]) * 60 + parseFloat(parts[2]);
}

function formatDuration(d: string) {
  const t = Math.floor(parseDuration(d));
  const h = Math.floor(t / 3600);
  const m = Math.floor((t % 3600) / 60);
  const s = t % 60;
  return h > 0
    ? `${h}h ${m.toString().padStart(2, "0")}m`
    : `${m}m ${s.toString().padStart(2, "0")}s`;
}

export function SupervisorPage() {
  const { data: agents, isLoading, refetch } = useLiveAgentsQuery(undefined, { pollingInterval: 5_000 });
  const [forceStatus] = useForceAgentStatusMutation();
  const [coach] = useCoachAgentMutation();
  const toast = useToast();

  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");

  const stats = useMemo(() => {
    const a = agents ?? [];
    return {
      total: a.length,
      available: a.filter((x) => x.status === "Available").length,
      onCall:    a.filter((x) => x.status === "OnCall").length,
      onBreak:   a.filter((x) => x.status === "Break" || x.status === "Lunch").length,
    };
  }, [agents]);

  const filtered = useMemo(() => {
    let items = (agents ?? []) as any[];
    if (statusFilter !== "all") items = items.filter((a) => a.status === statusFilter);
    const q = search.trim().toLowerCase();
    if (q) items = items.filter((a) => a.userName?.toLowerCase().includes(q));
    return items;
  }, [agents, search, statusFilter]);

  async function force(userId: string, status: string, label: string, reason: string) {
    try {
      await forceStatus({ id: userId, status, reason }).unwrap();
      await refetch();
      toast.success(`${label} applied`, `Agent moved to ${status}.`);
    } catch (err: unknown) {
      toast.error(`Couldn't ${label.toLowerCase()}`, getErrorDetail(err) ?? "Try again.");
    }
  }

  async function doCoach(userId: string, mode: "monitor" | "whisper" | "barge", agentName: string) {
    try {
      await coach({ id: userId, mode }).unwrap();
      toast.success(`${mode[0].toUpperCase() + mode.slice(1)} started`, `Connected to ${agentName}.`);
    } catch (err: unknown) {
      toast.error("Couldn't start coaching", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Supervisor"
        description="Live floor view — monitor, coach, and intervene with agents in real time."
        actions={
          <Button variant="outline" leftIcon={<Icon name="filter" size={16} />} onClick={() => refetch()}>
            Refresh
          </Button>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-6">
        <SupTile label="Clocked in" value={stats.total}     icon="users" tone="bg-brand-50 text-brand-600" />
        <SupTile label="Available"  value={stats.available} icon="check"   tone="bg-emerald-50 text-emerald-600" />
        <SupTile label="On call"    value={stats.onCall}    icon="phone"  tone="bg-brand-50 text-brand-600" />
        <SupTile label="On break"   value={stats.onBreak}   icon="clock"  tone="bg-amber-50 text-amber-600" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex flex-wrap gap-3 items-center">
          <div className="flex-1 min-w-[260px]">
            <Input
              leftIcon={<Icon name="search" size={16} />}
              placeholder="Search by agent name..."
              value={search} onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <div className="flex flex-wrap gap-1.5">
            {["all", "Available", "OnCall", "Break", "Offline"].map((f) => (
              <button
                key={f}
                onClick={() => setStatusFilter(f)}
                className={cn(
                  "px-3 py-1.5 rounded-lg text-xs font-medium transition-colors",
                  statusFilter === f ? "bg-brand-600 text-white" : "bg-ink-100 text-ink-700 hover:bg-ink-200",
                )}
              >{f === "all" ? "All" : f}</button>
            ))}
          </div>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>
          {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}
        </CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="users" size={20} />}
            title={search || statusFilter !== "all" ? "No agents match" : "No agents clocked in"}
            description="Live agent activity will appear here as your team starts shifts."
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>Agent</TH>
              <TH>Status</TH>
              <TH>Reason</TH>
              <TH>Duration</TH>
              <TH>Call</TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {filtered.map((a) => (
              <TR key={a.userId}>
                <TD>
                  <div className="flex items-center gap-3">
                    <Avatar name={a.userName ?? "?"} size={36} />
                    <div className="min-w-0">
                      <div className="font-medium text-ink-900">{a.userName}</div>
                      <div className="text-xs text-ink-500">since {new Date(a.sinceAt).toLocaleTimeString()}</div>
                    </div>
                  </div>
                </TD>
                <TD>
                  <Badge tone={statusTone[a.status] ?? "neutral"} variant="soft" dot>
                    {a.status}
                  </Badge>
                </TD>
                <TD className="text-ink-600 max-w-[200px] truncate">{a.reason ?? <span className="text-ink-400">—</span>}</TD>
                <TD className="font-mono text-ink-700">{formatDuration(a.duration)}</TD>
                <TD>
                  {a.currentCallStatus
                    ? <Badge tone="info" variant="soft" dot>{a.currentCallStatus}</Badge>
                    : <span className="text-ink-400">—</span>}
                </TD>
                <TD>
                  <div className="flex items-center justify-end gap-1.5 flex-wrap">
                    <Button variant="ghost" size="sm" leftIcon={<Icon name="clock" size={14} />}
                      onClick={() => force(a.userId, "Break", "Break", "Supervisor break")}>Break</Button>
                    <Button variant="ghost" size="sm" className="text-rose-600 hover:bg-rose-50"
                      leftIcon={<Icon name="logout" size={14} />}
                      onClick={() => force(a.userId, "Offline", "Logout", "Forced logout")}>Logout</Button>
                    <div className="h-5 w-px bg-ink-200 mx-1" />
                    <Button variant="ghost" size="sm" disabled={!a.currentCallStatus}
                      onClick={() => doCoach(a.userId, "monitor", a.userName)}>Listen</Button>
                    <Button variant="ghost" size="sm" disabled={!a.currentCallStatus}
                      onClick={() => doCoach(a.userId, "whisper", a.userName)}>Whisper</Button>
                    <Button variant="ghost" size="sm" disabled={!a.currentCallStatus}
                      className="text-emerald-700 hover:bg-emerald-50"
                      onClick={() => doCoach(a.userId, "barge", a.userName)}>Barge</Button>
                  </div>
                </TD>
              </TR>
            ))}
          </TBody>
        </Table>
      )}
    </>
  );
}

function SupTile({ label, value, icon, tone }: { label: string; value: number; icon: any; tone: string }) {
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
