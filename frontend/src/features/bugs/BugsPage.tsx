import { useMemo, useState } from "react";
import { getErrorDetail } from "../../shared/api/apiError";
import {
  useListBugsQuery, useGetBugQuery, useSetBugStatusMutation, useAssignBugMutation, useCommentBugMutation,
  useUserDirectoryQuery,
} from "../../shared/api/baseApi";
import {
  BUG_STATUSES, BUG_WORKFLOW, isBugOffRamp, bugStatusMeta, bugStatusLabel, bugSeverityMeta,
} from "../../shared/constants/bugs";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { timeAgoShort } from "../../shared/lib/time";
import { BUGS_MSG } from "./messages";
import {
  Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, Input, Modal, PageHeader,
  SearchInput, Select, Skeleton, Stat, Stepper, Table, TBody, TD, TH, THead, TR, Textarea, cn, useToast,
} from "../../shared/ui";

/**
 * Bug tracker — the triage board for everything reported through the in-app "Report a bug" button.
 * Everyone sees and comments; Admins/SuperAdmin move a bug through the status workflow and assign it.
 */
export function BugsPage() {
  const [status, setStatus] = useState<string>("");
  const [scope, setScope] = useState<"all" | "mine" | "assigned">("all");
  const { data: bugs, isLoading } = useListBugsQuery({ status: status || undefined, scope });
  const [openId, setOpenId] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const list = bugs ?? [];
  // Client-side search over the already-loaded reports — the query above is untouched.
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const all = bugs ?? [];
    if (!q) return all;
    return all.filter((b) =>
      [b.title, b.reporterName, b.assignedToName, b.pageUrl]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [bugs, search]);

  // Selection is scoped to the visible (searched) rows so a bulk action never reaches a hidden one.
  const sel = useRowSelection(filtered.map((b) => b.id));
  const canManage = list.some((b) => b.canManage);
  const [bulkStatus, setBulkStatus] = useState(BUG_STATUSES[1].value);
  const [setBugStatus, { isLoading: bulkBusy }] = useSetBugStatusMutation();
  const toast = useToast();

  const counts = useMemo(() => {
    const c = { New: 0, InProgress: 0, Resolved: 0 } as Record<string, number>;
    for (const b of list) if (b.status in c) c[b.status]++;
    return c;
  }, [list]);

  function exportSelected() {
    const chosen = filtered.filter((b) => sel.isSelected(b.id));
    exportRowsToCsv(chosen, [
      { header: "Title", value: (b) => b.title },
      { header: "Severity", value: (b) => bugSeverityMeta(b.severity).label },
      { header: "Status", value: (b) => bugStatusLabel(b.status) },
      { header: "Reporter", value: (b) => b.reporterName },
      { header: "Assigned to", value: (b) => b.assignedToName ?? "" },
      { header: "Page", value: (b) => b.pageUrl ?? "" },
      { header: "Reported", value: (b) => new Date(b.createdAt).toISOString() },
    ], `bugs-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(BUGS_MSG.exportReady, BUGS_MSG.rowsDownloaded(chosen.length));
  }

  async function bulkApplyStatus() {
    const ids = sel.selectedIds;
    if (ids.length === 0) return;
    const results = await Promise.allSettled(ids.map((id) => setBugStatus({ id, status: bulkStatus }).unwrap()));
    const failed = ids.filter((_, i) => results[i].status === "rejected");
    const ok = ids.length - failed.length;
    if (ok > 0) toast.success(BUGS_MSG.movedCount(ok, bugStatusLabel(bulkStatus)));
    if (failed.length > 0) { toast.error(BUGS_MSG.couldntBeUpdated(failed.length), BUGS_MSG.stillSelected); sel.keepOnly(failed); }
    else sel.clear();
  }

  return (
    <>
      <PageHeader eyebrow="Support" title="Bugs"
        description="Every issue reported from the app, triaged through a clear workflow: New → Triaged → In Progress → Resolved → Closed." />

      <div className="grid grid-cols-3 gap-3 mb-5 stagger-children">
        <Stat className="stagger-item" label="New" value={counts.New} icon={<Icon name="inbox" size={16} />} tone="brand" />
        <Stat className="stagger-item" label="In progress" value={counts.InProgress} icon={<Icon name="clock" size={16} />} tone="accent" />
        <Stat className="stagger-item" label="Resolved" value={counts.Resolved} icon={<Icon name="check" size={16} />} tone="success" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <Select aria-label="Filter by status" value={status} onChange={(e) => setStatus(e.target.value)} className="w-48">
            <option value="">All statuses</option>
            {BUG_STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </Select>
          <div className="inline-flex rounded-lg border border-ink-200 p-0.5 text-sm">
            {([
              { key: "all", label: "All reports" },
              { key: "mine", label: "My reports" },
              { key: "assigned", label: "Assigned to me" },
            ] as const).map((s) => (
              <button key={s.key} type="button" onClick={() => setScope(s.key)}
                className={cn("px-3 h-8 rounded-md transition-colors whitespace-nowrap",
                  scope === s.key ? "bg-brand-50 text-brand-700 font-medium" : "text-ink-500 hover:text-ink-800")}>
                {s.label}
              </button>
            ))}
          </div>
          <SearchInput value={search} onChange={setSearch} placeholder={BUGS_MSG.searchPlaceholder} className="w-64" />
          <span className="text-sm text-ink-500 ml-auto">{list.length} {list.length === 1 ? "bug" : "bugs"}</span>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : list.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="success" size={20} />} title={BUGS_MSG.noBugsTitle}
            description={status || scope === "mine" ? BUGS_MSG.noBugsFilterDesc : BUGS_MSG.noBugsDesc} />
        </CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="search" size={20} />} title={BUGS_MSG.noMatchTitle} description={BUGS_MSG.noMatchDesc} />
        </CardBody></Card>
      ) : (
        <div className="overflow-x-auto animate-rise">
          <Table>
            <THead><TR>
              <TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH>
              <TH>Bug</TH>
              <TH>Severity</TH>
              <TH>Status</TH>
              <TH>Reporter</TH>
              <TH>Assigned</TH>
              <TH numeric>Reported</TH>
            </TR></THead>
            <TBody>
              {filtered.map((b) => (
                <TR key={b.id}
                  className={cn("cursor-pointer border-l-[3px]", bugSeverityMeta(b.severity).accent, sel.isSelected(b.id) && "bg-brand-50/40")}
                  onClick={() => setOpenId(b.id)}>
                  <TD onClick={(e) => e.stopPropagation()}>
                    <Checkbox aria-label={`Select ${b.title}`} {...sel.checkboxProps(b.id)} />
                  </TD>
                  <TD className="max-w-[26rem]">
                    <div className="font-medium text-ink-900 truncate">{b.title}</div>
                    {b.pageUrl && <div className="font-mono text-[11px] text-ink-400 truncate">{b.pageUrl}</div>}
                  </TD>
                  <TD><Badge tone={bugSeverityMeta(b.severity).tone} variant="soft">{bugSeverityMeta(b.severity).label}</Badge></TD>
                  <TD><Badge tone={bugStatusMeta(b.status).tone} variant="soft">{bugStatusLabel(b.status)}</Badge></TD>
                  <TD className="text-ink-600">{b.reporterName}</TD>
                  <TD className="text-ink-600">{b.assignedToName ?? <span className="text-ink-400">—</span>}</TD>
                  <TD numeric className="text-ink-500 tabular-nums whitespace-nowrap">{timeAgoShort(b.createdAt)}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
          <BulkActionBar
            count={sel.selectedCount} itemNoun="bug" onClear={sel.clear}
            extra={canManage ? (
              <Select aria-label="Status to set" value={bulkStatus} onChange={(e) => setBulkStatus(e.target.value as typeof bulkStatus)}
                className="h-8 w-40 !bg-white/10 !border-white/20 !text-white text-sm">
                {BUG_STATUSES.map((s) => <option key={s.value} value={s.value} className="text-ink-900">{s.label}</option>)}
              </Select>
            ) : undefined}
            actions={[
              ...(canManage ? [{ key: "status", label: "Set status", icon: "check" as const, onClick: bulkApplyStatus, loading: bulkBusy }] : []),
              { key: "csv", label: "Export CSV", icon: "download" as const, onClick: exportSelected },
            ]}
          />
        </div>
      )}

      {openId && <BugDetailModal bugId={openId} onClose={() => setOpenId(null)} />}
    </>
  );
}

function BugDetailModal({ bugId, onClose }: { bugId: string; onClose: () => void }) {
  const { data, isLoading } = useGetBugQuery(bugId);
  const { data: users } = useUserDirectoryQuery();
  const [setStatus, { isLoading: savingStatus }] = useSetBugStatusMutation();
  const [assign] = useAssignBugMutation();
  const [comment, { isLoading: commenting }] = useCommentBugMutation();
  const toast = useToast();

  const bug = data?.bug;
  const [newStatus, setNewStatus] = useState<string>("");
  const [resolution, setResolution] = useState("");
  const [body, setBody] = useState("");

  const effectiveStatus = newStatus || bug?.status || "New";
  const showResolution = ["Resolved", "Closed", "WontFix", "Duplicate", "CannotReproduce"].includes(effectiveStatus);

  async function applyStatus() {
    if (!bug || effectiveStatus === bug.status && !resolution.trim()) return;
    try {
      await setStatus({ id: bug.id, status: effectiveStatus, resolution: resolution.trim() || undefined }).unwrap();
      toast.success(BUGS_MSG.movedTo(bugStatusLabel(effectiveStatus)));
      setResolution(""); setNewStatus("");
    } catch (err: unknown) { toast.error(BUGS_MSG.updateStatusFailed, getErrorDetail(err) ?? BUGS_MSG.retry); }
  }

  async function changeAssignee(userId: string) {
    if (!bug) return;
    try { await assign({ id: bug.id, assignedToUserId: userId || null }).unwrap(); }
    catch (err: unknown) { toast.error(BUGS_MSG.assignFailed, getErrorDetail(err) ?? BUGS_MSG.retry); }
  }

  async function sendComment() {
    if (!body.trim() || !bug) return;
    try { await comment({ id: bug.id, comment: body.trim() }).unwrap(); setBody(""); }
    catch (err: unknown) { toast.error(BUGS_MSG.commentFailed, getErrorDetail(err) ?? BUGS_MSG.retry); }
  }

  return (
    <Modal open onClose={onClose} size="xl"
      title={bug ? bug.title : "Bug"}
      description={bug ? `Reported by ${bug.reporterName} · ${new Date(bug.createdAt).toLocaleString()}` : undefined}>
      {isLoading || !bug ? (
        <div className="space-y-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-16" />)}</div>
      ) : (
        <div className="space-y-5">
          <div className="flex items-center gap-2 flex-wrap">
            <Badge tone={bugSeverityMeta(bug.severity).tone} variant="soft">{bugSeverityMeta(bug.severity).label} severity</Badge>
            {bug.assignedToName && <Badge tone="brand" variant="soft">Assigned · {bug.assignedToName}</Badge>}
            {bug.pageUrl && <span className="font-mono text-[11px] text-ink-500">{bug.pageUrl}</span>}
          </div>

          {/* Workflow position: the linear pipeline for on-track statuses, a distinct banner otherwise. */}
          {isBugOffRamp(bug.status) ? (
            <div className={cn("rounded-xl border px-3 py-2 text-sm font-medium inline-flex items-center gap-2",
              bugStatusMeta(bug.status).tone === "danger" ? "border-rose-200 bg-rose-50 text-rose-700" : "border-ink-200 bg-ink-50 text-ink-700")}>
              <Icon name="archive" size={14} /> Closed as {bugStatusLabel(bug.status)}
            </div>
          ) : (
            <Stepper steps={BUG_WORKFLOW.map((s) => ({ key: s, label: bugStatusLabel(s) }))}
              currentIndex={BUG_WORKFLOW.indexOf(bug.status as (typeof BUG_WORKFLOW)[number])} />
          )}

          <p className="text-sm text-ink-800 whitespace-pre-wrap break-words leading-relaxed">{bug.description}</p>
          {bug.resolution && (
            <div className="rounded-xl border border-emerald-200 bg-emerald-50/60 p-3 text-sm text-emerald-900">
              <span className="font-semibold">Resolution:</span> {bug.resolution}
            </div>
          )}

          {/* Triage — managers only */}
          {bug.canManage && (
            <div className="rounded-xl border border-ink-200 p-3 space-y-3">
              <div className="text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500">Triage</div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <Select label="Move to status" value={effectiveStatus} onChange={(e) => setNewStatus(e.target.value)}>
                  {BUG_STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                </Select>
                <Select label="Assign to" value={bug.assignedToUserId ?? ""} onChange={(e) => changeAssignee(e.target.value)}>
                  <option value="">Unassigned</option>
                  {(users ?? []).map((u) => <option key={u.id} value={u.id}>{u.userName}</option>)}
                </Select>
              </div>
              {showResolution && (
                <Textarea label="Resolution / reason (optional)" value={resolution} onChange={(e) => setResolution(e.target.value)}
                  rows={2} placeholder="What was done, or why it's being closed this way." />
              )}
              <div className="flex justify-end">
                <Button size="sm" loading={savingStatus} onClick={applyStatus}
                  disabled={effectiveStatus === bug.status && !resolution.trim()} leftIcon={<Icon name="check" size={14} />}>
                  Update status
                </Button>
              </div>
            </div>
          )}

          {/* Activity trail */}
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500 mb-2">Activity</div>
            <div className="space-y-2.5">
              {data.activity.length === 0 && <p className="text-sm text-ink-400">No activity yet.</p>}
              {data.activity.map((a) => (
                <div key={a.id} className="flex items-start gap-2.5 text-sm">
                  <div className="mt-0.5 h-6 w-6 shrink-0 grid place-items-center rounded-full bg-ink-100 text-ink-500">
                    <Icon name={a.toStatus ? "refresh" : "chat"} size={12} />
                  </div>
                  <div className="min-w-0">
                    <span className="font-medium text-ink-800">{a.userName}</span>{" "}
                    {a.toStatus ? (
                      <span className="text-ink-600">
                        moved it {a.fromStatus ? <>from <b>{bugStatusLabel(a.fromStatus)}</b> </> : ""}to <b>{bugStatusLabel(a.toStatus)}</b>
                      </span>
                    ) : <span className="text-ink-600">commented</span>}
                    <span className="text-ink-400"> · {timeAgoShort(a.createdAt)}</span>
                    {a.comment && <p className="text-ink-700 whitespace-pre-wrap break-words mt-0.5">{a.comment}</p>}
                  </div>
                </div>
              ))}
            </div>
            <div className="flex items-center gap-2 mt-3">
              <Input value={body} onChange={(e) => setBody(e.target.value)} placeholder="Add a comment…"
                onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); sendComment(); } }} />
              <Button size="sm" variant="ghost" loading={commenting} disabled={!body.trim()} onClick={sendComment}>Send</Button>
            </div>
          </div>
        </div>
      )}
    </Modal>
  );
}
