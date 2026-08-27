import { VERIFIER_STATUSES as STATUSES, MARITAL_STATUSES as MARITAL, verifierStatusLabel } from "../../shared/constants/intake";
import { getErrorDetail } from "../../shared/api/apiError";
import { formatPhone } from "../../shared/lib/format";
import { useEffect, useState } from "react";
import {
  useVerifierQueueQuery, useSetVerifierStatusMutation,
  useGetVerifyLeadQuery, useUpdateVerifyLeadMutation,
} from "../../shared/api/baseApi";
import type { IntakeQueueItem, VerifierStatusValue } from "../../shared/api/types";
import { timeAgoShort, waitTone } from "../../shared/lib/time";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader, SearchInput, Select,
  Skeleton, Stat, Stepper, Table, TBody, TD, TH, THead, TR, useToast, Pager, usePagination,} from "../../shared/ui";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection, type RowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { INTAKE_PIPELINE, PIPELINE_STEP } from "../../shared/constants/pipeline";
import { INTAKE_MSG } from "./messages";


/** Verifier work queue — fronted leads awaiting a verification status. */
export function VerifyQueuePage() {
  // Poll: leads land here from other users' actions (fronters capturing), so refresh without a reload.
  const { data: queue, isLoading } = useVerifierQueueQuery(undefined, { pollingInterval: 30_000 });
  const [editingId, setEditingId] = useState<string | null>(null);
  const [q, setQ] = useState("");
  const filtered = (queue ?? []).filter((l) =>
    !q.trim() || `${l.firstName} ${l.lastName} ${l.phoneNumber} ${l.city ?? ""} ${l.state ?? ""} ${l.email ?? ""}`.toLowerCase().includes(q.trim().toLowerCase()));
  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: {
      name: (l) => `${l.firstName} ${l.lastName}`,
      location: (l) => [l.city, l.state].filter(Boolean).join(", "),
    },
  });

  // Presentational paging over the final (filtered + sorted) queue.
  const pg = usePagination(sorted);
  const total = queue?.length ?? 0;
  const stale = (queue ?? []).filter((l) => waitTone(l.createdAt) === "danger").length;
  const topScore = total ? Math.max(...(queue ?? []).map((l) => l.score)) : 0;
  const toast = useToast();
  const sel = useRowSelection(sorted.map((l) => l.id));

  function exportSelected() {
    const chosen = sorted.filter((l) => sel.isSelected(l.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (l) => `${l.firstName} ${l.lastName}` },
      { header: "Phone", value: (l) => formatPhone(l.phoneNumber) },
      { header: "Status", value: (l) => verifierStatusLabel(l.verifierStatus) },
      { header: "Date", value: (l) => new Date(l.createdAt).toLocaleDateString() },
    ], `verifier-queue-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(INTAKE_MSG.exportReadyTitle, INTAKE_MSG.exportRows(chosen.length));
  }

  return (
    <>
      <PageHeader eyebrow="Verifier" title="Verifier Queue" description="Leads captured by fronters. Open one to review or correct it, then set a status — 'Verified' sends it to the closer queue." />
      <Stepper steps={INTAKE_PIPELINE} currentIndex={PIPELINE_STEP.verify} className="mb-5 max-w-2xl" />
      {queue && total > 0 && (
        <div className="grid grid-cols-2 md:grid-cols-3 gap-4 mb-5 stagger-children">
          <Stat className="stagger-item" label="In queue" value={total} icon={<Icon name="inbox" size={16} />} tone="brand" hint="Awaiting verification" />
          <Stat className="stagger-item" label="Going stale" value={stale} icon={<Icon name="clock" size={16} />} tone="danger" hint="Waiting 24h or more" />
          <Stat className="stagger-item" label="Top priority" value={Math.round(topScore)} icon={<Icon name="zap" size={16} />} tone="accent" hint="Highest score in queue" />
        </div>
      )}
      <Card>
        <CardHeader title="Awaiting verification" subtitle={queue ? <span className="tabular-nums">{filtered.length} of {queue.length} {queue.length === 1 ? "lead" : "leads"}</span> : undefined}
          action={<SearchInput value={q} onChange={setQ} placeholder={INTAKE_MSG.queueSearchPlaceholder} className="w-56" />} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !filtered || filtered.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title={INTAKE_MSG.verifyEmptyTitle} description={q ? INTAKE_MSG.noMatches : INTAKE_MSG.verifyEmptyDesc} />
          ) : (
            <>
            <Table>
              <THead>
                <TR>
                  <TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH>
                  <TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Name</TH><TH sortDir={dirFor("phoneNumber")} onClick={() => toggle("phoneNumber")}>Phone</TH><TH sortDir={dirFor("location")} onClick={() => toggle("location")}>Location</TH><TH sortDir={dirFor("ageYears")} onClick={() => toggle("ageYears")}>Age</TH>
                  <TH sortDir={dirFor("createdAt")} onClick={() => toggle("createdAt")}>
                    <span className="inline-flex items-center gap-1">Waiting
                      <InfoHint title="Waiting time" side="bottom">How long this lead has sat in the queue — red means it's going stale. Work the oldest first.</InfoHint>
                    </span>
                  </TH>
                  <TH sortDir={dirFor("score")} onClick={() => toggle("score")}>
                    <span className="inline-flex items-center gap-1">Priority
                      <InfoHint title="Lead priority score" side="bottom">The lead's likelihood-to-convert score — higher is hotter.</InfoHint>
                    </span>
                  </TH>
                  <TH className="sticky right-0 bg-ink-50 border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
                    <span className="inline-flex items-center gap-1">
                      Verifier status
                      <InfoHint title="Verifier statuses" side="bottom">
                        The outcome of verifying a fronted lead. 'Verified' promotes it to the closer queue; Not interested / DNC / Busy / Call Back / Dead Air keep it out of the closer queue.
                      </InfoHint>
                    </span>
                  </TH>
                </TR>
              </THead>
              <TBody>
                {pg.pageItems.map((l) => <VerifyRow key={l.id} lead={l} onEdit={() => setEditingId(l.id)} selected={sel.isSelected(l.id)} checkboxProps={sel.checkboxProps(l.id)} />)}
              </TBody>
            </Table>
            <Pager {...pg} onPage={pg.setPage} unit="leads" />
            </>
          )}
          <BulkActionBar count={sel.selectedCount} itemNoun="lead" onClear={sel.clear}
            actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]} />
        </CardBody>
      </Card>
      {editingId && <EditLeadModal leadId={editingId} onClose={() => setEditingId(null)} />}
    </>
  );
}

function VerifyRow({ lead, onEdit, selected, checkboxProps }: {
  lead: IntakeQueueItem; onEdit: () => void;
  selected: boolean; checkboxProps: ReturnType<RowSelection["checkboxProps"]>;
}) {
  const [setStatus, { isLoading }] = useSetVerifierStatusMutation();
  const toast = useToast();
  const [status, setStatusVal] = useState<VerifierStatusValue | "">("");

  async function apply() {
    if (!status) { toast.error(INTAKE_MSG.pickStatus); return; }
    try {
      const r = await setStatus({ leadId: lead.id, status }).unwrap();
      toast.success(INTAKE_MSG.statusSavedTitle, r.status === "Verified" ? INTAKE_MSG.leadSentToCloser : INTAKE_MSG.marked(verifierStatusLabel(r.status)));
    } catch (err: unknown) {
      toast.error(INTAKE_MSG.saveFailedTitle, getErrorDetail(err) ?? INTAKE_MSG.retry);
    }
  }

  return (
    <TR className={selected ? "bg-brand-50/40" : undefined}>
      <TD><Checkbox aria-label={`Select ${lead.firstName} ${lead.lastName}`} {...checkboxProps} /></TD>
      <TD className="font-medium text-ink-900 whitespace-nowrap">{lead.firstName} {lead.lastName}</TD>
      <TD className="font-mono text-xs whitespace-nowrap tabular-nums">{formatPhone(lead.phoneNumber)}</TD>
      <TD className="text-sm text-ink-600 max-w-[14rem] truncate">{[lead.city, lead.state].filter(Boolean).join(", ") || "—"}</TD>
      <TD className="text-sm tabular-nums">{lead.ageYears ?? "—"}</TD>
      <TD className="whitespace-nowrap">
        <span title={new Date(lead.createdAt).toLocaleString()}>
          <Badge tone={waitTone(lead.createdAt)} variant="soft">{timeAgoShort(lead.createdAt)}</Badge>
        </span>
      </TD>
      <TD className="text-sm tabular-nums font-medium text-ink-800">{Math.round(lead.score)}</TD>
      <TD className="sticky right-0 bg-white border-l hairline shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
        <div className="flex items-center gap-2">
          <Button size="sm" variant="ghost" leftIcon={<Icon name="edit" size={14} />} onClick={onEdit}>Open</Button>
          <Select aria-label={`Set verifier status for ${lead.firstName} ${lead.lastName}`} title="Set the verification outcome for this lead" className="h-9 w-40 text-sm" value={status} onChange={(e) => setStatusVal(e.target.value as VerifierStatusValue)}>
            <option value="">Select…</option>
            {STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </Select>
          <Button size="sm" loading={isLoading} leftIcon={<Icon name="save" size={14} />} onClick={apply}>Save</Button>
          {lead.verifierStatus !== "None" && <Badge tone="neutral" variant="soft" className="whitespace-nowrap">{verifierStatusLabel(lead.verifierStatus)}</Badge>}
        </div>
      </TD>
    </TR>
  );
}


/** Verifier opens a lead to review / correct its intake details. Typing only. */
function EditLeadModal({ leadId, onClose }: { leadId: string; onClose: () => void }) {
  const { data, isLoading } = useGetVerifyLeadQuery(leadId);
  const [save, { isLoading: saving }] = useUpdateVerifyLeadMutation();
  const toast = useToast();

  const [f, setF] = useState({
    firstName: "", lastName: "", maritalStatus: "", streetAddress: "", city: "", state: "",
    zipcode: "", phoneNumber: "", birthDate: "", ageYears: "", email: "", jornayaLeadId: "",
  });
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setF((p) => ({ ...p, [k]: e.target.value }));

  useEffect(() => {
    if (!data) return;
    setF({
      firstName: data.firstName ?? "", lastName: data.lastName ?? "", maritalStatus: data.maritalStatus ?? "",
      streetAddress: data.address ?? "", city: data.city ?? "", state: data.state ?? "",
      zipcode: data.postalCode ?? "", phoneNumber: data.phoneNumber ?? "",
      birthDate: data.dateOfBirth?.slice(0, 10) ?? "", ageYears: data.ageYears != null ? String(data.ageYears) : "",
      email: data.email ?? "", jornayaLeadId: data.jornayaLeadId ?? "",
    });
  }, [data]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await save({
        leadId,
        firstName: f.firstName, lastName: f.lastName, maritalStatus: f.maritalStatus || undefined,
        streetAddress: f.streetAddress || undefined, city: f.city || undefined, state: f.state || undefined,
        zipcode: f.zipcode || undefined, phoneNumber: f.phoneNumber,
        birthDate: f.birthDate ? new Date(f.birthDate).toISOString() : undefined,
        ageYears: f.ageYears ? parseInt(f.ageYears, 10) : undefined,
        email: f.email || undefined, jornayaLeadId: f.jornayaLeadId || undefined,
      }).unwrap();
      toast.success(INTAKE_MSG.leadUpdatedTitle);
      onClose();
    } catch (err: unknown) {
      toast.error(INTAKE_MSG.saveFailedTitle, getErrorDetail(err) ?? INTAKE_MSG.checkFieldsAndTryAgain);
    }
  }

  return (
    <Modal open onClose={onClose} title="Edit lead" description="Correct the intake details captured by the fronter (typing only)." size="lg">
      {isLoading ? <Skeleton className="h-64" /> : (
        <form onSubmit={submit} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input label="First name" required secure value={f.firstName} onChange={set("firstName")} />
          <Input label="Last name" required secure value={f.lastName} onChange={set("lastName")} />
          <Select label="Marital status" value={f.maritalStatus} onChange={set("maritalStatus")}>
            <option value="">Select…</option>
            {MARITAL.map((m) => <option key={m} value={m}>{m}</option>)}
          </Select>
          <Input label="Phone number" required secure leftIcon={<Icon name="phone" size={14} />} value={f.phoneNumber} onChange={set("phoneNumber")} />
          <Input label="Street address" secure containerClassName="sm:col-span-2" value={f.streetAddress} onChange={set("streetAddress")} />
          <Input label="City" secure value={f.city} onChange={set("city")} />
          <Input label="State" secure value={f.state} onChange={set("state")} />
          <Input label="Zipcode" secure inputMode="numeric" className="tabular-nums" value={f.zipcode} onChange={set("zipcode")} />
          <Input label="Birth date" type="date" leftIcon={<Icon name="calendar" size={14} />} value={f.birthDate} onChange={set("birthDate")} />
          <Input label="Age (years)" type="number" min={1} max={129} className="tabular-nums" value={f.ageYears} onChange={set("ageYears")} />
          <Input label="Email" type="email" secure leftIcon={<Icon name="mail" size={14} />} value={f.email} onChange={set("email")} />
          <div className="sm:col-span-2 flex flex-col gap-1.5">
            <div className="flex items-center gap-1">
              <span className="text-[12px] font-medium text-ink-700 leading-none">Jornaya LeadiD</span>
              <InfoHint title="Jornaya LeadiD" side="right">
                A compliance tracking token from Jornaya proving the prospect consented to be contacted (TCPA) and tying this lead to its original web form.
              </InfoHint>
            </div>
            <Input value={f.jornayaLeadId} onChange={set("jornayaLeadId")} />
          </div>
          <div className="sm:col-span-2 flex justify-end gap-2 pt-1">
            <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
            <Button type="submit" loading={saving} leftIcon={<Icon name="check" size={16} />}>Save changes</Button>
          </div>
        </form>
      )}
    </Modal>
  );
}
