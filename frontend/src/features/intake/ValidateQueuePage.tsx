import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useState } from "react";
import {
  useSetValidatorStatusMutation, useValidatorQueueQuery, useGetValidateLeadQuery,
  useSetSubmissionCommentMutation,
  useAgencyOptionsQuery, useAgencyLicenseAgentsQuery,
} from "../../shared/api/baseApi";
import type { ValidatorQueueItem, ValidatorStatusValue, ClosingApplicationView } from "../../shared/api/types";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Select, SensitiveValue, Skeleton, Stat, Stepper, Table, TBody, TD, TH, THead, TR, Textarea, cn, useToast,
} from "../../shared/ui";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import {
  VALIDATOR_STATUSES as STATUSES,
  VALIDATOR_ERROR_REASONS as ERROR_REASONS,
  VALIDATOR_STATUS_LABEL as LABEL,
  VALIDATOR_STATUS_TONE as TONE,
} from "../../shared/constants/intake";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { formatUsd, formatPhone } from "../../shared/lib/format";
import { INTAKE_PIPELINE, PIPELINE_STEP } from "../../shared/constants/pipeline";
import { INTAKE_MSG } from "./messages";
import { StatusFilterBar } from "../../shared/components/StatusFilterBar";

/** Submission queue — every submitted sale, worked through the validator statuses. */
export function ValidateQueuePage() {
  // Poll: sales arrive as closers submit them, so the submission queue stays live across users.
  const { data: queue, isLoading } = useValidatorQueueQuery(undefined, { pollingInterval: 30_000, skipPollingIfUnfocused: true });
  const [active, setActive] = useState<ValidatorQueueItem | null>(null);
  const [viewing, setViewing] = useState<ValidatorQueueItem | null>(null);
  const [q, setQ] = useState("");
  const [statusFilter, setStatusFilter] = useState<ValidatorStatusValue | null>(null);
  const [agentFilter, setAgentFilter] = useState("");
  // The sale whose note is open, and the draft being typed into it.
  const [commenting, setCommenting] = useState<ValidatorQueueItem | null>(null);
  const [commentDraft, setCommentDraft] = useState("");
  const [saveComment, { isLoading: savingComment }] = useSetSubmissionCommentMutation();

  // Everyone who appears on a row, in any of the three roles a submitted sale carries. A manager
  // asking "what's going on with Laraib" means all of it — the sales she closed, the ones she is
  // submitting, and the ones written under her licence — not whichever column happens to be hers.
  const agentOptions = useMemo(() => {
    const counts = new Map<string, number>();
    for (const s of queue ?? []) {
      for (const name of new Set([s.closerName, s.validatorName, s.licenseAgentName])) {
        if (name) counts.set(name, (counts.get(name) ?? 0) + 1);
      }
    }
    const list = [...counts.entries()].sort((a, b) => a[0].localeCompare(b[0]));

    // Keep the chosen agent listed even once their last row leaves the queue. Without this the
    // select falls back to showing "All agents" while the filter is still applied, so the table
    // looks empty for no visible reason.
    if (agentFilter && !counts.has(agentFilter)) list.push([agentFilter, 0]);
    return list;
  }, [queue, agentFilter]);

  const involves = (s: ValidatorQueueItem, name: string) =>
    s.closerName === name || s.validatorName === name || s.licenseAgentName === name;

  const filtered = (queue ?? []).filter((s) =>
    (statusFilter === null || s.status === statusFilter) &&
    (!agentFilter || involves(s, agentFilter)) &&
    (!q.trim() || `${s.leadName} ${s.leadPhone} ${s.carrier} ${s.closerName ?? ""}`.toLowerCase().includes(q.trim().toLowerCase())));
  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: { status: (s) => LABEL[s.status] },
  });
  const total = queue?.length ?? 0;
  const approved = (queue ?? []).filter((s) => s.status === "Approved" || s.status === "ActivePaid").length;
  // Premium that is actually worth something: carrier-approved, plus what Head Office is still
  // carrying. Summing the whole queue counted declines, bad banks and NSFs as revenue, so the
  // figure a submission agent read all day was never money anyone was going to see.
  //
  // ActivePaid is included because it IS approved — and paid. Leaving it out would make the total
  // DROP as sales progressed, which is the opposite of what the card is for.
  // Approved sales only (owner's instruction, 11 Oct 2026 — this started as the whole queue, then
  // briefly included Referred to HO). Nothing with an outcome short of approval counts: a decline,
  // a bad bank or a sale still sitting with Head Office is not money anyone is going to see.
  //
  // The amount is the carrier's APPROVED premium, not what the closer recorded at the point of
  // sale — those differ, and the approved figure is the one the carrier will actually draft.
  const premiumTotal = (queue ?? [])
    .filter((s) => s.status === "Approved")
    .reduce((sum, s) => sum + (s.premiumApproved ?? s.monthlyPremium ?? 0), 0);
  const toast = useToast();
  const sel = useRowSelection(sorted.map((s) => s.saleId));

  function exportSelected() {
    const chosen = sorted.filter((s) => sel.isSelected(s.saleId));
    exportRowsToCsv(chosen, [
      { header: "Customer", value: (s) => s.leadName },
      { header: "Carrier", value: (s) => s.carrier },
      { header: "Plan", value: (s) => s.planApproved ?? s.policyNumber ?? "" },
      { header: "Premium", value: (s) => formatUsd(s.monthlyPremium) },
      { header: "Status", value: (s) => LABEL[s.status] },
      { header: "Agency", value: (s) => s.agencyName },
    ], `submission-queue-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(INTAKE_MSG.exportReadyTitle, INTAKE_MSG.exportRows(chosen.length));
  }

  return (
    <>
      <PageHeader
        eyebrow="Submission"
        title="Submission Queue"
        description="Sales submitted by closers. Open a sale to copy its details into the carrier portal, then set its submission status."
      />
      <Stepper steps={INTAKE_PIPELINE} currentIndex={PIPELINE_STEP.submit} className="mb-5 max-w-2xl" />
      {queue && total > 0 && (
        <div className="grid grid-cols-2 md:grid-cols-3 gap-4 mb-5 stagger-children">
          <Stat className="stagger-item" label="To submit" value={total} icon={<Icon name="inbox" size={16} />} tone="brand" hint="Sales in the queue" />
          <Stat className="stagger-item" label="Approved" value={approved} icon={<Icon name="check" size={16} />} tone="success" hint="Approved or active paid" />
          <Stat className="stagger-item" label="Premium / mo" value={formatUsd(premiumTotal)} icon={<Icon name="dollar" size={16} />} tone="accent" hint={INTAKE_MSG.premiumApprovedHint} />
        </div>
      )}
      <Card>
        <CardHeader title="Submitted sales" subtitle={queue ? <span className="tabular-nums">{filtered.length} of {queue.length} {queue.length === 1 ? "sale" : "sales"}</span> : undefined}
          action={<SearchInput value={q} onChange={setQ} placeholder={INTAKE_MSG.queueSearchPlaceholder} className="w-56" />} />
        <CardBody>
          {/* Counts come from the whole queue, not the search result, so the chips keep telling the
              truth about the workload while someone is searching inside it. */}
          {queue && total > 0 && (
            <div className="mb-4 flex flex-wrap items-center gap-3">
              <StatusFilterBar
                rows={queue}
                statusOf={(s) => s.status}
                options={STATUSES.map((o) => ({ value: o.value, label: o.label, tone: TONE[o.value] }))}
                value={statusFilter}
                onChange={setStatusFilter}
                className="min-w-0 flex-1"
              />
              {agentOptions.length > 0 && (
                <div className="flex items-center gap-1.5 shrink-0">
                  <Select
                    aria-label={INTAKE_MSG.agentFilterLabel}
                    value={agentFilter}
                    onChange={(e) => setAgentFilter(e.target.value)}
                    className="h-9 w-52 text-sm"
                  >
                    <option value="">{INTAKE_MSG.agentFilterAll}</option>
                    {agentOptions.map(([name, count]) => (
                      <option key={name} value={name}>{name} ({count})</option>
                    ))}
                  </Select>
                  <InfoHint title={INTAKE_MSG.agentFilterLabel} side="left">
                    {INTAKE_MSG.agentFilterHint}
                  </InfoHint>
                </div>
              )}
            </div>
          )}
          {isLoading ? <Skeleton className="h-40" /> : !filtered || filtered.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title={INTAKE_MSG.validateEmptyTitle}
              description={q || statusFilter || agentFilter ? INTAKE_MSG.noMatches : INTAKE_MSG.validateEmptyDesc} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH>
                  <TH sortDir={dirFor("leadName")} onClick={() => toggle("leadName")}>Customer</TH><TH sortDir={dirFor("agencyName")} onClick={() => toggle("agencyName")}>Agency</TH><TH sortDir={dirFor("carrier")} onClick={() => toggle("carrier")}>Carrier</TH><TH sortDir={dirFor("monthlyPremium")} onClick={() => toggle("monthlyPremium")}><span className="inline-flex items-center gap-1">Premium<InfoHint title="Monthly premium" side="bottom">The amount the customer pays each month for this policy.</InfoHint></span></TH><TH sortDir={dirFor("closerName")} onClick={() => toggle("closerName")}>Closer</TH>
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
                  <TR key={s.saleId} className={sel.isSelected(s.saleId) ? "bg-brand-50/40" : undefined}>
                    <TD><Checkbox aria-label={`Select ${s.leadName}`} {...sel.checkboxProps(s.saleId)} /></TD>
                    <TD>
                      <div className="font-medium text-ink-900 whitespace-nowrap">{s.leadName}</div>
                      <div className="font-mono text-xs text-ink-500 whitespace-nowrap tabular-nums">{formatPhone(s.leadPhone)}</div>
                    </TD>
                    <TD className="text-sm text-ink-600 max-w-[12rem] truncate">{s.agencyName || "—"}</TD>
                    <TD className="text-sm whitespace-nowrap">{s.carrier}</TD>
                    <TD className="text-sm tabular-nums whitespace-nowrap">
                      <span className="inline-flex items-center gap-1.5">
                        {formatUsd(s.monthlyPremium)}
                        {/* Beside the premium, where the submission agent is already looking.
                            Filled notes get a solid icon so a glance down the column shows which
                            sales have something written on them. */}
                        <button
                          type="button"
                          aria-label={s.submissionComment ? INTAKE_MSG.commentEdit : INTAKE_MSG.commentAdd}
                          title={s.submissionComment || INTAKE_MSG.commentAdd}
                          onClick={(e) => {
                            e.stopPropagation();
                            setCommenting(s);
                            setCommentDraft(s.submissionComment ?? "");
                          }}
                          className={cn(
                            "rounded p-1 transition-colors",
                            s.submissionComment
                              ? "text-brand-600 hover:bg-brand-50"
                              : "text-ink-300 hover:text-ink-600 hover:bg-ink-100",
                          )}
                        >
                          <Icon name="chat" size={14} />
                        </button>
                      </span>
                    </TD>
                    <TD className="text-sm text-ink-600 max-w-[12rem] truncate">{s.closerName ?? "—"}</TD>
                    <TD className="text-sm text-ink-600 max-w-[12rem] truncate">{s.licenseAgentName ?? "—"}</TD>
                    <TD>
                      <Badge tone={TONE[s.status]} variant="soft">{LABEL[s.status]}</Badge>
                      {(s.status === "Decline" || s.status === "ErrorInApplicationInformation" || s.status === "BadCustomer") && s.declineReason && (
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
          <BulkActionBar count={sel.selectedCount} itemNoun="sale" onClear={sel.clear}
            actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]} />
        </CardBody>
      </Card>

      {active && <UpdateModal sale={active} onClose={() => setActive(null)} />}
      {viewing && <LeadDetailModal leadId={viewing.leadId} title={viewing.leadName} onClose={() => setViewing(null)} />}

      {/* The note. Optional throughout — saving an empty box clears it. */}
      <Modal
        open={commenting !== null}
        onClose={() => setCommenting(null)}
        title={commenting ? INTAKE_MSG.commentTitle(commenting.leadName) : ""}
        description={INTAKE_MSG.commentDescription}
        footer={
          <>
            <Button variant="ghost" onClick={() => setCommenting(null)}>Cancel</Button>
            <Button
              loading={savingComment}
              onClick={async () => {
                if (!commenting) return;
                try {
                  await saveComment({
                    saleId: commenting.saleId,
                    comment: commentDraft.trim() || null,
                  }).unwrap();
                  toast.success(commentDraft.trim() ? INTAKE_MSG.commentSaved : INTAKE_MSG.commentCleared);
                  setCommenting(null);
                } catch (err: unknown) {
                  toast.error(INTAKE_MSG.commentFailed, getErrorDetail(err) ?? INTAKE_MSG.retry);
                }
              }}
            >{INTAKE_MSG.commentSave}</Button>
          </>
        }
      >
        <Textarea
          label={INTAKE_MSG.commentLabel}
          hint={INTAKE_MSG.commentHint}
          rows={5}
          value={commentDraft}
          onChange={(e) => setCommentDraft(e.target.value)}
          placeholder={INTAKE_MSG.commentPlaceholder}
          autoFocus
        />
      </Modal>
    </>
  );
}

/** Read-only, copyable full lead + application — for pasting into the carrier's portal. */
function LeadDetailModal({ leadId, title, onClose }: { leadId: string; title: string; onClose: () => void }) {
  const { data, isLoading } = useGetValidateLeadQuery(leadId);
  const toast = useToast();

  const rows = data ? detailRows(data) : [];
  const text = rowsToText(rows);

  async function copyAll() {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(INTAKE_MSG.copiedTitle, INTAKE_MSG.copiedDesc);
    } catch {
      toast.error(INTAKE_MSG.copyFailedTitle, INTAKE_MSG.copyFailedDesc);
    }
  }

  return (
    <Modal open onClose={onClose} title={`Lead details — ${title}`}
      description="Copy these into the carrier portal." size="lg"
      footer={
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={onClose}>Close</Button>
          <Button type="button" leftIcon={<Icon name="copy" size={15} />} onClick={copyAll} disabled={!data} title={!data ? "Loading lead details…" : "Copy every field to the clipboard to paste into the carrier portal"}>Copy all</Button>
        </div>
      }>
      {isLoading || !data ? <Skeleton className="h-64" /> : (
        <div className="max-h-[60vh] overflow-auto pr-1 divide-y divide-ink-100">
          {rows.map(([k, v]) => (
            <div key={k} className="flex items-start justify-between gap-4 py-1.5 text-sm">
              <span className="text-ink-500 shrink-0">{k}</span>
              {SENSITIVE_LABELS.has(k)
                ? <SensitiveValue value={String(v)} className="text-right" />
                : <span className="text-ink-800 font-medium text-right break-words">{String(v)}</span>}
            </div>
          ))}
        </div>
      )}
    </Modal>
  );
}

// Labels whose values are regulated PII — masked by default in the UI (eye to reveal), full value
// still available via "Copy all" for the carrier portal.
const SENSITIVE_LABELS = new Set(["SSN", "Driver's licence", "Account number", "Routing number"]);

type DetailRow = [string, string | number | null | undefined];

function detailRows(d: ClosingApplicationView): DetailRow[] {
  const a = d.application;
  const rows: DetailRow[] = [
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
  return rows.filter(([, v]) => v !== null && v !== undefined && String(v).trim() !== "");
}

function rowsToText(rows: DetailRow[]): string {
  return rows.map(([k, v]) => `${k}: ${v}`).join("\n");
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
  // The note IS the status — "bad customer" with nothing written down warns nobody.
  const isBadCustomer = status === "BadCustomer";

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
        declineReason: isDecline || isError || isBadCustomer ? reason : undefined,
        licenseAgentUserId: status === "Approved" && licenseAgentUserId ? licenseAgentUserId : undefined,
      }).unwrap();
      toast.success(INTAKE_MSG.statusUpdatedTitle, INTAKE_MSG.statusUpdatedDesc(sale.leadName, LABEL[status]));
      onClose();
    } catch (err: unknown) {
      toast.error(INTAKE_MSG.updateFailedTitle, getErrorDetail(err) ?? INTAKE_MSG.checkRequiredFields);
    }
  }

  return (
    <Modal open onClose={onClose} title={`Submission — ${sale.leadName}`} description={`${sale.carrier} · ${formatUsd(sale.monthlyPremium)}/mo · closer ${sale.closerName ?? "—"}`} size="lg">
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
              <div className="sm:col-span-2 flex items-center gap-1">
                <span className="text-[12px] font-semibold text-ink-700 leading-none">Approved details</span>
                <InfoHint title="Approved details" side="right">
                  The final terms the carrier approved — these can differ from what the closer submitted. Coverage Approved = the death benefit (face amount); Premium Approved = the monthly payment.
                </InfoHint>
              </div>
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
                title={agentsLoading ? "Loading agents for this agency…" : !agencyId ? "Choose an agency first to list its agents" : undefined}
                onChange={(e) => setLicenseAgentUserId(e.target.value)}>
                <option value="">{agentsLoading ? "Loading…" : "Unassigned"}</option>
                {(licenseAgents ?? []).map((a) => (
                  <option key={a.id} value={a.id}>{a.name}{a.isActive ? "" : " (inactive)"}</option>
                ))}
              </Select>
            </div>
          </>
        )}

        {isBadCustomer && (
          <Textarea
            label={INTAKE_MSG.badCustomerNotesLabel}
            hint={INTAKE_MSG.badCustomerNotesHint}
            required
            rows={3}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder={INTAKE_MSG.badCustomerNotesPlaceholder}
          />
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
