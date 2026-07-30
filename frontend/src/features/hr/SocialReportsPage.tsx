import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import {
  useListSocialReportsQuery, useCreateSocialReportMutation, useUpdateSocialReportMutation,
  useDeleteSocialReportMutation, useListEmployeesQuery,
} from "../../shared/api/baseApi";
import type { SocialMediaReport, SocialMediaInput } from "../../shared/api/types";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, InfoHint, Input, Modal, PageHeader, SearchInput,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";

const BLANK: SocialMediaInput = { date: new Date().toISOString(), employeeId: null, platform: "", postsMade: 0, queriesAnswered: 0, notes: "" };
const dateOnly = (iso: string | null | undefined) => (iso ? iso.slice(0, 10) : "");

/** HR → Social Media. Daily handling reports — posts made and customer queries answered. */
export function SocialReportsPage() {
  const [search, setSearch] = useState("");
  const { data: reports, isLoading } = useListSocialReportsQuery({ search: search.trim() || undefined });
  const { data: employees } = useListEmployeesQuery();
  const [create, { isLoading: creating }] = useCreateSocialReportMutation();
  const [update, { isLoading: updating }] = useUpdateSocialReportMutation();
  const [remove] = useDeleteSocialReportMutation();
  const toast = useToast();
  const confirm = useConfirm();

  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<SocialMediaReport | null>(null);
  const [form, setForm] = useState<SocialMediaInput>(BLANK);

  useEffect(() => {
    if (!editing) return;
    setForm({
      date: editing.date, employeeId: editing.employeeId ?? null, platform: editing.platform ?? "",
      postsMade: editing.postsMade, queriesAnswered: editing.queriesAnswered, notes: editing.notes ?? "",
    });
  }, [editing]);

  function openAdd() { setEditing(null); setForm({ ...BLANK, date: new Date().toISOString() }); setOpen(true); }
  function openEdit(r: SocialMediaReport) { setEditing(r); setOpen(true); }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const payload = { ...form, employeeId: form.employeeId || null };
    try {
      if (editing) { await update({ id: editing.id, ...payload }).unwrap(); toast.success("Report updated"); }
      else { await create(payload).unwrap(); toast.success("Report added"); }
      setOpen(false);
    } catch (err: unknown) { toast.error("Couldn't save", getErrorDetail(err) ?? "Try again."); }
  }

  async function onDelete(r: SocialMediaReport) {
    if (!(await confirm({ title: "Delete report?", description: `Remove the ${dateOnly(r.date)} report?`, confirmLabel: "Delete", danger: true }))) return;
    try { await remove(r.id).unwrap(); toast.success("Report removed"); }
    catch (err: unknown) { toast.error("Couldn't remove", getErrorDetail(err) ?? "Try again."); }
  }

  const rows = reports ?? [];
  const totalPosts = rows.reduce((s, r) => s + r.postsMade, 0);
  const totalQueries = rows.reduce((s, r) => s + r.queriesAnswered, 0);

  return (
    <>
      <PageHeader eyebrow="Human Resources" title="Social Media"
        description="Daily handling reports — posts published and customer queries answered."
        actions={<Button leftIcon={<Icon name="plus" size={15} />} onClick={openAdd}>Add report</Button>} />

      <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 mb-5">
        <Stat label="Reports" value={rows.length} icon={<Icon name="doc" size={16} />} tone="brand" />
        <Stat label="Posts (total)" value={totalPosts} icon={<Icon name="send" size={16} />} tone="accent" />
        <Stat label="Queries answered" value={totalQueries} icon={<Icon name="chat" size={16} />} tone="success" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[220px]"><SearchInput value={search} onChange={setSearch} placeholder="Search platform, notes…" /></div>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : rows.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="chat" size={20} />} title={search ? "No matches" : "No reports yet"}
            description={search ? "No report matches your search." : "Log a daily report of posts and queries answered."}
            action={!search ? <Button leftIcon={<Icon name="plus" size={15} />} onClick={openAdd}>Add report</Button> : undefined} />
        </CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
              <TH>Date</TH>
              <TH>
                <span className="inline-flex items-center gap-1">Handler
                  <InfoHint title="Handler" side="top">The employee who managed the social accounts on that date.</InfoHint>
                </span>
              </TH>
              <TH>Platform</TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Posts
                  <InfoHint title="Posts made" side="top">Number of posts the handler published that day.</InfoHint>
                </span>
              </TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Queries answered
                  <InfoHint title="Queries answered" side="top">Customer questions or messages the handler replied to that day.</InfoHint>
                </span>
              </TH>
              <TH>Notes</TH>
              <TH className="text-right">Actions</TH>
            </TR></THead>
            <TBody>
              {rows.map((r) => (
                <TR key={r.id}>
                  <TD className="tabular-nums text-ink-700">{dateOnly(r.date)}</TD>
                  <TD className="text-ink-600">{r.employeeName ?? <span className="text-ink-400">—</span>}</TD>
                  <TD>{r.platform ? <Badge tone="neutral" variant="soft">{r.platform}</Badge> : <span className="text-ink-400">—</span>}</TD>
                  <TD numeric className="tabular-nums font-medium text-ink-900">{r.postsMade}</TD>
                  <TD numeric className="tabular-nums font-medium text-ink-900">{r.queriesAnswered}</TD>
                  <TD className="text-ink-600 max-w-[220px]"><span className="line-clamp-2">{r.notes || <span className="text-ink-400">—</span>}</span></TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => openEdit(r)}>Edit</Button>
                      <Button variant="ghost" size="sm" aria-label="Delete" onClick={() => onDelete(r)}><Icon name="trash" size={14} className="text-rose-500" /></Button>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
      )}

      <Modal open={open} onClose={() => setOpen(false)} title={editing ? "Edit report" : "Add report"}
        description="One handler's posts and answered queries for a given day."
        size="lg"
        footer={<>
          <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
          <Button form="soc-form" type="submit" loading={creating || updating}>{editing ? "Save" : "Add"}</Button>
        </>}>
        <form id="soc-form" onSubmit={submit} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input label="Date" type="date" value={dateOnly(form.date)}
            onChange={(e) => setForm((f) => ({ ...f, date: e.target.value ? new Date(e.target.value).toISOString() : new Date().toISOString() }))} />
          <Select label="Handler (employee)" value={form.employeeId ?? ""} onChange={(e) => setForm((f) => ({ ...f, employeeId: e.target.value || null }))}>
            <option value="">—</option>
            {(employees ?? []).map((emp) => <option key={emp.id} value={emp.id}>{emp.fullName}</option>)}
          </Select>
          <Input label="Platform" value={form.platform ?? ""} onChange={(e) => setForm((f) => ({ ...f, platform: e.target.value }))} placeholder="e.g. Facebook" />
          <Input label="Posts made" type="number" min={0} value={form.postsMade} onChange={(e) => setForm((f) => ({ ...f, postsMade: e.target.value === "" ? 0 : Number(e.target.value) }))} />
          <Input label="Queries answered" type="number" min={0} value={form.queriesAnswered} onChange={(e) => setForm((f) => ({ ...f, queriesAnswered: e.target.value === "" ? 0 : Number(e.target.value) }))} />
          <Textarea label="Notes" containerClassName="sm:col-span-2" value={form.notes ?? ""} onChange={(e) => setForm((f) => ({ ...f, notes: e.target.value }))} />
        </form>
      </Modal>
    </>
  );
}
