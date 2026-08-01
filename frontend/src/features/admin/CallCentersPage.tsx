import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useListCallCentersQuery, useCreateCallCenterMutation, useUpdateCallCenterMutation,
  useAgencyOptionsQuery, useAgencyCallCentersQuery, useCreateCallCenterInAgencyMutation,
  useUpdateCallCenterInAgencyMutation,
} from "../../shared/api/baseApi";
import type { CallCenterDto } from "../../shared/api/types";
import { useTableSort } from "../../shared/hooks/useTableSort";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
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
  const { sorted, dirFor, toggle } = useTableSort(list, {
    key: "name",
    accessors: { status: (c) => (c.isActive ? 1 : 0) },
  });
  const isLoading = isSuperAdmin ? (scoped.isLoading || !agencyId) : own.isLoading;

  // At-a-glance totals for the currently-scoped agency (mirrors AgenciesPage).
  const stats = useMemo(() => {
    const l = list ?? [];
    return {
      total: l.length,
      active: l.filter((c) => c.isActive).length,
      leads: l.reduce((sum, c) => sum + (c.leadCount ?? 0), 0),
    };
  }, [list]);

  const [createCc, { isLoading: creating }] = useCreateCallCenterMutation();
  const [createCcInAgency, { isLoading: creatingSa }] = useCreateCallCenterInAgencyMutation();
  const [updateCc, { isLoading: saving }] = useUpdateCallCenterMutation();
  const [updateCcInAgency, { isLoading: savingSa }] = useUpdateCallCenterInAgencyMutation();
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
      // SuperAdmin manages call centres cross-tenant via the agency-scoped endpoint; the plain
      // updateCc endpoint is agency-scoped and 403s for SuperAdmin (empty AgencyId). Mirror submitNew.
      const body = { name: editing.name.trim(), code: editing.code?.trim() || null, isActive: editing.isActive };
      if (isSuperAdmin) await updateCcInAgency({ agencyId, callCenterId: editing.id, ...body }).unwrap();
      else await updateCc({ id: editing.id, ...body }).unwrap();
      toast.success("Saved", editing.name);
      setEditing(null);
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Administration"
        title="Call Centers"
        badge={
          <InfoHint title="Agency vs call centre" side="right">
            An agency is a top-level tenant; a call centre is the finer data-isolation unit inside it.
            Pipeline data belongs to one call centre, and call-centre-scoped agents see only their own.
            Super Admins pick which agency to manage; agency admins are pinned to their own.
          </InfoHint>
        }
        description="Operational units within your agency. Assign agents to a call center to isolate their pipeline data."
        actions={
          <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setShowNew(true)}>
            Add call center
          </Button>
        }
      />

      {/* At-a-glance totals for the scoped agency */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-5">
        <Stat
          label="Call centers"
          value={stats.total}
          icon={<Icon name="building" size={18} />}
          hint="Operational units"
          tone="brand"
        />
        <Stat
          label="Active"
          value={stats.active}
          icon={<Icon name="check" size={18} />}
          hint={`${stats.total - stats.active} inactive`}
          tone="success"
        />
        <Stat
          label="Leads"
          value={stats.leads}
          icon={<Icon name="target" size={18} />}
          hint="Across all centers"
          tone="accent"
        />
      </div>

      <Card>
        <CardHeader
          title="Call centers"
          subtitle={list ? `${list.length} call center${list.length === 1 ? "" : "s"}` : undefined}
          action={isSuperAdmin ? (
            <Select aria-label="Agency" value={agencyId} onChange={(e) => setAgencyId(e.target.value)} className="w-56">
              {(!agencyOptions || agencyOptions.length === 0) && <option value="">No agencies</option>}
              {(agencyOptions ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}{a.isActive ? "" : " (inactive)"}</option>)}
            </Select>
          ) : undefined}
        />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !list || list.length === 0 ? (
            <EmptyState icon={<Icon name="building" size={20} />} title="No call centers yet"
              description="Create one, then assign agents to it from User Management."
              action={<Button size="sm" leftIcon={<Icon name="plus" size={14} />} onClick={() => setShowNew(true)}>Add call center</Button>} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Name</TH>
                  <TH sortDir={dirFor("code")} onClick={() => toggle("code")}>
                    <span className="inline-flex items-center gap-1">Code<InfoHint title="Short code" side="top">An optional short identifier for the call centre — it shows up in reports and logs so you can spot it at a glance.</InfoHint></span>
                  </TH>
                  <TH numeric sortDir={dirFor("leadCount")} onClick={() => toggle("leadCount")}>
                    <span className="inline-flex items-center gap-1">Leads<InfoHint title="Leads" side="top">How many leads currently sit in this call centre's pipeline.</InfoHint></span>
                  </TH>
                  <TH sortDir={dirFor("status")} onClick={() => toggle("status")}>
                    <span className="inline-flex items-center gap-1">Status<InfoHint title="Status" side="top">Active call centres accept sign-ins. Disabling one locks out every agent pinned to it — they can no longer sign in, and any active session ends within minutes.</InfoHint></span>
                  </TH>
                  <TH></TH>
                </TR>
              </THead>
              <TBody>
                {sorted.map((c) => (
                  <TR key={c.id}>
                    <TD className="font-medium text-ink-900 truncate max-w-[16rem]">{c.name}</TD>
                    <TD className="font-mono text-xs text-ink-600 whitespace-nowrap">{c.code || "—"}</TD>
                    <TD numeric className="text-sm tabular-nums">{c.leadCount}</TD>
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
            <label className="flex items-center gap-2 text-sm text-ink-700 cursor-pointer select-none">
              <input type="checkbox" className="rounded border-ink-300 text-brand-600 focus:ring-2 focus:ring-brand-500/40" checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })} />
              Active
            </label>
            <div className="flex justify-end gap-2 pt-1">
              <Button type="button" variant="outline" onClick={() => setEditing(null)}>Cancel</Button>
              <Button type="submit" loading={saving || savingSa}>Save</Button>
            </div>
          </form>
        )}
      </Modal>
    </>
  );
}
