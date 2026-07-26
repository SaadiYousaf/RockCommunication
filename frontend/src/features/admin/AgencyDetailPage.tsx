import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  useGetAgencyQuery, useListSalesQuery, useAgencyLicenseAgentsQuery,
  useCreateCallCenterInAgencyMutation, useCreateLicenseAgentMutation,
  useAgencyCallCentersQuery, useUpdateCallCenterInAgencyMutation,
  useListUsersQuery, useSetUserCallCenterMutation,
} from "../../shared/api/baseApi";
import type { CallCenterDto } from "../../shared/api/types";
import { roleLabel } from "../../shared/constants/roles";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

const money = (n: number | null | undefined) =>
  n == null ? "—" : n.toLocaleString(undefined, { style: "currency", currency: "USD" });

const PAGE = 25;

/**
 * SuperAdmin Agency Panel — drill-in for one agency. Top controls create a new call centre
 * or a License Agent (both provisioned through the shared invitation service); the table
 * beneath lists the agency's sales with their approved figures, commission, call centre and
 * assigned License Agent.
 */
export function AgencyDetailPage() {
  const { agencyId = "" } = useParams();
  const { data: agency, isLoading: agencyLoading } = useGetAgencyQuery(agencyId, { skip: !agencyId });
  const { data: agents } = useAgencyLicenseAgentsQuery(agencyId, { skip: !agencyId });

  const [skip, setSkip] = useState(0);
  const { data: sales, isLoading: salesLoading } = useListSalesQuery(
    { agencyId, skip, take: PAGE, sort: "soldAt-desc" }, { skip: !agencyId });

  const { data: callCenters } = useAgencyCallCentersQuery(agencyId, { skip: !agencyId });
  const { data: people } = useListUsersQuery({ agencyId }, { skip: !agencyId });
  const [setUserCc] = useSetUserCallCenterMutation();
  const toast = useToast();
  const [showCallCenter, setShowCallCenter] = useState(false);
  const [showAgent, setShowAgent] = useState(false);
  const [editCc, setEditCc] = useState<CallCenterDto | null>(null);

  async function assignCallCentre(userId: string, value: string) {
    try {
      await setUserCc({ userId, callCenterId: value || null }).unwrap();
      toast.success("Assignment saved", value ? "Pinned to the call centre." : "Set to agency-wide.");
    } catch (err: unknown) {
      toast.error("Couldn't assign", getErrorDetail(err) ?? "Try again.");
    }
  }

  const total = sales?.total ?? 0;
  const items = sales?.items ?? [];

  return (
    <>
      <PageHeader
        title={agency?.name ?? "Agency"}
        description={agency
          ? `${agency.code ? agency.code + " · " : ""}CEO ${agency.ceoUserName ?? "unassigned"} · ${agency.userCount} user(s)`
          : undefined}
      />

      <div className="flex flex-wrap items-center gap-2 mb-4">
        <Link to="/admin/agencies">
          <Button variant="ghost" size="sm" leftIcon={<Icon name="chevronLeft" size={14} />}>All agencies</Button>
        </Link>
        <div className="flex-1" />
        <Button variant="outline" leftIcon={<Icon name="building" size={14} />} onClick={() => setShowCallCenter(true)}>
          New call centre
        </Button>
        <Button leftIcon={<Icon name="userPlus" size={14} />} onClick={() => setShowAgent(true)}>
          Add license agent
        </Button>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-4">
        <Stat label="Sales" value={total} icon={<Icon name="chart" size={18} />} tone="brand" />
        <Stat label="Premium (page)" value={money(sales?.totalPremium)} icon={<Icon name="dollar" size={18} />} tone="success" />
        <Stat label="Funded" value={sales?.fundedCount ?? 0} icon={<Icon name="check" size={18} />} tone="success" />
        <Stat label="License agents" value={agents?.length ?? 0} icon={<Icon name="userCheck" size={18} />} tone="accent" />
      </div>

      {/* License-agent roster */}
      <Card className="mb-4">
        <CardHeader title="License agents" subtitle={agents ? `${agents.length} agent(s)` : undefined} />
        <CardBody>
          {!agents ? (
            <Skeleton className="h-24" />
          ) : agents.length === 0 ? (
            <EmptyState icon={<Icon name="userPlus" size={18} />} title="No license agents yet"
              description="Add one with the button above — they'll be emailed an invitation." />
          ) : (
            <div className="flex flex-wrap gap-2">
              {agents.map((a) => (
                <Badge key={a.id} tone={a.isActive ? "info" : "neutral"} variant="soft">
                  {a.name}{a.isActive ? "" : " (inactive)"}
                </Badge>
              ))}
            </div>
          )}
        </CardBody>
      </Card>

      {/* Call centres */}
      <Card className="mb-4">
        <CardHeader
          title="Call centres"
          subtitle={callCenters ? `${callCenters.length} call centre(s)` : undefined}
          action={<Button variant="outline" size="sm" leftIcon={<Icon name="building" size={14} />} onClick={() => setShowCallCenter(true)}>New call centre</Button>}
        />
        <CardBody>
          {!callCenters ? <Skeleton className="h-24" /> : callCenters.length === 0 ? (
            <EmptyState icon={<Icon name="building" size={18} />} title="No call centres yet"
              description="Add one with the button above — its admin is emailed an invitation." />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <TR><TH>Name</TH><TH>Code</TH><TH numeric>Leads</TH><TH>Status</TH><TH className="text-right">Actions</TH></TR>
                </THead>
                <TBody>
                  {callCenters.map((c) => (
                    <TR key={c.id}>
                      <TD className="font-medium text-ink-900">{c.name}</TD>
                      <TD className="font-mono text-xs text-ink-500">{c.code ?? "—"}</TD>
                      <TD numeric className="text-sm text-ink-600 tabular-nums">{c.leadCount}</TD>
                      <TD><Badge tone={c.isActive ? "success" : "neutral"} variant="soft">{c.isActive ? "Active" : "Disabled"}</Badge></TD>
                      <TD className="text-right">
                        <Button variant="ghost" size="sm" leftIcon={<Icon name="edit" size={14} />} onClick={() => setEditCc(c)}>Edit</Button>
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            </div>
          )}
        </CardBody>
      </Card>

      {/* People — assign to call centres */}
      <Card className="mb-4">
        <CardHeader
          title="People"
          subtitle={people ? `${people.length} user(s) — assign each to a call centre or leave agency-wide` : undefined}
        />
        <CardBody>
          {!people ? <Skeleton className="h-24" /> : people.length === 0 ? (
            <EmptyState icon={<Icon name="users" size={18} />} title="No users yet"
              description="Invite a CEO, call-centre admin, or license agent — they'll appear here to assign." />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <TR><TH>User</TH><TH>Roles</TH><TH>Call centre</TH></TR>
                </THead>
                <TBody>
                  {people.map((u) => (
                    <TR key={u.id}>
                      <TD>
                        <div className="flex items-center gap-2.5 min-w-0">
                          <Avatar name={u.userName} size={30} />
                          <div className="leading-tight min-w-0">
                            <div className="font-medium text-ink-900 truncate">{u.userName}</div>
                            <div className="text-xs text-ink-500 truncate">{u.email}</div>
                          </div>
                        </div>
                      </TD>
                      <TD>
                        <div className="flex flex-wrap gap-1">
                          {u.roles.map((r) => <Badge key={r} tone="neutral" variant="soft">{roleLabel(r)}</Badge>)}
                        </div>
                      </TD>
                      <TD>
                        <Select
                          aria-label={`Call centre for ${u.userName}`}
                          className="w-52"
                          value={u.callCenterId ?? ""}
                          onChange={(e) => assignCallCentre(u.id, e.target.value)}
                        >
                          <option value="">Agency-wide (no call centre)</option>
                          {(callCenters ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}{c.isActive ? "" : " (disabled)"}</option>)}
                        </Select>
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            </div>
          )}
        </CardBody>
      </Card>

      {/* Sales list */}
      <Card>
        <CardHeader title="Sales" subtitle={sales ? `${total} sale(s)` : undefined} />
        <CardBody>
          {salesLoading || agencyLoading ? <Skeleton className="h-48" /> : items.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="No sales" description="This agency has no recorded sales yet." />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <TR>
                    <TH numeric>#</TH>
                    <TH>Name</TH><TH>Phone</TH>
                    <TH>Carrier appr.</TH><TH>Plan appr.</TH>
                    <TH numeric>Coverage appr.</TH><TH numeric>Premium appr.</TH>
                    <TH numeric>Commission</TH>
                    <TH>Call centre</TH><TH>License agent</TH>
                    <TH>Status</TH>
                  </TR>
                </THead>
                <TBody>
                  {items.map((s) => (
                    <TR key={s.id}>
                      <TD numeric className="font-mono text-xs text-ink-500 tabular-nums">{s.saleNumber}</TD>
                      <TD className="font-medium text-ink-900"><span className="block truncate max-w-[12rem]">{s.leadName}</span></TD>
                      <TD className="font-mono text-xs text-ink-500 tabular-nums whitespace-nowrap">{s.leadPhone}</TD>
                      <TD className="text-sm">{s.carrierApproved ?? "—"}</TD>
                      <TD className="text-sm">{s.planApproved ?? "—"}</TD>
                      <TD numeric className="text-sm tabular-nums">{money(s.coverageApproved)}</TD>
                      <TD numeric className="text-sm tabular-nums">{money(s.premiumApproved)}</TD>
                      <TD numeric className="text-sm tabular-nums">{money(s.commissionEarned)}</TD>
                      <TD className="text-sm text-ink-600">{s.callCenterName ?? "—"}</TD>
                      <TD className="text-sm text-ink-600">{s.licenseAgentName ?? "—"}</TD>
                      <TD><Badge tone={s.status === "Funded" ? "success" : s.status === "Validated" ? "info" : "neutral"} variant="soft">{s.status}</Badge></TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            </div>
          )}

          {total > PAGE && (
            <div className="flex items-center justify-between pt-3 text-sm text-ink-500">
              <span className="tabular-nums whitespace-nowrap">{skip + 1}–{Math.min(skip + PAGE, total)} of {total}</span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>Prev</Button>
                <Button variant="outline" size="sm" disabled={skip + PAGE >= total} onClick={() => setSkip(skip + PAGE)}>Next</Button>
              </div>
            </div>
          )}
        </CardBody>
      </Card>

      {showCallCenter && <NewCallCenterModal agencyId={agencyId} onClose={() => setShowCallCenter(false)} />}
      {showAgent && <NewLicenseAgentModal agencyId={agencyId} onClose={() => setShowAgent(false)} />}
      {editCc && <EditCallCenterModal agencyId={agencyId} cc={editCc} onClose={() => setEditCc(null)} />}
    </>
  );
}

function EditCallCenterModal({ agencyId, cc, onClose }: { agencyId: string; cc: CallCenterDto; onClose: () => void }) {
  const [update, { isLoading }] = useUpdateCallCenterInAgencyMutation();
  const toast = useToast();
  const [name, setName] = useState(cc.name);
  const [code, setCode] = useState(cc.code ?? "");
  const [isActive, setIsActive] = useState(cc.isActive);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await update({ agencyId, callCenterId: cc.id, name: name.trim(), code: code.trim() || null, isActive }).unwrap();
      toast.success("Call centre saved", isActive ? cc.name : `${cc.name} disabled — its agents are logged out`);
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Check the fields and try again.");
    }
  }

  return (
    <Modal open onClose={onClose} title="Edit call centre" description="Rename, re-code, or disable this call centre. Disabling it immediately signs out and locks out every agent pinned to it.">
      <form onSubmit={submit} className="space-y-3">
        <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
        <Input label="Code" value={code} onChange={(e) => setCode(e.target.value)} />
        <label className="flex items-center gap-2 text-sm text-ink-700 select-none">
          <input type="checkbox" className="accent-brand-600" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          Active {!isActive && <span className="text-amber-700">— agents will be locked out</span>}
        </label>
        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
          <Button type="submit" loading={isLoading} leftIcon={<Icon name="check" size={15} />}>Save</Button>
        </div>
      </form>
    </Modal>
  );
}

function NewCallCenterModal({ agencyId, onClose }: { agencyId: string; onClose: () => void }) {
  const [create, { isLoading }] = useCreateCallCenterInAgencyMutation();
  const toast = useToast();
  const [form, setForm] = useState({ name: "", code: "", adminName: "", adminEmail: "" });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await create({
        agencyId,
        name: form.name.trim(),
        code: form.code.trim() || null,
        adminName: form.adminName.trim(),
        adminEmail: form.adminEmail.trim(),
      }).unwrap();
      toast.success("Call centre created", "The Call Center Admin has been emailed an invitation.");
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't create call centre", getErrorDetail(err) ?? "Check the fields and try again.");
    }
  }

  return (
    <Modal open onClose={onClose} title="New call centre" description="Create the call centre. Inviting a Call Center Admin is optional — you can create it now and assign people later.">
      <form onSubmit={submit} className="space-y-3">
        <Input label="Call centre name" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        <Input label="Short code" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })} />
        <div className="pt-1 text-[11px] font-semibold uppercase tracking-wider text-ink-500">Invite an admin — optional</div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input label="Admin name" labelHint="Optional" value={form.adminName} onChange={(e) => setForm({ ...form, adminName: e.target.value })} />
          <Input label="Admin email" labelHint="Optional" type="email" leftIcon={<Icon name="mail" size={14} />} value={form.adminEmail} onChange={(e) => setForm({ ...form, adminEmail: e.target.value })} />
        </div>
        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
          <Button type="submit" loading={isLoading} leftIcon={<Icon name="plus" size={15} />}>
            {form.adminEmail.trim() ? "Create + invite admin" : "Create call centre"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}

function NewLicenseAgentModal({ agencyId, onClose }: { agencyId: string; onClose: () => void }) {
  const [create, { isLoading }] = useCreateLicenseAgentMutation();
  const toast = useToast();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await create({ agencyId, name: name.trim(), email: email.trim() }).unwrap();
      toast.success("License agent added", "They've been emailed an invitation.");
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't add license agent", getErrorDetail(err) ?? "Check the fields and try again.");
    }
  }

  return (
    <Modal open onClose={onClose} title="Add license agent" description="Creates an agency-level License Agent and emails them an invitation.">
      <form onSubmit={submit} className="space-y-3">
        <Input label="Full name" required value={name} onChange={(e) => setName(e.target.value)} />
        <Input label="Email" type="email" required leftIcon={<Icon name="mail" size={14} />} value={email} onChange={(e) => setEmail(e.target.value)} />
        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
          <Button type="submit" loading={isLoading} leftIcon={<Icon name="userPlus" size={15} />}>Add + invite</Button>
        </div>
      </form>
    </Modal>
  );
}
