import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { ADMIN_MSG } from "./messages";
import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import type { RootState } from "../../app/store";
import {
  useListCallCentersQuery, useCreateCallCenterMutation, useUpdateCallCenterMutation,
  useAgencyOptionsQuery, useAgencyCallCentersQuery, useCreateCallCenterInAgencyMutation,
  useUpdateCallCenterInAgencyMutation, useListUsersQuery,
} from "../../shared/api/baseApi";
import type { CallCenterDto, UserSummary } from "../../shared/api/types";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
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
  const navigate = useNavigate();

  // SuperAdmin scopes the page to a chosen agency; others are pinned to their own.
  const { data: agencyOptions } = useAgencyOptionsQuery(undefined, { skip: !isSuperAdmin });
  const [agencyId, setAgencyId] = useState("");
  useEffect(() => {
    if (isSuperAdmin && !agencyId && agencyOptions && agencyOptions.length) setAgencyId(agencyOptions[0].id);
  }, [isSuperAdmin, agencyOptions, agencyId]);

  const own = useListCallCentersQuery(undefined, { skip: isSuperAdmin });
  const scoped = useAgencyCallCentersQuery(agencyId, { skip: !isSuperAdmin || !agencyId });
  const list = isSuperAdmin ? scoped.data : own.data;

  // Who runs each centre: the Call Center Admin(s) pinned to it. The users list already carries
  // callCenterId + roles, so we group here rather than round-tripping the backend. Clicking one
  // opens their profile (where their access can be managed).
  const { data: users } = useListUsersQuery(isSuperAdmin ? { agencyId } : undefined, {
    skip: isSuperAdmin && !agencyId,
  });
  const adminsByCenter = useMemo(() => {
    const map = new Map<string, UserSummary[]>();
    for (const u of users ?? []) {
      if (!u.callCenterId || !(u.roles ?? []).includes("CallCenterAdmin")) continue;
      const arr = map.get(u.callCenterId) ?? [];
      arr.push(u);
      map.set(u.callCenterId, arr);
    }
    return map;
  }, [users]);
  // Client-side search over the already-loaded centres (name / short code).
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return list ?? [];
    return (list ?? []).filter((c) =>
      [c.name, c.code].some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [list, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    key: "name",
    accessors: { status: (c) => (c.isActive ? 1 : 0) },
  });
  const sel = useRowSelection(sorted.map((c) => c.id));
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

  function exportSelected() {
    const chosen = sorted.filter((c) => sel.isSelected(c.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (c) => c.name },
      { header: "Code", value: (c) => c.code ?? "" },
      { header: "Admin", value: (c) => (adminsByCenter.get(c.id) ?? []).map((a) => a.userName).join("; ") },
      { header: "Leads", value: (c) => c.leadCount ?? 0 },
      { header: "Active", value: (c) => (c.isActive ? "Yes" : "No") },
    ], `call-centres-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(ADMIN_MSG.common.exportReady, ADMIN_MSG.common.exportReadyDesc(chosen.length));
  }

  const [editing, setEditing] = useState<CallCenterDto | null>(null);
  const [showNew, setShowNew] = useState(false);
  const [form, setForm] = useState({ name: "", code: "", adminName: "", adminEmail: "" });

  async function submitNew(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (!form.adminName.trim() || !form.adminEmail.trim()) { toast.error(ADMIN_MSG.callCenters.adminRequiredTitle, ADMIN_MSG.callCenters.adminRequiredDesc); return; }
      if (isSuperAdmin && !agencyId) { toast.error(ADMIN_MSG.callCenters.agencyRequiredTitle, ADMIN_MSG.callCenters.agencyRequiredDesc); return; }
      const body = { name: form.name.trim(), code: form.code.trim() || null, adminName: form.adminName.trim(), adminEmail: form.adminEmail.trim() };
      if (isSuperAdmin) await createCcInAgency({ agencyId, ...body }).unwrap();
      else await createCc(body).unwrap();
      toast.success(ADMIN_MSG.callCenters.created, ADMIN_MSG.callCenters.createdDesc(form.name));
      setShowNew(false); setForm({ name: "", code: "", adminName: "", adminEmail: "" });
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.common.createFailed, getErrorDetail(err) ?? ADMIN_MSG.callCenters.createFailedDesc);
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
      toast.success(ADMIN_MSG.common.saved, editing.name);
      setEditing(null);
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.common.saveFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
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
          action={
            <div className="flex flex-wrap items-center gap-2">
              {isSuperAdmin && (
                <Select aria-label="Agency" value={agencyId} onChange={(e) => setAgencyId(e.target.value)} className="w-56">
                  {(!agencyOptions || agencyOptions.length === 0) && <option value="">No agencies</option>}
                  {(agencyOptions ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}{a.isActive ? "" : " (inactive)"}</option>)}
                </Select>
              )}
              <SearchInput value={search} onChange={setSearch}
                placeholder={ADMIN_MSG.callCenters.searchPlaceholder} className="w-64" />
            </div>
          }
        />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !list || list.length === 0 ? (
            <EmptyState icon={<Icon name="building" size={20} />} title={ADMIN_MSG.callCenters.emptyTitle}
              description={ADMIN_MSG.callCenters.emptyDesc}
              action={<Button size="sm" leftIcon={<Icon name="plus" size={14} />} onClick={() => setShowNew(true)}>Add call center</Button>} />
          ) : sorted.length === 0 ? (
            <EmptyState icon={<Icon name="search" size={20} />} title={ADMIN_MSG.callCenters.noMatchTitle}
              description={ADMIN_MSG.callCenters.noMatchDesc} />
          ) : (
            <>
            <Table>
              <THead>
                <TR>
                  <TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH>
                  <TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Name</TH>
                  <TH sortDir={dirFor("code")} onClick={() => toggle("code")}>
                    <span className="inline-flex items-center gap-1">Code<InfoHint title="Short code" side="top">An optional short identifier for the call centre — it shows up in reports and logs so you can spot it at a glance.</InfoHint></span>
                  </TH>
                  <TH>
                    <span className="inline-flex items-center gap-1">Admin<InfoHint title="Call Center Admin" side="top">The admin who runs this call centre. Click a name to open their profile, where you can manage their access.</InfoHint></span>
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
                  <TR key={c.id} className={sel.isSelected(c.id) ? "bg-brand-50/40" : undefined}>
                    <TD><Checkbox aria-label={`Select ${c.name}`} {...sel.checkboxProps(c.id)} /></TD>
                    <TD className="font-medium text-ink-900 truncate max-w-[16rem]">{c.name}</TD>
                    <TD className="font-mono text-xs text-ink-600 whitespace-nowrap">{c.code || "—"}</TD>
                    <TD><AdminCell admins={adminsByCenter.get(c.id) ?? []} onOpen={(id) => navigate(`/profile/${id}`)} /></TD>
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
            <BulkActionBar
              count={sel.selectedCount} itemNoun="call centre" onClear={sel.clear}
              actions={[
                { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
              ]}
            />
            </>
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
              Active {!editing.isActive && <span className="text-amber-700">— every agent pinned to this centre will be locked out</span>}
            </label>
            <div className="border-t border-ink-100 pt-3">
              <div className="text-xs font-semibold text-ink-500 uppercase tracking-wide mb-2">Call Center Admin</div>
              <AdminCell admins={adminsByCenter.get(editing.id) ?? []}
                onOpen={(id) => { setEditing(null); navigate(`/profile/${id}`); }} />
            </div>
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

/** The Call Center Admin(s) for a centre — each a link to their profile. Falls back to a muted dash. */
function AdminCell({ admins, onOpen }: { admins: UserSummary[]; onOpen: (id: string) => void }) {
  if (admins.length === 0) return <span className="text-ink-400 text-sm">—</span>;
  return (
    <div className="flex flex-wrap gap-1.5">
      {admins.map((a) => (
        <button
          key={a.id} type="button" onClick={() => onOpen(a.id)} title={a.email}
          className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700 hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/30 rounded"
        >
          <Icon name="user" size={13} className="text-ink-400" />
          <span className="truncate max-w-[10rem]">{a.userName}</span>
        </button>
      ))}
    </div>
  );
}
