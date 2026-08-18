import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useCompleteCallbackMutation, useMyCallbacksQuery, useScheduleCallbackMutation, useMyLeadsQuery } from "../../shared/api/baseApi";
import {
  Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { MESSAGES } from "../../shared/constants/messages";
import { CALLBACKS_MSG } from "./messages";

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
  // Always pull completed rows too — otherwise the "Completed" stat card and tab badge
  // read 0 until you actually click into the Completed tab (the count is derived from
  // whatever's loaded). Per-user callback volume is small, and loading them once also
  // makes tab switches instant instead of refetching. The tab still filters client-side.
  const { data: callbacks, isLoading } = useMyCallbacksQuery({ includeCompleted: true });
  const [schedule, { isLoading: scheduling }] = useScheduleCallbackMutation();
  const [complete] = useCompleteCallbackMutation();
  const toast = useToast();

  const [open, setOpen] = useState(false);
  const [completingId, setCompletingId] = useState<string | null>(null);
  const [leadId, setLeadId] = useState("");
  const { data: myLeads = [] } = useMyLeadsQuery();
  const [when, setWhen] = useState(() => {
    // datetime-local wants a LOCAL wall-clock string; toISOString() is UTC, so shift by the
    // tz offset first — otherwise the default reads hours off for anyone not on UTC.
    const d = new Date(Date.now() + 60 * 60 * 1000);
    d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
    return d.toISOString().slice(0, 16);
  });
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

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: { status: (c) => (c.completed ? "Completed" : "Pending") },
  });

  const sel = useRowSelection(sorted.map((c) => c.id));

  function exportSelected() {
    const chosen = sorted.filter((c) => sel.isSelected(c.id));
    exportRowsToCsv(chosen, [
      { header: "Lead name", value: (c) => c.leadName },
      { header: "Phone", value: (c) => c.leadPhone },
      { header: "Due at", value: (c) => new Date(c.scheduledFor).toLocaleString() },
      { header: "Notes", value: (c) => c.reason ?? "" },
    ], `callbacks-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(CALLBACKS_MSG.exportReadyTitle, CALLBACKS_MSG.rowsDownloaded(chosen.length));
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await schedule({
        leadId,
        scheduledFor: new Date(when).toISOString(),
        reason: reason || undefined,
      }).unwrap();
      toast.success(CALLBACKS_MSG.callbackScheduledTitle, CALLBACKS_MSG.callbackScheduledBody(new Date(when).toLocaleString()));
      setLeadId(""); setReason(""); setOpen(false);
    } catch (err: unknown) {
      toast.error(CALLBACKS_MSG.scheduleCallbackFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function markDone(id: string) {
    setCompletingId(id);
    try {
      await complete(id).unwrap();
      toast.success(CALLBACKS_MSG.callbackCompleted);
    } catch (err: unknown) {
      toast.error(CALLBACKS_MSG.markCompleteFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    } finally {
      setCompletingId(null);
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
        <Stat label="Upcoming"  value={stats.upcoming} icon={<Icon name="calendar" size={16} />} tone="warning"
              hint="Due today or later" />
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
            title={CALLBACKS_MSG.noCallbacksTitle}
            description={CALLBACKS_MSG.noCallbacksBody}
            action={
              <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>
                Schedule callback
              </Button>
            }
          />
        </CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
        <Table>
          <THead>
            <TR>
              <TH className="w-10"><Checkbox aria-label="Select all callbacks" {...sel.allCheckboxProps} /></TH>
              <TH sortDir={dirFor("scheduledFor")} onClick={() => toggle("scheduledFor")}><span className="inline-flex items-center gap-1">When<InfoHint title="When" side="bottom">The scheduled date and time. The coloured tag shows how soon it's due — amber is within the hour, red means it's already overdue.</InfoHint></span></TH>
              <TH sortDir={dirFor("leadName")} onClick={() => toggle("leadName")}>Lead</TH>
              <TH sortDir={dirFor("reason")} onClick={() => toggle("reason")}>Reason</TH>
              <TH sortDir={dirFor("status")} onClick={() => toggle("status")}>Status</TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {sorted.map((c) => {
              const w = formatWhen(c.scheduledFor);
              return (
                <TR key={c.id} className={sel.isSelected(c.id) ? "bg-brand-50/40" : undefined}>
                  <TD>
                    <Checkbox aria-label={`Select ${c.leadName}`} {...sel.checkboxProps(c.id)} />
                  </TD>
                  <TD>
                    <div className="text-ink-900">{w.abs}</div>
                    {!c.completed && <Badge tone={w.tone} variant="soft" className="mt-1">{w.rel}</Badge>}
                  </TD>
                  <TD>
                    <Link to={`/leads/${c.leadId}`} className="font-medium text-brand-700 hover:underline">
                      {c.leadName}
                    </Link>
                    <div className="font-mono text-xs text-ink-500 tabular-nums whitespace-nowrap">{c.leadPhone}</div>
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
                          loading={completingId === c.id}
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
        <BulkActionBar
          count={sel.selectedCount} itemNoun="callback" onClear={sel.clear}
          actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
        />
        </div>
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
          <Select
            label="Lead" required
            value={leadId}
            onChange={(e) => setLeadId(e.target.value)}
            hint={myLeads.length === 0 ? "No leads in your queue yet — you can also schedule a callback from a lead's page." : "Pick the lead to follow up with."}
          >
            <option value="" disabled>Select a lead…</option>
            {myLeads.map((l) => (
              <option key={l.id} value={l.id}>
                {l.firstName} {l.lastName}{l.phoneNumber ? ` — ${l.phoneNumber}` : ""}
              </option>
            ))}
          </Select>
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

