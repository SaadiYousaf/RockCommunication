import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useCompleteCallbackMutation, useMyCallbacksQuery, useScheduleCallbackMutation } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";

type Bucket = "overdue" | "today" | "upcoming" | "completed" | "all";

function bucketOf(scheduledFor: string, completed: boolean): Bucket {
  if (completed) return "completed";
  const t = new Date(scheduledFor).getTime();
  const now = Date.now();
  const today = new Date(); today.setHours(23, 59, 59, 999);
  if (t < now - 5 * 60_000) return "overdue";
  if (t <= today.getTime()) return "today";
  return "upcoming";
}

function formatWhen(iso: string): { abs: string; rel: string; tone: "danger" | "warning" | "success" | "neutral" } {
  const date = new Date(iso);
  const now = Date.now();
  const diffMin = Math.round((date.getTime() - now) / 60000);
  const abs = date.toLocaleString();
  if (diffMin < -5) return { abs, rel: `${Math.abs(diffMin)}m overdue`, tone: "danger" };
  if (diffMin < 60) return { abs, rel: diffMin <= 0 ? "now" : `in ${diffMin}m`, tone: "warning" };
  if (diffMin < 60 * 24) return { abs, rel: `in ${Math.round(diffMin / 60)}h`, tone: "success" };
  return { abs, rel: `in ${Math.round(diffMin / (60 * 24))}d`, tone: "neutral" };
}

export function CallbacksPage() {
  const [tab, setTab] = useState<Bucket>("today");
  const includeCompleted = tab === "completed" || tab === "all";
  const { data: callbacks, isLoading } = useMyCallbacksQuery({ includeCompleted });
  const [schedule, { isLoading: scheduling }] = useScheduleCallbackMutation();
  const [complete] = useCompleteCallbackMutation();
  const toast = useToast();

  const [open, setOpen] = useState(false);
  const [leadId, setLeadId] = useState("");
  const [when, setWhen] = useState(() => new Date(Date.now() + 60 * 60 * 1000).toISOString().slice(0, 16));
  const [reason, setReason] = useState("");

  const buckets = useMemo(() => {
    const items = callbacks ?? [];
    const all: Record<Exclude<Bucket, "all">, number> = { overdue: 0, today: 0, upcoming: 0, completed: 0 };
    for (const c of items) {
      const b = bucketOf(c.scheduledFor, c.completed);
      if (b !== "all") all[b]++;
    }
    return all;
  }, [callbacks]);

  const filtered = useMemo(() => {
    const items = callbacks ?? [];
    if (tab === "all") return items;
    return items.filter((c) => bucketOf(c.scheduledFor, c.completed) === tab);
  }, [callbacks, tab]);

  const stats = { overdue: buckets.overdue, upcoming: buckets.today + buckets.upcoming, done: buckets.completed };

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await schedule({
        leadId,
        scheduledFor: new Date(when).toISOString(),
        reason: reason || undefined,
      }).unwrap();
      toast.success("Callback scheduled", `Reminder set for ${new Date(when).toLocaleString()}.`);
      setLeadId(""); setReason(""); setOpen(false);
    } catch (err: unknown) {
      toast.error("Couldn't schedule callback", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function markDone(id: string) {
    try {
      await complete(id).unwrap();
      toast.success("Callback completed");
    } catch (err: unknown) {
      toast.error("Couldn't mark complete", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Scheduled Callbacks"
        description="Stay on top of customer follow-ups and avoid overdue commitments."
        actions={
          <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>
            Schedule callback
          </Button>
        }
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-5">
        <Stat label="Overdue"   value={stats.overdue}  icon={<Icon name="alert" size={16} />}    tone="danger"
              hint={stats.overdue > 0 ? "Reach out now" : "All clear"} />
        <Stat label="Upcoming"  value={stats.upcoming} icon={<Icon name="calendar" size={16} />} tone="warning" />
        <Stat label="Completed" value={stats.done}     icon={<Icon name="success" size={16} />}  tone="success" />
      </div>

      <Card className="mb-4">
        <div className="px-2 pt-2 pb-1 overflow-x-auto">
          <Tabs<Bucket>
            value={tab} onChange={setTab}
            items={[
              { value: "today",     label: "Today",     count: buckets.today },
              { value: "overdue",   label: "Overdue",   count: buckets.overdue },
              { value: "upcoming",  label: "Upcoming",  count: buckets.upcoming },
              { value: "completed", label: "Completed", count: buckets.completed },
              { value: "all",       label: "All" },
            ]}
          />
        </div>
      </Card>

      {isLoading ? (
        <Card><CardBody>
          {[0, 1, 2, 3].map((i) => (
            <div key={i} className="flex items-center gap-3 py-3 border-b hairline last:border-0">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-4 w-20" />
              <Skeleton className="h-4 w-48 ml-auto" />
            </div>
          ))}
        </CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="calendar" size={20} />}
            title="No callbacks scheduled"
            description="Schedule a callback to keep customer follow-ups on track."
            action={
              <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>
                Schedule callback
              </Button>
            }
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>When</TH>
              <TH>Lead</TH>
              <TH>Reason</TH>
              <TH>Status</TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {filtered.map((c) => {
              const w = formatWhen(c.scheduledFor);
              return (
                <TR key={c.id}>
                  <TD>
                    <div className="text-ink-900">{w.abs}</div>
                    {!c.completed && <Badge tone={w.tone} variant="soft" className="mt-1">{w.rel}</Badge>}
                  </TD>
                  <TD className="font-mono text-xs">
                    <Link to={`/leads/${c.leadId}`} className="text-brand-700 hover:underline">
                      {c.leadId.slice(0, 8)}…
                    </Link>
                  </TD>
                  <TD className="text-ink-600">{c.reason ?? <span className="text-ink-400">—</span>}</TD>
                  <TD>
                    {c.completed
                      ? <Badge tone="success" variant="soft" dot>Completed</Badge>
                      : <Badge tone="warning" variant="soft" dot>Pending</Badge>}
                  </TD>
                  <TD>
                    <div className="flex justify-end">
                      {!c.completed && (
                        <Button
                          variant="outline" size="sm"
                          leftIcon={<Icon name="check" size={14} />}
                          onClick={() => markDone(c.id)}
                        >Mark done</Button>
                      )}
                    </div>
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
      )}

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Schedule a callback"
        description="Set a future reminder to follow up with a lead."
        size="lg"
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="schedule-cb" type="submit" loading={scheduling}>Schedule</Button>
          </>
        }
      >
        <form id="schedule-cb" onSubmit={submit} className="grid grid-cols-1 gap-3">
          <Input
            label="Lead ID" required
            placeholder="UUID of the lead"
            value={leadId}
            onChange={(e) => setLeadId(e.target.value)}
            hint="Find the lead's ID from the Leads list."
          />
          <Input
            label="When" type="datetime-local" required
            value={when} onChange={(e) => setWhen(e.target.value)}
          />
          <Input
            label="Reason"
            placeholder="e.g. Customer requested afternoon call"
            value={reason} onChange={(e) => setReason(e.target.value)}
          />
        </form>
      </Modal>
    </>
  );
}

