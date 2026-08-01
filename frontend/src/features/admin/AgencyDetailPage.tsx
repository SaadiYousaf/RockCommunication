import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  useGetAgencyQuery, useListSalesQuery, useAgencyLicenseAgentsQuery,
  useCreateCallCenterInAgencyMutation, useCreateLicenseAgentMutation,
  useAgencyCallCentersQuery, useUpdateCallCenterInAgencyMutation,
  useListUsersQuery,
} from "../../shared/api/baseApi";
import type { CallCenterDto } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { formatUsd } from "../../shared/lib/format";

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
  const [showCallCenter, setShowCallCenter] = useState(false);
  const [showAgent, setShowAgent] = useState(false);
  const [editCc, setEditCc] = useState<CallCenterDto | null>(null);

  // Staff head-count per call centre (empty key = the agency-wide bucket) so the drill-down
  // list can show how many people sit in each before you click into it.
  const staffByCc = useMemo(() => {
    const m = new Map<string, number>();
    for (const u of people ?? []) {
      const key = u.callCenterId ?? "";
      m.set(key, (m.get(key) ?? 0) + 1);
    }
    return m;
  }, [people]);
  const agencyWideCount = staffByCc.get("") ?? 0;

  const total = sales?.total ?? 0;
  const items = sales?.items ?? [];

  return (
    <>
      <PageHeader
        eyebrow="Administration"
        title={agency?.name ?? "Agency"}
        description={agency
          ? `${agency.code ? agency.code + " · " : ""}CEO ${agency.ceoUserName ?? "unassigned"} · ${agency.userCount} user(s)`
          : undefined}
        breadcrumbs={[{ label: "Admin" }, { label: "Agencies", to: "/admin/agencies" }, { label: agency?.name ?? "Agency" }]}
        badge={
          <Badge tone="brand" variant="soft">
            <Icon name="shield" size={11} className="-ml-0.5" /> SuperAdmin
          </Badge>
        }
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
        <Stat label="Premium (total)" value={formatUsd(sales?.totalPremium)} icon={<Icon name="dollar" size={18} />} tone="success" />
        <Stat label="Funded" value={sales?.fundedCount ?? 0} icon={<Icon name="check" size={18} />} tone="success"
          hint="First payment cleared" />
        <Stat label="License agents" value={agents?.length ?? 0} icon={<Icon name="userCheck" size={18} />} tone="accent" />
      </div>

      {/* License-agent roster */}
      <Card className="mb-4">
        <CardHeader
          title={
            <span className="inline-flex items-center gap-1.5">
              License agents
              <InfoHint title="License agent" side="right">
                A licensed agent at this agency. Every approved sale is assigned to one license
                agent, who is credentialed to submit the policy to the carrier.
              </InfoHint>
            </span>
          }
          subtitle={agents ? `${agents.length} agent(s)` : undefined}
        />
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
                  <TR><TH>Call centre</TH><TH numeric><span className="inline-flex items-center gap-1">Staff<InfoHint title="Staff" side="top">How many users are assigned to this call centre. The bottom row counts everyone not pinned to any centre (agency-wide).</InfoHint></span></TH><TH numeric>Leads</TH><TH>Status</TH><TH className="text-right">Actions</TH></TR>
                </THead>
                <TBody>
                  {callCenters.map((c) => (
                    <TR key={c.id}>
                      <TD>
                        <Link to={`/admin/agencies/${agencyId}/call-centers/${c.id}`}
                          className="font-medium text-brand-700 hover:underline inline-flex items-center gap-1.5">
                          <Icon name="building" size={14} className="text-brand-500 shrink-0" />{c.name}
                        </Link>
                        {c.code && <span className="ml-2 font-mono text-xs text-ink-400">{c.code}</span>}
                      </TD>
                      <TD numeric className="text-sm text-ink-700 tabular-nums font-medium">{staffByCc.get(c.id) ?? 0}</TD>
                      <TD numeric className="text-sm text-ink-600 tabular-nums">{c.leadCount}</TD>
                      <TD><Badge tone={c.isActive ? "success" : "neutral"} variant="soft">{c.isActive ? "Active" : "Disabled"}</Badge></TD>
                      <TD className="text-right">
                        <Button variant="ghost" size="sm" leftIcon={<Icon name="edit" size={14} />} onClick={() => setEditCc(c)}>Edit</Button>
                      </TD>
                    </TR>
                  ))}
                  {/* Agency-wide bucket — users not pinned to any call centre */}
                  <TR>
                    <TD>
                      <Link to={`/admin/agencies/${agencyId}/call-centers/none`}
                        className="font-medium text-ink-700 hover:underline inline-flex items-center gap-1.5">
                        <Icon name="globe" size={14} className="text-ink-400 shrink-0" />Agency-wide (no call centre)
                      </Link>
                    </TD>
                    <TD numeric className="text-sm text-ink-700 tabular-nums font-medium">{agencyWideCount}</TD>
                    <TD numeric className="text-ink-400">—</TD>
                    <TD className="text-ink-400">—</TD>
                    <TD />
                  </TR>
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
                    <TH numeric><span className="inline-flex items-center gap-1">#<InfoHint title="Sale number" side="top">The sale's reference number in the system — use it to find this sale in reports or when raising a query.</InfoHint></span></TH>
                    <TH>Name</TH><TH>Phone</TH>
                    <TH><span className="inline-flex items-center gap-1">Carrier appr.<InfoHint title="Carrier approved" side="top">The insurance carrier that approved the policy — may differ from the one originally quoted.</InfoHint></span></TH><TH><span className="inline-flex items-center gap-1">Plan appr.<InfoHint title="Plan approved" side="top">The specific plan (product) the carrier approved for this policy.</InfoHint></span></TH>
                    <TH numeric><span className="inline-flex items-center gap-1">Coverage appr.<InfoHint title="Coverage approved" side="top">The policy coverage amount (face amount / death benefit) the carrier approved.</InfoHint></span></TH><TH numeric><span className="inline-flex items-center gap-1">Premium appr.<InfoHint title="Premium approved" side="top">The monthly premium the carrier approved — may differ from the premium originally sold.</InfoHint></span></TH>
                    <TH numeric><span className="inline-flex items-center gap-1">Commission<InfoHint title="Commission earned" side="top">The commission earned on this sale under the applicable commission rules.</InfoHint></span></TH>
                    <TH>Call centre</TH><TH>License agent</TH>
                    <TH><span className="inline-flex items-center gap-1">Status<InfoHint title="Sale status" side="left">The sale's stage — e.g. Validated (submitted and approved by the carrier) or Funded (first payment draft cleared).</InfoHint></span></TH>
                  </TR>
                </THead>
                <TBody>
                  {items.map((s) => (
                    <TR key={s.id}>
                      <TD numeric className="font-mono text-xs text-ink-500 tabular-nums">{s.saleNumber}</TD>
                      <TD className="font-medium text-ink-900"><span className="block truncate max-w-[12rem]" title={s.leadName}>{s.leadName}</span></TD>
                      <TD className="font-mono text-xs text-ink-500 tabular-nums whitespace-nowrap">{s.leadPhone}</TD>
                      <TD className="text-sm">{s.carrierApproved ?? "—"}</TD>
                      <TD className="text-sm">{s.planApproved ?? "—"}</TD>
                      <TD numeric className="text-sm tabular-nums">{formatUsd(s.coverageApproved)}</TD>
                      <TD numeric className="text-sm tabular-nums">{formatUsd(s.premiumApproved)}</TD>
                      <TD numeric className="text-sm tabular-nums">{formatUsd(s.commissionEarned)}</TD>
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
