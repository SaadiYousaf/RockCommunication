import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import {
  useSetValidatorStatusMutation, useValidatorQueueQuery, useGetValidateLeadQuery,
  useAgencyOptionsQuery, useAgencyLicenseAgentsQuery,
} from "../../shared/api/baseApi";
import type { ValidatorQueueItem, ValidatorStatusValue, ClosingApplicationView } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Select, Skeleton, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";
import {
  VALIDATOR_STATUSES as STATUSES,
  VALIDATOR_ERROR_REASONS as ERROR_REASONS,
  VALIDATOR_STATUS_LABEL as LABEL,
  VALIDATOR_STATUS_TONE as TONE,
} from "../../shared/constants/intake";
import { useTableSort } from "../../shared/hooks/useTableSort";

const money = (n: number | null | undefined) =>
  n == null ? "—" : n.toLocaleString(undefined, { style: "currency", currency: "USD" });

/** Submission queue — every submitted sale, worked through the validator statuses. */
export function ValidateQueuePage() {
  const { data: queue, isLoading } = useValidatorQueueQuery();
  const [active, setActive] = useState<ValidatorQueueItem | null>(null);
  const [viewing, setViewing] = useState<ValidatorQueueItem | null>(null);
  const [q, setQ] = useState("");
  const filtered = (queue ?? []).filter((s) =>
    !q.trim() || `${s.leadName} ${s.leadPhone} ${s.carrier} ${s.closerName ?? ""}`.toLowerCase().includes(q.trim().toLowerCase()));
  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: { status: (s) => LABEL[s.status] },
  });

  return (
    <>
      <PageHeader
        title="Submission Queue"
        description="Sales submitted by closers. Open a sale to copy its details into the carrier portal, then set its submission status."
      />
      <Card>
        <CardHeader title="Submitted sales" subtitle={queue ? <span className="tabular-nums">{filtered.length} of {queue.length} sale(s)</span> : undefined}
          action={<SearchInput value={q} onChange={setQ} placeholder="Search this queue…" className="w-56" />} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !filtered || filtered.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="No sales to submit" description={q ? "No matches in this queue." : "Sales appear here as soon as a closer completes one."} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH sortDir={dirFor("leadName")} onClick={() => toggle("leadName")}>Customer</TH><TH sortDir={dirFor("agencyName")} onClick={() => toggle("agencyName")}>Agency</TH><TH sortDir={dirFor("carrier")} onClick={() => toggle("carrier")}>Carrier</TH><TH sortDir={dirFor("monthlyPremium")} onClick={() => toggle("monthlyPremium")}>Premium</TH><TH sortDir={dirFor("closerName")} onClick={() => toggle("closerName")}>Closer</TH>
                  <TH sortDir={dirFor("licenseAgentName")} onClick={() => toggle("licenseAgentName")}>
                    <span className="inline-flex items-center gap-1">
                      Agent
                      <InfoHint title="License Agent" side="bottom">
                        The licensed agent of record assigned to the approved policy — the agent who earns the approval commission.
                      </InfoHint>
                    </span>
                  </TH>
                  <TH sortDir={dirFor("status")} onClick={() => toggle("status")}>
                    <span className="inline-flex items-center gap-1">
                      Status
                      <InfoHint title="Submission statuses" side="bottom">
                        Completed (submitted, awaiting review); Approved (validated — approval details captured); Active Paid (funded); No update in commission; Bad Bank; NSF (insufficient funds); Decline; Client Cancelled; Error in application information (banking/payor or identity).
                      </InfoHint>
                    </span>
                  </TH>
                  <TH sortDir={dirFor("soldAt")} onClick={() => toggle("soldAt")}>Sold</TH>
                  {/* Pinned to the right edge so the actions stay visible however wide the table
                      gets — otherwise "Update" scrolls off-screen on narrower viewports. */}
                  <TH className="sticky right-0 bg-ink-50 border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">Actions</TH>
                </TR>
              </THead>
              <TBody>
                {sorted.map((s) => (
                  <TR key={s.saleId}>
                    <TD>
                      <div className="font-medium text-ink-900 whitespace-nowrap">{s.leadName}</div>
                      <div className="font-mono text-xs text-ink-500 whitespace-nowrap tabular-nums">{s.leadPhone}</div>
                    </TD>
                    <TD className="text-sm text-ink-600 max-w-[12rem] truncate">{s.agencyName || "—"}</TD>
                    <TD className="text-sm whitespace-nowrap">{s.carrier}</TD>
                    <TD className="text-sm tabular-nums whitespace-nowrap">{money(s.monthlyPremium)}</TD>
                    <TD className="text-sm text-ink-600 max-w-[12rem] truncate">{s.closerName ?? "—"}</TD>
                    <TD className="text-sm text-ink-600 max-w-[12rem] truncate">{s.licenseAgentName ?? "—"}</TD>
                    <TD>
                      <Badge tone={TONE[s.status]} variant="soft">{LABEL[s.status]}</Badge>
                      {(s.status === "Decline" || s.status === "ErrorInApplicationInformation") && s.declineReason && (
                        <div className="text-xs text-ink-500 mt-0.5 max-w-[16rem] truncate" title={s.declineReason}>{s.declineReason}</div>
                      )}
                    </TD>
                    <TD className="text-sm text-ink-500 whitespace-nowrap tabular-nums">{new Date(s.soldAt).toLocaleDateString()}</TD>
                    <TD className="text-right whitespace-nowrap sticky right-0 bg-white border-l hairline shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
                      <div className="inline-flex items-center justify-end gap-1.5">
                        <Button size="sm" variant="ghost" leftIcon={<Icon name="eye" size={14} />} onClick={() => setViewing(s)}>
                          Open
                        </Button>
                        <Button size="sm" variant="primary" leftIcon={<Icon name="edit" size={14} />} onClick={() => setActive(s)}>
                          Update
                        </Button>
                      </div>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      {active && <UpdateModal sale={active} onClose={() => setActive(null)} />}
      {viewing && <LeadDetailModal leadId={viewing.leadId} title={viewing.leadName} onClose={() => setViewing(null)} />}
    </>
  );
}

/** Read-only, copyable full lead + application — for pasting into the carrier's portal. */
function LeadDetailModal({ leadId, title, onClose }: { leadId: string; title: string; onClose: () => void }) {
  const { data, isLoading } = useGetValidateLeadQuery(leadId);
  const toast = useToast();

  const text = data ? buildCopyText(data) : "";

  async function copyAll() {
    try {
      await navigator.clipboard.writeText(text);
      toast.success("Copied", "Lead details copied to clipboard.");
    } catch {
      toast.error("Couldn't copy", "Your browser blocked clipboard access.");
    }
  }

  return (
    <Modal open onClose={onClose} title={`Lead details — ${title}`}
      description="Copy these into the carrier portal." size="lg"
      footer={
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={onClose}>Close</Button>
          <Button type="button" leftIcon={<Icon name="copy" size={15} />} onClick={copyAll} disabled={!data}>Copy all</Button>
        </div>
      }>
      {isLoading || !data ? <Skeleton className="h-64" /> : (
        <pre className="whitespace-pre-wrap break-words text-xs text-ink-800 bg-ink-50 rounded-lg p-3 max-h-[60vh] overflow-auto font-mono">
          {text}
        </pre>
      )}
    </Modal>
  );
}

function buildCopyText(d: ClosingApplicationView): string {
  const a = d.application;
  const rows: [string, string | number | null | undefined][] = [
    ["Name", a?.name ?? `${d.firstName} ${d.lastName}`],
    ["DOB", (a?.dateOfBirth ?? d.dateOfBirth)?.slice(0, 10)],
    ["Age", a?.age ?? d.ageYears],
    ["Marital status", d.maritalStatus],
    ["Gender", a?.gender],
    ["Address", a?.address ?? d.address],
    ["City", d.city], ["State", d.state], ["Zip", d.postalCode],
    ["Phone", a?.phoneNumber ?? d.phoneNumber], ["Alt phone", a?.altPhone],
    ["Email", a?.email ?? d.email],
    ["SSN", a?.social], ["Driver's licence", a?.driversLicense], ["Born in", a?.bornIn],
    ["Height", a?.height], ["Weight", a?.weight], ["Primary doctor", a?.primaryDoctor],
    ["Health conditions", a?.healthConditions],
    ["Carrier", a?.carrier], ["Plan", a?.plan],
    ["Face amount", a?.faceAmount], ["Premium", a?.premium],
    ["Beneficiary", a?.beneficiary], ["Second beneficiary", a?.secondBeneficiary],
    ["Initial draft date", a?.initialDraftDate?.slice(0, 10)], ["Future draft date", a?.futureDraftDate?.slice(0, 10)],
    ["Account type", a?.accountType], ["Bank name", a?.bankName],
    ["Account number", a?.accountNumber], ["Routing number", a?.routingNumber],
    ["Jornaya LeadiD", d.jornayaLeadId],
  ];
  return rows
    .filter(([, v]) => v !== null && v !== undefined && String(v).trim() !== "")
    .map(([k, v]) => `${k}: ${v}`)
    .join("\n");
}

function UpdateModal({ sale, onClose }: { sale: ValidatorQueueItem; onClose: () => void }) {
  const [save, { isLoading }] = useSetValidatorStatusMutation();
  const toast = useToast();
  const [status, setStatus] = useState<ValidatorStatusValue>(sale.status);
  const [carrierApproved, setCarrierApproved] = useState(sale.carrierApproved ?? sale.carrier ?? "");
  const [coverageApproved, setCoverageApproved] = useState(sale.coverageApproved?.toString() ?? "");
  const [premiumApproved, setPremiumApproved] = useState(sale.premiumApproved?.toString() ?? sale.monthlyPremium?.toString() ?? "");
  const [planApproved, setPlanApproved] = useState(sale.planApproved ?? sale.policyNumber ?? "");
  const [reason, setReason] = useState(sale.declineReason ?? "");
  // Agency → Agent assignment. The License Agent must belong to the sale's agency, so the
  // Agency picker defaults to (and normally stays) the sale's own agency.
  const [agencyId, setAgencyId] = useState(sale.agencyId);
  const [licenseAgentUserId, setLicenseAgentUserId] = useState(sale.licenseAgentUserId ?? "");

  // Agency options: SuperAdmin / central Submission Agents get every agency; agency-scoped
  // validators are forbidden the /options endpoint, so we fall back to the sale's own agency.
  const { data: agencyOptions } = useAgencyOptionsQuery();
  const agencyList = agencyOptions && agencyOptions.length
    ? agencyOptions
    : [{ id: sale.agencyId, name: sale.agencyName }];
  // Dependent Agent picker — populated only once an agency is chosen.
  const { data: licenseAgents, isFetching: agentsLoading } =
    useAgencyLicenseAgentsQuery(agencyId, { skip: !agencyId });

  // Reset editable fields whenever a different sale is opened.
  useEffect(() => {
    setStatus(sale.status);
    setCarrierApproved(sale.carrierApproved ?? sale.carrier ?? "");
    setCoverageApproved(sale.coverageApproved?.toString() ?? "");
    setPremiumApproved(sale.premiumApproved?.toString() ?? sale.monthlyPremium?.toString() ?? "");
    setPlanApproved(sale.planApproved ?? sale.policyNumber ?? "");
    setReason(sale.declineReason ?? "");
    setAgencyId(sale.agencyId);
    setLicenseAgentUserId(sale.licenseAgentUserId ?? "");
  }, [sale]);

  const isError = status === "ErrorInApplicationInformation";
  const isDecline = status === "Decline";

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await save({
        saleId: sale.saleId,
        status,
        carrierApproved: status === "Approved" ? carrierApproved : undefined,
        coverageApproved: status === "Approved" ? parseFloat(coverageApproved) || 0 : undefined,
        premiumApproved: status === "Approved" ? parseFloat(premiumApproved) || 0 : undefined,
        planApproved: status === "Approved" ? planApproved : undefined,
        declineReason: isDecline || isError ? reason : undefined,
        licenseAgentUserId: status === "Approved" && licenseAgentUserId ? licenseAgentUserId : undefined,
      }).unwrap();
      toast.success("Status updated", `${sale.leadName} → ${LABEL[status]}`);
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't update", getErrorDetail(err) ?? "Check the required fields and try again.");
    }
  }

  return (
    <Modal open onClose={onClose} title={`Submission — ${sale.leadName}`} description={`${sale.carrier} · ${money(sale.monthlyPremium)}/mo · closer ${sale.closerName ?? "—"}`} size="lg">
      <form onSubmit={submit} className="space-y-4">
        <div>
          <div className="flex items-center gap-1 mb-1.5">
            <span className="text-[12px] font-medium text-ink-700 leading-none">
              Submission status<span className="text-rose-500 ml-0.5" aria-hidden>*</span>
            </span>
            <InfoHint title="Submission statuses" side="right">
              Completed (submitted, awaiting review); Approved (validated — approval details captured); Active Paid (funded); No update in commission; Bad Bank; NSF (insufficient funds); Decline; Client Cancelled; Error in application information (banking/payor or identity).
            </InfoHint>
          </div>
          <Select required value={status} onChange={(e) => setStatus(e.target.value as ValidatorStatusValue)}>
            {STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </Select>
        </div>

        {status === "Approved" && (
          <>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 rounded-lg border border-ink-200 bg-ink-50/50 p-3">
              <Input label="Carrier Approved" required value={carrierApproved} onChange={(e) => setCarrierApproved(e.target.value)} />
              <Input label="Plan Approved" required value={planApproved} onChange={(e) => setPlanApproved(e.target.value)} />
              <Input label="Coverage Approved" type="number" min={0} step="0.01" required className="tabular-nums" leftIcon={<Icon name="dollar" size={14} />} value={coverageApproved} onChange={(e) => setCoverageApproved(e.target.value)} />
              <Input label="Premium Approved" type="number" min={0} step="0.01" required className="tabular-nums" leftIcon={<Icon name="dollar" size={14} />} value={premiumApproved} onChange={(e) => setPremiumApproved(e.target.value)} />
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 rounded-lg border border-ink-200 bg-ink-50/50 p-3">
              {/* Pick the agency first, then a License Agent from that agency. */}
              <div>
                <div className="flex items-center gap-1 mb-1.5">
                  <span className="text-[12px] font-medium text-ink-700 leading-none">Agency</span>
                  <InfoHint title="Assigning a License Agent" side="right">
                    The License Agent must belong to the selected agency, so Agency defaults to the sale's own. Central submission agents can pick any agency; agency-scoped validators are pinned to their own.
                  </InfoHint>
                </div>
                <Select value={agencyId}
                  onChange={(e) => { setAgencyId(e.target.value); setLicenseAgentUserId(""); }}>
                  {agencyList.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                </Select>
              </div>
              <Select label="License Agent" value={licenseAgentUserId}
                disabled={!agencyId || agentsLoading}
                onChange={(e) => setLicenseAgentUserId(e.target.value)}>
                <option value="">{agentsLoading ? "Loading…" : "Unassigned"}</option>
                {(licenseAgents ?? []).map((a) => (
                  <option key={a.id} value={a.id}>{a.name}{a.isActive ? "" : " (inactive)"}</option>
                ))}
              </Select>
            </div>
          </>
        )}

        {isDecline && (
          <Textarea label="Reason for decline" required value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why was the application declined?" />
        )}

        {isError && (
          <Select label="Application error" required value={reason} onChange={(e) => setReason(e.target.value)}>
            <option value="">Select the error…</option>
            {ERROR_REASONS.map((r) => <option key={r} value={r}>{r}</option>)}
          </Select>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
          <Button type="submit" loading={isLoading} leftIcon={<Icon name="check" size={16} />}>Save status</Button>
        </div>
      </form>
    </Modal>
  );
}
