import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import {
  useListEmployeesQuery, useGetEmployeeQuery, useCreateEmployeeMutation,
  useUpdateEmployeeMutation, useDeleteEmployeeMutation, useListCallCentersQuery,
  useSyncEmployeesFromUsersMutation, useUpcomingBirthdaysQuery,
} from "../../shared/api/baseApi";
import type { EmployeeInput } from "../../shared/api/types";
import {
  DESIGNATIONS, GENDERS, MARITAL_STATUSES, EMPLOYMENT_STATUSES, GUARDIAN_RELATIONSHIPS,
  hrLabel, EMPLOYMENT_TONE,
} from "../../shared/constants/hr";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { EmployeeImageField } from "./EmployeeImageField";
import {
  Avatar, Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader, SearchInput,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { HR_MSG } from "./messages";

const BLANK: EmployeeInput = {
  fullName: "", agentCode: "", phoneNumber: "", officialEmail: "",
  designation: "Fronter", callCenterId: null, userId: null,
  cnic: "", gender: "Male", maritalStatus: "Single", dateOfBirth: null,
  guardianPhone: "", guardianRelationship: null,
  currentAddress: "", permanentAddress: "",
  dateOfJoining: null, employmentStatus: "Probation",
  reportingToEmployeeId: null, workHours: "",
  bankName: "", bankAccountTitle: "", bankAccountNumber: "", bankIban: "",
  photoKey: null, idCardFrontKey: null, idCardBackKey: null,
};

const opts = (arr: readonly string[]) =>
  arr.map((v) => <option key={v} value={v}>{hrLabel(v)}</option>);
const dateOnly = (iso: string | null | undefined) => (iso ? iso.slice(0, 10) : "");

/**
 * HR → Employees. The master roster of agent/staff records with the full profile form
 * (identity, personal, guardian, addresses, employment, banking). HR / Admin / SuperAdmin only.
 */
export function EmployeesPage() {
  const [search, setSearch] = useState("");
  const [designation, setDesignation] = useState("");
  const { data: employees, isLoading } = useListEmployeesQuery({
    search: search.trim() || undefined,
    designation: designation || undefined,
  });
  const { data: callCenters } = useListCallCentersQuery();
  const { data: birthdays } = useUpcomingBirthdaysQuery(30);
  const [create, { isLoading: creating }] = useCreateEmployeeMutation();
  const [update, { isLoading: updating }] = useUpdateEmployeeMutation();
  const [remove] = useDeleteEmployeeMutation();
  const [syncFromUsers, { isLoading: syncing }] = useSyncEmployeesFromUsersMutation();
  const toast = useToast();
  const confirm = useConfirm();

  async function importFromUsers() {
    try {
      const r = await syncFromUsers().unwrap();
      toast.success(r.created > 0 ? HR_MSG.imported(r.created) : HR_MSG.alreadyUpToDate,
        r.created > 0 ? HR_MSG.importedDesc : HR_MSG.allUsersHaveRecords);
    } catch (err: unknown) {
      toast.error(HR_MSG.importFailed, getErrorDetail(err) ?? HR_MSG.retry);
    }
  }

  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<EmployeeInput>(BLANK);
  const { data: editingFull, refetch: refetchEmployee } = useGetEmployeeQuery(editingId!, { skip: !editingId });

  // Hydrate the form when the full record for the edited employee arrives.
  useEffect(() => {
    if (!editingId || !editingFull || editingFull.id !== editingId) return;
    setForm({
      fullName: editingFull.fullName, agentCode: editingFull.agentCode,
      phoneNumber: editingFull.phoneNumber ?? "", officialEmail: editingFull.officialEmail ?? "",
      designation: editingFull.designation, callCenterId: editingFull.callCenterId ?? null,
      userId: editingFull.userId ?? null, cnic: editingFull.cnic ?? "",
      gender: editingFull.gender, maritalStatus: editingFull.maritalStatus,
      dateOfBirth: editingFull.dateOfBirth ?? null, guardianPhone: editingFull.guardianPhone ?? "",
      guardianRelationship: editingFull.guardianRelationship ?? null,
      currentAddress: editingFull.currentAddress ?? "", permanentAddress: editingFull.permanentAddress ?? "",
      dateOfJoining: editingFull.dateOfJoining ?? null, employmentStatus: editingFull.employmentStatus,
      reportingToEmployeeId: editingFull.reportingToEmployeeId ?? null, workHours: editingFull.workHours ?? "",
      bankName: editingFull.bankName ?? "", bankAccountTitle: editingFull.bankAccountTitle ?? "",
      bankAccountNumber: editingFull.bankAccountNumber ?? "", bankIban: editingFull.bankIban ?? "",
      photoKey: editingFull.photoKey ?? null, idCardFrontKey: editingFull.idCardFrontKey ?? null,
      idCardBackKey: editingFull.idCardBackKey ?? null,
    });
  }, [editingId, editingFull]);

  function openAdd() { setEditingId(null); setForm(BLANK); setOpen(true); }
  function openEdit(id: string) { setEditingId(id); setForm(BLANK); setOpen(true); }
  // Reset editingId on close so reopening the SAME employee re-fires the hydrate effect (otherwise the
  // effect deps are unchanged, the form stays BLANK, and a Save would wipe the record with blanks).
  function closeModal() { setOpen(false); setEditingId(null); setForm(BLANK); }

  // True while an edit's full record hasn't loaded yet — the form isn't safe to save (would send blanks).
  const hydrating = !!editingId && editingFull?.id !== editingId;

  const set = (k: keyof EmployeeInput) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) =>
      setForm((f) => ({ ...f, [k]: e.target.value }));
  const setDate = (k: keyof EmployeeInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({ ...f, [k]: e.target.value ? new Date(e.target.value).toISOString() : null }));

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (hydrating) return;   // guard: never save before the edited record has loaded
    const payload: EmployeeInput = { ...form, callCenterId: form.callCenterId || null,
      reportingToEmployeeId: form.reportingToEmployeeId || null };
    try {
      if (editingId) { await update({ id: editingId, ...payload }).unwrap(); toast.success(HR_MSG.employeeUpdated); }
      else { await create(payload).unwrap(); toast.success(HR_MSG.employeeAdded); }
      closeModal();
    } catch (err: unknown) {
      toast.error(HR_MSG.saveFailed, getErrorDetail(err) ?? HR_MSG.checkRequiredFields);
    }
  }

  async function onDelete(id: string, name: string) {
    if (!(await confirm({ title: HR_MSG.removeEmployeeTitle, description: HR_MSG.removeEmployeeDesc(name), confirmLabel: HR_MSG.removeLabel, danger: true }))) return;
    try { await remove(id).unwrap(); toast.success(HR_MSG.employeeRemoved); }
    catch (err: unknown) { toast.error(HR_MSG.removeFailed, getErrorDetail(err) ?? HR_MSG.retry); }
  }

  const rows = employees ?? [];
  const managerOptions = rows.filter((r) => r.id !== editingId);
  const sel = useRowSelection(rows.map((r) => r.id));
  const [bulkDeleting, setBulkDeleting] = useState(false);

  function exportSelected() {
    const chosen = rows.filter((r) => sel.isSelected(r.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (r) => r.fullName },
      { header: "Agent ID", value: (r) => r.agentCode },
      { header: "Designation", value: (r) => hrLabel(r.designation) },
      { header: "Call centre", value: (r) => r.callCenterName ?? "Agency-wide" },
      { header: "Phone", value: (r) => r.phoneNumber ?? "" },
      { header: "Email", value: (r) => r.officialEmail ?? "" },
    ], `employees-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(HR_MSG.exportReady, HR_MSG.rowsDownloaded(chosen.length));
  }

  async function bulkDelete() {
    const ids = sel.selectedIds;
    if (ids.length === 0) return;
    if (!(await confirm({
      title: HR_MSG.removeEmployeesTitle(ids.length),
      description: HR_MSG.removeEmployeesDesc,
      confirmLabel: HR_MSG.removeLabel, danger: true,
    }))) return;
    setBulkDeleting(true);
    try {
      await Promise.all(ids.map((id) => remove(id).unwrap()));
      toast.success(HR_MSG.removedEmployees(ids.length));
      sel.clear();
    } catch (err: unknown) { toast.error(HR_MSG.removeFailed, getErrorDetail(err) ?? HR_MSG.retry); }
    finally { setBulkDeleting(false); }
  }

  return (
    <>
      <PageHeader
        eyebrow="Human Resources"
        title="Employees"
        description="The master roster of agents and staff — identity, employment, and banking details."
        actions={
          <>
            <Button variant="outline" leftIcon={<Icon name="download" size={15} />} loading={syncing} onClick={importFromUsers}>
              Import from users
            </Button>
            <Button leftIcon={<Icon name="userPlus" size={15} />} onClick={openAdd}>Add employee</Button>
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-5">
        <Stat label="Employees" value={rows.length} icon={<Icon name="users" size={16} />} tone="brand" />
        <Stat label="Permanent" value={rows.filter((r) => r.employmentStatus === "Permanent").length} hint="Confirmed staff who have passed probation" icon={<Icon name="userCheck" size={16} />} tone="success" />
        <Stat label="Probation" value={rows.filter((r) => r.employmentStatus === "Probation").length} hint="New hires still under review before being confirmed" icon={<Icon name="clock" size={16} />} tone="warning" />
        <Stat label="Call centres" value={callCenters?.length ?? 0} icon={<Icon name="building" size={16} />} tone="accent" />
      </div>

      {birthdays && birthdays.length > 0 && (
        <Card className="mb-4">
          <CardBody>
            <div className="flex items-center gap-2 mb-2.5">
              <Icon name="calendar" size={15} className="text-brand-500" />
              <h3 className="text-sm font-semibold text-ink-900">Upcoming birthdays</h3>
              <InfoHint title="Birthdays" side="right">Refreshed once a day. A birthday reminder is automatically sent to each employee on the day.</InfoHint>
            </div>
            <div className="flex flex-wrap gap-2">
              {birthdays.map((b) => (
                <Badge key={b.employeeId} tone={b.isToday ? "success" : "neutral"} variant="soft">
                  {b.isToday ? "🎉 " : ""}{b.fullName} · {b.isToday ? "today" : b.daysUntil === 1 ? "tomorrow" : `in ${b.daysUntil} days`}
                </Badge>
              ))}
            </div>
          </CardBody>
        </Card>
      )}

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[220px]">
            <SearchInput value={search} onChange={setSearch} placeholder="Search name, agent ID, phone, email…" />
          </div>
          <Select aria-label="Filter by designation" value={designation} onChange={(e) => setDesignation(e.target.value)} className="w-48">
            <option value="">All designations</option>
            {opts(DESIGNATIONS)}
          </Select>
          {employees && <Badge tone="neutral" variant="soft" className="tabular-nums whitespace-nowrap">{rows.length} total</Badge>}
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : rows.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="users" size={20} />} title={search || designation ? HR_MSG.noMatchesTitle : HR_MSG.employeesEmptyTitle}
            description={search || designation ? HR_MSG.noEmployeeMatchesDesc : HR_MSG.employeesEmptyDesc}
            action={!search && !designation ? (
              <div className="flex items-center gap-2">
                <Button variant="outline" leftIcon={<Icon name="download" size={15} />} loading={syncing} onClick={importFromUsers}>Import from users</Button>
                <Button leftIcon={<Icon name="userPlus" size={15} />} onClick={openAdd}>Add employee</Button>
              </div>
            ) : undefined} />
        </CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead>
              <TR>
                <TH className="w-10"><Checkbox aria-label="Select all employees" {...sel.allCheckboxProps} /></TH>
                <TH>Employee</TH>
                <TH><span className="inline-flex items-center gap-1">Agent ID<InfoHint title="Agent ID" side="top">The employee's unique staff code (e.g. EMP-014), used to identify them across the CRM.</InfoHint></span></TH>
                <TH>Designation</TH>
                <TH><span className="inline-flex items-center gap-1">Call centre<InfoHint title="Call centre" side="top">The centre this employee works at; "Agency-wide" means they aren't tied to a single call centre.</InfoHint></span></TH>
                <TH><span className="inline-flex items-center gap-1">Status<InfoHint title="Employment status" side="top">The employee's stage — e.g. Probation (new hire under review) or Permanent (confirmed on staff).</InfoHint></span></TH>
                <TH>Joined</TH>
                <TH className="text-right">Actions</TH>
              </TR>
            </THead>
            <TBody>
              {rows.map((e) => (
                <TR key={e.id} className={sel.isSelected(e.id) ? "bg-brand-50/40" : undefined}>
                  <TD>
                    <Checkbox aria-label={`Select ${e.fullName}`} {...sel.checkboxProps(e.id)} />
                  </TD>
                  <TD>
                    <div className="flex items-center gap-2.5 min-w-0">
                      <Avatar name={e.fullName} size={30} />
                      <div className="leading-tight min-w-0">
                        <div className="font-medium text-ink-900 truncate">{e.fullName}</div>
                        <div className="text-xs text-ink-500 truncate">{e.officialEmail || e.phoneNumber || "—"}</div>
                      </div>
                    </div>
                  </TD>
                  <TD className="font-mono text-xs text-ink-600">{e.agentCode}</TD>
                  <TD><Badge tone="neutral" variant="soft">{hrLabel(e.designation)}</Badge></TD>
                  <TD className="text-ink-600">{e.callCenterName ?? <span className="text-ink-400">Agency-wide</span>}</TD>
                  <TD><Badge tone={EMPLOYMENT_TONE[e.employmentStatus] ?? "neutral"} variant="soft">{hrLabel(e.employmentStatus)}</Badge></TD>
                  <TD className="text-ink-600 tabular-nums">{e.dateOfJoining ? dateOnly(e.dateOfJoining) : <span className="text-ink-400">—</span>}</TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => openEdit(e.id)}>Edit</Button>
                      <Button variant="ghost" size="sm" aria-label={`Remove ${e.fullName}`} title="Remove employee" onClick={() => onDelete(e.id, e.fullName)}>
                        <Icon name="trash" size={14} className="text-rose-500" />
                      </Button>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
          <BulkActionBar
            count={sel.selectedCount} itemNoun="employee" onClear={sel.clear}
            actions={[
              { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
              { key: "delete", label: "Remove", icon: "trash", onClick: bulkDelete, danger: true, loading: bulkDeleting },
            ]}
          />
        </div>
      )}

      <Modal open={open} onClose={closeModal}
        title={editingId ? "Edit employee" : "Add employee"}
        description="CNIC and bank account number are encrypted at rest."
        size="xl"
        footer={<>
          <Button variant="ghost" onClick={closeModal}>Cancel</Button>
          <Button form="emp-form" type="submit" loading={creating || updating} disabled={hydrating}>{editingId ? "Save" : "Add employee"}</Button>
        </>}>
        <form id="emp-form" onSubmit={submit} className="space-y-5">
          <Section title="Identity">
            <Input label="Full name" required value={form.fullName} onChange={set("fullName")} />
            <Input label="Agent ID" required value={form.agentCode} onChange={set("agentCode")} placeholder="e.g. EMP-014" />
            <Select label="Designation" value={form.designation} onChange={set("designation")}>{opts(DESIGNATIONS)}</Select>
            <Select label="Call centre" value={form.callCenterId ?? ""} onChange={set("callCenterId")}>
              <option value="">Agency-wide (none)</option>
              {(callCenters ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
            <Input label="Phone number" value={form.phoneNumber ?? ""} onChange={set("phoneNumber")} />
            <Input label="Official email" type="email" value={form.officialEmail ?? ""} onChange={set("officialEmail")} />
          </Section>

          <Section title={<>Personal<InfoHint title="CNIC" side="right">The national ID number, encrypted at rest and visible only to authorised HR.</InfoHint></>}>
            <Input label="CNIC" value={form.cnic ?? ""} onChange={set("cnic")} secure placeholder="e.g. 35202-1234567-1" />
            <Select label="Gender" value={form.gender} onChange={set("gender")}>{opts(GENDERS)}</Select>
            <Select label="Marital status" value={form.maritalStatus} onChange={set("maritalStatus")}>{opts(MARITAL_STATUSES)}</Select>
            <Input label="Date of birth" type="date" value={dateOnly(form.dateOfBirth)} onChange={setDate("dateOfBirth")} />
          </Section>

          <Section title="Guardian / emergency contact">
            <Input label="Guardian phone" value={form.guardianPhone ?? ""} onChange={set("guardianPhone")} />
            <Select label="Guardian relationship" value={form.guardianRelationship ?? ""} onChange={set("guardianRelationship")}>
              <option value="">—</option>
              {opts(GUARDIAN_RELATIONSHIPS)}
            </Select>
          </Section>

          <Section title="Addresses">
            <Textarea label="Current address" containerClassName="sm:col-span-2" value={form.currentAddress ?? ""} onChange={set("currentAddress")} />
            <Textarea label="Permanent address" containerClassName="sm:col-span-2" value={form.permanentAddress ?? ""} onChange={set("permanentAddress")} />
          </Section>

          <Section title={<>Employment<InfoHint title="Employment" side="right">Probation is a new hire under review, Permanent is confirmed staff; "Reporting to" is this person's line manager.</InfoHint></>}>
            <Input label="Date of joining" type="date" value={dateOnly(form.dateOfJoining)} onChange={setDate("dateOfJoining")} />
            <Select label="Employment status" value={form.employmentStatus} onChange={set("employmentStatus")}>{opts(EMPLOYMENT_STATUSES)}</Select>
            <Select label="Reporting to" value={form.reportingToEmployeeId ?? ""} onChange={set("reportingToEmployeeId")}>
              <option value="">—</option>
              {managerOptions.map((m) => <option key={m.id} value={m.id}>{m.fullName} ({m.agentCode})</option>)}
            </Select>
            <Input label="Work hours" value={form.workHours ?? ""} onChange={set("workHours")} placeholder="e.g. 9:00 AM – 6:00 PM" />
          </Section>

          <Section title={<>Banking<InfoHint title="Bank details" side="right">Used to pay salary; the account number is encrypted at rest, like the CNIC.</InfoHint></>}>
            <Input label="Bank name" value={form.bankName ?? ""} onChange={set("bankName")} />
            <Input label="Account title" value={form.bankAccountTitle ?? ""} onChange={set("bankAccountTitle")} />
            <Input label="Account number" value={form.bankAccountNumber ?? ""} onChange={set("bankAccountNumber")} secure />
            <Input label="IBAN" value={form.bankIban ?? ""} onChange={set("bankIban")} />
          </Section>

          <Section title="Documents">
            {editingId && editingFull ? (
              <>
                <EmployeeImageField employeeId={editingId} kind="Photo" label="Photo" hasImage={!!editingFull.photoKey} onChanged={refetchEmployee} />
                <EmployeeImageField employeeId={editingId} kind="IdCardFront" label="ID card — front" hasImage={!!editingFull.idCardFrontKey} onChanged={refetchEmployee} />
                <EmployeeImageField employeeId={editingId} kind="IdCardBack" label="ID card — back" hasImage={!!editingFull.idCardBackKey} onChanged={refetchEmployee} />
              </>
            ) : (
              <div className="sm:col-span-2 text-xs text-ink-400">Save the employee first, then reopen to upload their photo and ID card.</div>
            )}
          </Section>
        </form>
      </Modal>
    </>
  );
}

function Section({ title, children }: { title: React.ReactNode; children: React.ReactNode }) {
  return (
    <div>
      <div className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500 mb-2 pb-1 border-b hairline">{title}</div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">{children}</div>
    </div>
  );
}
