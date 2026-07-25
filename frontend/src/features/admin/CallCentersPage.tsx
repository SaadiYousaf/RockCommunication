import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useListCallCentersQuery, useCreateCallCenterMutation, useUpdateCallCenterMutation,
  useAgencyOptionsQuery, useAgencyCallCentersQuery, useCreateCallCenterInAgencyMutation,
} from "../../shared/api/baseApi";
import type { CallCenterDto } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

/**
 * Manage the call centers within an agency. Call centers are the finer data-isolation
 * unit: pipeline data belongs to one, and call-center-scoped agents only see their own.
 * A Super Admin has no agency of their own, so they first pick which agency to manage;
 * agency-level admins/managers work within their own agency automatically.
 */
export function CallCentersPage() {
  const roles = useSelector((s: RootState) => s.auth.user?.roles ?? []);
  const isSuperAdmin = roles.includes("SuperAdmin");

  // SuperAdmin scopes the page to a chosen agency; others are pinned to their own.
  const { data: agencyOptions } = useAgencyOptionsQuery(undefined, { skip: !isSuperAdmin });
  const [agencyId, setAgencyId] = useState("");
  useEffect(() => {
    if (isSuperAdmin && !agencyId && agencyOptions && agencyOptions.length) setAgencyId(agencyOptions[0].id);
  }, [isSuperAdmin, agencyOptions, agencyId]);

  const own = useListCallCentersQuery(undefined, { skip: isSuperAdmin });
  const scoped = useAgencyCallCentersQuery(agencyId, { skip: !isSuperAdmin || !agencyId });
  const list = isSuperAdmin ? scoped.data : own.data;
  const isLoading = isSuperAdmin ? (scoped.isLoading || !agencyId) : own.isLoading;

  const [createCc, { isLoading: creating }] = useCreateCallCenterMutation();
  const [createCcInAgency, { isLoading: creatingSa }] = useCreateCallCenterInAgencyMutation();
  const [updateCc, { isLoading: saving }] = useUpdateCallCenterMutation();
  const toast = useToast();

  const [editing, setEditing] = useState<CallCenterDto | null>(null);
  const [showNew, setShowNew] = useState(false);
  const [form, setForm] = useState({ name: "", code: "", adminName: "", adminEmail: "" });

  async function submitNew(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (!form.adminName.trim() || !form.adminEmail.trim()) { toast.error("Admin required", "A Call Center Admin name and email are required."); return; }
      if (isSuperAdmin && !agencyId) { toast.error("Agency required", "Choose which agency this call centre belongs to."); return; }
      const body = { name: form.name.trim(), code: form.code.trim() || null, adminName: form.adminName.trim(), adminEmail: form.adminEmail.trim() };
      if (isSuperAdmin) await createCcInAgency({ agencyId, ...body }).unwrap();
      else await createCc(body).unwrap();
      toast.success("Call center created", `${form.name} — the admin has been emailed an invitation.`);
      setShowNew(false); setForm({ name: "", code: "", adminName: "", adminEmail: "" });
    } catch (err: unknown) {
      toast.error("Couldn't create", getErrorDetail(err) ?? "Check the name and try again.");
    }
  }

  async function submitEdit(e: React.FormEvent) {
    e.preventDefault();
    if (!editing) return;
    try {
      await updateCc({
        id: editing.id, name: editing.name.trim(), code: editing.code?.trim() || null,
        isActive: editing.isActive,
      }).unwrap();
      toast.success("Saved", editing.name);
      setEditing(null);
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Call Centers"
        description="Operational units within your agency. Assign agents to a call center to isolate their pipeline data."
        actions={
          <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setShowNew(true)}>
            Add call center
          </Button>
        }
      />
      <Card>
        <CardHeader
          title="Call centers"
          subtitle={list ? `${list.length} total` : undefined}
          action={isSuperAdmin ? (
            <Select aria-label="Agency" value={agencyId} onChange={(e) => setAgencyId(e.target.value)} className="w-56">
              {(!agencyOptions || agencyOptions.length === 0) && <option value="">No agencies</option>}
              {(agencyOptions ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </Select>
          ) : undefined}
        />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !list || list.length === 0 ? (
            <EmptyState icon={<Icon name="building" size={20} />} title="No call centers yet"
              description="Create one, then assign agents to it from User Management." />
          ) : (
            <Table>
              <THead>
                <TR><TH>Name</TH><TH>Code</TH><TH>Leads</TH><TH>Status</TH><TH></TH></TR>
              </THead>
              <TBody>
                {list.map((c) => (
                  <TR key={c.id}>
                    <TD className="font-medium text-ink-900">{c.name}</TD>
                    <TD className="font-mono text-xs text-ink-600">{c.code || "—"}</TD>
                    <TD className="text-sm">{c.leadCount}</TD>
                    <TD>{c.isActive
                      ? <Badge tone="success" variant="soft">Active</Badge>
                      : <Badge tone="neutral" variant="soft">Inactive</Badge>}</TD>
                    <TD className="text-right">
                      <Button size="sm" variant="outline" onClick={() => setEditing({ ...c })}>Edit</Button>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      <Modal open={showNew} onClose={() => setShowNew(false)} title="New call center"
        description={isSuperAdmin ? `In agency: ${agencyOptions?.find((a) => a.id === agencyId)?.name ?? "—"}` : undefined}>
        <form onSubmit={submitNew} className="space-y-3">
          <Input label="Name" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Input label="Code" placeholder="Optional short code" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} />
          <div className="border-t border-ink-100 pt-3 mt-1">
            <div className="text-xs font-semibold text-ink-500 uppercase tracking-wide mb-2">Call Center Admin</div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Input label="Admin name" required value={form.adminName} onChange={(e) => setForm({ ...form, adminName: e.target.value })} />
              <Input label="Admin email" type="email" required value={form.adminEmail} onChange={(e) => setForm({ ...form, adminEmail: e.target.value })} />
            </div>
            <p className="text-xs text-ink-500 mt-2">The admin is created automatically and emailed a temporary password — they set their own on first login.</p>
          </div>
          <div className="flex justify-end gap-2 pt-1">
            <Button type="button" variant="outline" onClick={() => setShowNew(false)}>Cancel</Button>
            <Button type="submit" loading={creating || creatingSa}>Create</Button>
          </div>
        </form>
      </Modal>

      <Modal open={!!editing} onClose={() => setEditing(null)} title="Edit call center">
        {editing && (
          <form onSubmit={submitEdit} className="space-y-3">
            <Input label="Name" required value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            <Input label="Code" value={editing.code ?? ""} onChange={(e) => setEditing({ ...editing, code: e.target.value })} />
            <label className="flex items-center gap-2 text-sm text-ink-700">
              <input type="checkbox" checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })} />
              Active
            </label>
            <div className="flex justify-end gap-2 pt-1">
              <Button type="button" variant="outline" onClick={() => setEditing(null)}>Cancel</Button>
              <Button type="submit" loading={saving}>Save</Button>
            </div>
          </form>
        )}
      </Modal>
    </>
  );
}
