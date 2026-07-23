import { VERIFIER_STATUSES as STATUSES, MARITAL_STATUSES as MARITAL } from "../../shared/constants/intake";
import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import {
  useVerifierQueueQuery, useSetVerifierStatusMutation,
  useGetVerifyLeadQuery, useUpdateVerifyLeadMutation,
} from "../../shared/api/baseApi";
import type { IntakeQueueItem, VerifierStatusValue } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader, Select,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";


/** Verifier work queue — fronted leads awaiting a verification status. */
export function VerifyQueuePage() {
  const { data: queue, isLoading } = useVerifierQueueQuery();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [q, setQ] = useState("");
  const filtered = (queue ?? []).filter((l) =>
    !q.trim() || `${l.firstName} ${l.lastName} ${l.phoneNumber} ${l.city ?? ""} ${l.state ?? ""} ${l.email ?? ""}`.toLowerCase().includes(q.trim().toLowerCase()));
  return (
    <>
      <PageHeader title="Verifier Queue" description="Leads captured by fronters. Open one to review or correct it, then set a status — 'Verified' sends it to the closer queue." />
      <Card>
        <CardHeader title="Awaiting verification" subtitle={queue ? `${filtered.length} of ${queue.length} lead(s)` : undefined}
          action={<Input placeholder="Search this queue…" leftIcon={<Icon name="search" size={14} />} value={q} onChange={(e) => setQ(e.target.value)} className="w-56" />} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !filtered || filtered.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="Queue is empty" description={q ? "No matches in this queue." : "New fronted leads will appear here."} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH>Name</TH><TH>Phone</TH><TH>Location</TH><TH>Age</TH><TH>Received</TH><TH>Verifier status</TH>
                </TR>
              </THead>
              <TBody>
                {filtered.map((l) => <VerifyRow key={l.id} lead={l} onEdit={() => setEditingId(l.id)} />)}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>
      {editingId && <EditLeadModal leadId={editingId} onClose={() => setEditingId(null)} />}
    </>
  );
}

function VerifyRow({ lead, onEdit }: { lead: IntakeQueueItem; onEdit: () => void }) {
  const [setStatus, { isLoading }] = useSetVerifierStatusMutation();
  const toast = useToast();
  const [status, setStatusVal] = useState<VerifierStatusValue | "">("");

  async function apply() {
    if (!status) { toast.error("Pick a status"); return; }
    try {
      const r = await setStatus({ leadId: lead.id, status }).unwrap();
      toast.success("Status saved", r.status === "Verified" ? "Lead sent to closer queue" : `Marked ${r.status}`);
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <TR>
      <TD className="font-medium text-ink-900">{lead.firstName} {lead.lastName}</TD>
      <TD className="font-mono text-xs">{lead.phoneNumber}</TD>
      <TD className="text-sm text-ink-600">{[lead.city, lead.state].filter(Boolean).join(", ") || "—"}</TD>
      <TD className="text-sm">{lead.ageYears ?? "—"}</TD>
      <TD className="text-xs text-ink-500">{new Date(lead.createdAt).toLocaleString()}</TD>
      <TD>
        <div className="flex items-center gap-2">
          <Button size="sm" variant="ghost" leftIcon={<Icon name="edit" size={14} />} onClick={onEdit}>Open</Button>
          <Select className="h-9 w-40 text-sm" value={status} onChange={(e) => setStatusVal(e.target.value as VerifierStatusValue)}>
            <option value="">Select…</option>
            {STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </Select>
          <Button size="sm" loading={isLoading} onClick={apply}>Save</Button>
          {lead.verifierStatus !== "None" && <Badge tone="neutral" variant="soft">{lead.verifierStatus}</Badge>}
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
      toast.success("Lead updated");
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Check the fields and try again.");
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
          <Input label="Phone number" required secure value={f.phoneNumber} onChange={set("phoneNumber")} />
          <Input label="Street address" secure containerClassName="sm:col-span-2" value={f.streetAddress} onChange={set("streetAddress")} />
          <Input label="City" secure value={f.city} onChange={set("city")} />
          <Input label="State" secure value={f.state} onChange={set("state")} />
          <Input label="Zipcode" secure inputMode="numeric" value={f.zipcode} onChange={set("zipcode")} />
          <Input label="Birth date" type="date" value={f.birthDate} onChange={set("birthDate")} />
          <Input label="Age (years)" type="number" min={1} max={129} value={f.ageYears} onChange={set("ageYears")} />
          <Input label="Email" type="email" secure value={f.email} onChange={set("email")} />
          <Input label="Jornaya LeadiD" containerClassName="sm:col-span-2" value={f.jornayaLeadId} onChange={set("jornayaLeadId")} />
          <div className="sm:col-span-2 flex justify-end gap-2 pt-1">
            <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
            <Button type="submit" loading={saving} leftIcon={<Icon name="check" size={16} />}>Save changes</Button>
          </div>
        </form>
      )}
    </Modal>
  );
}
