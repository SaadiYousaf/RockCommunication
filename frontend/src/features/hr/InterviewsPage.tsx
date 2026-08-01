import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import {
  useListInterviewsQuery, useCreateInterviewMutation, useUpdateInterviewMutation, useDeleteInterviewMutation,
  useUpcomingTrainingsQuery,
} from "../../shared/api/baseApi";
import type { Interview, InterviewInput } from "../../shared/api/types";
import { OFFER_STATUSES, OFFER_TONE, hrLabel } from "../../shared/constants/hr";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, InfoHint, Input, Modal, PageHeader, SearchInput,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";

const BLANK: InterviewInput = {
  interviewDate: null, candidateName: "", cnic: "", phoneNumber: "",
  positionAppliedFor: "", experience: "", expectedSalary: null, ableToJoinOn: null,
  status: "Applied", positionOffered: "", salaryOffered: null, remarks: "", trainingScheduledAt: null,
};

const money = (n: number | null | undefined) =>
  n == null ? "—" : n.toLocaleString(undefined, { style: "currency", currency: "USD", maximumFractionDigits: 0 });
const dateOnly = (iso: string | null | undefined) => (iso ? iso.slice(0, 10) : "");
const dateTimeLocal = (iso: string | null | undefined) => (iso ? iso.slice(0, 16) : "");

/** Whole calendar days from today (local) to the given instant; negative if past. */
function daysUntil(iso: string): number {
  const start = new Date(); start.setHours(0, 0, 0, 0);
  const target = new Date(iso); target.setHours(0, 0, 0, 0);
  return Math.round((target.getTime() - start.getTime()) / 86_400_000);
}
const whenLabel = (d: number) => (d <= 0 ? "today" : d === 1 ? "tomorrow" : `in ${d} days`);

/** HR → Interviews. Recruitment pipeline: candidates, offers, and scheduled training. */
export function InterviewsPage() {
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const { data: interviews, isLoading } = useListInterviewsQuery({ search: search.trim() || undefined, status: status || undefined });
  const [create, { isLoading: creating }] = useCreateInterviewMutation();
  const [update, { isLoading: updating }] = useUpdateInterviewMutation();
  const [remove] = useDeleteInterviewMutation();
  const { data: trainings } = useUpcomingTrainingsQuery(14);
  const toast = useToast();
  const confirm = useConfirm();

  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<Interview | null>(null);
  const [form, setForm] = useState<InterviewInput>(BLANK);

  useEffect(() => {
    if (!editing) return;
    setForm({
      interviewDate: editing.interviewDate ?? null, candidateName: editing.candidateName,
      cnic: editing.cnic ?? "", phoneNumber: editing.phoneNumber ?? "",
      positionAppliedFor: editing.positionAppliedFor ?? "", experience: editing.experience ?? "",
      expectedSalary: editing.expectedSalary ?? null, ableToJoinOn: editing.ableToJoinOn ?? null,
      status: editing.status, positionOffered: editing.positionOffered ?? "",
      salaryOffered: editing.salaryOffered ?? null, remarks: editing.remarks ?? "",
      trainingScheduledAt: editing.trainingScheduledAt ?? null,
    });
  }, [editing]);

  function openAdd() { setEditing(null); setForm(BLANK); setOpen(true); }
  function openEdit(i: Interview) { setEditing(i); setOpen(true); }

  const set = (k: keyof InterviewInput) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) =>
      setForm((f) => ({ ...f, [k]: e.target.value }));
  const setNum = (k: keyof InterviewInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({ ...f, [k]: e.target.value === "" ? null : Number(e.target.value) }));
  const setDate = (k: keyof InterviewInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({ ...f, [k]: e.target.value ? new Date(e.target.value).toISOString() : null }));

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editing) { await update({ id: editing.id, ...form }).unwrap(); toast.success("Interview updated"); }
      else { await create(form).unwrap(); toast.success("Interview added"); }
      setOpen(false);
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Check the required fields and try again.");
    }
  }

  async function onDelete(i: Interview) {
    if (!(await confirm({ title: "Delete interview?", description: `Remove ${i.candidateName}'s record?`, confirmLabel: "Delete", danger: true }))) return;
    try { await remove(i.id).unwrap(); toast.success("Interview removed"); }
    catch (err: unknown) { toast.error("Couldn't remove", getErrorDetail(err) ?? "Try again."); }
  }

  const rows = interviews ?? [];
  const hired = rows.filter((r) => r.status === "Hired").length;
  const offered = rows.filter((r) => r.status === "Offered").length;

  return (
    <>
      <PageHeader eyebrow="Human Resources" title="Interviews"
        description="Track candidates through the hiring pipeline — offers, salaries, and scheduled training."
        actions={<Button leftIcon={<Icon name="userPlus" size={15} />} onClick={openAdd}>New interview</Button>} />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-5">
        <Stat label="Candidates" value={rows.length} icon={<Icon name="users" size={16} />} tone="brand" />
        <Stat label="Offered" value={offered} icon={<Icon name="mail" size={16} />} tone="accent" />
        <Stat label="Hired" value={hired} icon={<Icon name="userCheck" size={16} />} tone="success" />
        <Stat label="In pipeline" value={rows.filter((r) => !["Hired", "Rejected", "Declined"].includes(r.status)).length} hint="Candidates still active — not yet hired, rejected, or declined" icon={<Icon name="clock" size={16} />} tone="warning" />
      </div>

      {trainings && trainings.length > 0 && (
        <Card className="mb-4">
          <CardBody>
            <div className="flex items-center gap-2 mb-2.5">
              <Icon name="calendar" size={15} className="text-brand-500" />
              <h3 className="text-sm font-semibold text-ink-900">Upcoming trainings</h3>
              <InfoHint title="Upcoming trainings" side="right">Candidates with a training booked in the next 14 days. Refreshed daily — HR &amp; admins get an automatic reminder the day before each training.</InfoHint>
            </div>
            <div className="flex flex-wrap gap-2">
              {trainings.map((t) => {
                const d = daysUntil(t.trainingScheduledAt);
                return (
                  <Badge key={t.id} tone={d <= 1 ? "success" : "neutral"} variant="soft">
                    {d <= 1 ? "🎓 " : ""}{t.candidateName}{t.positionOffered ? ` · ${t.positionOffered}` : ""} · {whenLabel(d)}
                  </Badge>
                );
              })}
            </div>
          </CardBody>
        </Card>
      )}

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[220px]"><SearchInput value={search} onChange={setSearch} placeholder="Search candidate, phone, position…" /></div>
          <Select aria-label="Filter by status" value={status} onChange={(e) => setStatus(e.target.value)} className="w-44">
            <option value="">All statuses</option>
            {OFFER_STATUSES.map((s) => <option key={s} value={s}>{hrLabel(s)}</option>)}
          </Select>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : rows.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="users" size={20} />} title={search || status ? "No matches" : "No interviews yet"}
            description={search || status ? "No candidate matches your filters." : "Add a candidate to start tracking the hiring pipeline."}
            action={!search && !status ? <Button leftIcon={<Icon name="userPlus" size={15} />} onClick={openAdd}>New interview</Button> : undefined} />
        </CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
              <TH>Candidate</TH><TH>Position</TH><TH>Interview</TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Expected
                  <InfoHint title="Expected salary" side="top">The salary the candidate is asking to be paid.</InfoHint>
                </span>
              </TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Offered
                  <InfoHint title="Salary offered" side="top">The pay in the offer extended to the candidate.</InfoHint>
                </span>
              </TH>
              <TH>
                <span className="inline-flex items-center gap-1">Training
                  <InfoHint title="Training scheduled" side="top">When the new hire's onboarding/training session is booked.</InfoHint>
                </span>
              </TH>
              <TH>
                <span className="inline-flex items-center gap-1">Status
                  <InfoHint title="Offer pipeline" side="top">Applied → Interviewed → Offered → Accepted → Hired; or Declined, Rejected, or On hold.</InfoHint>
                </span>
              </TH>
              <TH className="text-right">Actions</TH>
            </TR></THead>
            <TBody>
              {rows.map((i) => (
                <TR key={i.id}>
                  <TD>
                    <div className="font-medium text-ink-900">{i.candidateName}</div>
                    <div className="text-xs text-ink-500">{i.phoneNumber || "—"}</div>
                  </TD>
                  <TD className="text-ink-600">{i.positionAppliedFor || "—"}</TD>
                  <TD className="text-ink-600 tabular-nums">{i.interviewDate ? dateOnly(i.interviewDate) : <span className="text-ink-400">—</span>}</TD>
                  <TD numeric className="tabular-nums text-ink-600">{money(i.expectedSalary)}</TD>
                  <TD numeric className="tabular-nums text-ink-900">{money(i.salaryOffered)}</TD>
                  <TD className="text-ink-600 tabular-nums">{i.trainingScheduledAt ? new Date(i.trainingScheduledAt).toLocaleString() : <span className="text-ink-400">—</span>}</TD>
                  <TD><Badge tone={OFFER_TONE[i.status] ?? "neutral"} variant="soft">{hrLabel(i.status)}</Badge></TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => openEdit(i)}>Edit</Button>
                      <Button variant="ghost" size="sm" aria-label={`Delete ${i.candidateName}`} title="Delete interview" onClick={() => onDelete(i)}><Icon name="trash" size={14} className="text-rose-500" /></Button>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
      )}

      <Modal open={open} onClose={() => setOpen(false)}
        title={editing ? "Edit interview" : "New interview"}
        description="CNIC is encrypted at rest."
        size="xl"
        footer={<>
          <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
          <Button form="int-form" type="submit" loading={creating || updating}>{editing ? "Save" : "Add"}</Button>
        </>}>
        <form id="int-form" onSubmit={submit} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input label="Candidate name" required value={form.candidateName} onChange={set("candidateName")} />
          <Input label="Phone number" value={form.phoneNumber ?? ""} onChange={set("phoneNumber")} />
          <Input label="CNIC" value={form.cnic ?? ""} onChange={set("cnic")} secure placeholder="e.g. 35202-1234567-1" />
          <Input label="Interview date" type="date" value={dateOnly(form.interviewDate)} onChange={setDate("interviewDate")} />
          <Input label="Position applied for" value={form.positionAppliedFor ?? ""} onChange={set("positionAppliedFor")} />
          <Input label="Experience" value={form.experience ?? ""} onChange={set("experience")} placeholder="e.g. 3 years — outbound sales" />
          <Input label="Expected salary" type="number" min={0} value={form.expectedSalary ?? ""} onChange={setNum("expectedSalary")} />
          <Input label="Able to join on" type="date" value={dateOnly(form.ableToJoinOn)} onChange={setDate("ableToJoinOn")} />
          <Select label="Offer status" value={form.status} onChange={set("status")}>
            {OFFER_STATUSES.map((s) => <option key={s} value={s}>{hrLabel(s)}</option>)}
          </Select>
          <Input label="Position offered" value={form.positionOffered ?? ""} onChange={set("positionOffered")} />
          <Input label="Salary offered" type="number" min={0} value={form.salaryOffered ?? ""} onChange={setNum("salaryOffered")} />
          <Input label="Training scheduled at" type="datetime-local" value={dateTimeLocal(form.trainingScheduledAt)} onChange={setDate("trainingScheduledAt")} />
          <Textarea label="Remarks" containerClassName="sm:col-span-2" value={form.remarks ?? ""} onChange={set("remarks")} placeholder="Interview notes — strengths, concerns, next steps…" />
        </form>
      </Modal>
    </>
  );
}
