import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { useListRetentionPoliciesQuery, useResolveRetentionMutation } from "../../shared/api/baseApi";
import type { RetentionPolicy } from "../../shared/api/types";
import { useTableSort } from "../../shared/hooks/useTableSort";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Modal, PageHeader, SearchInput,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";
import {
  RETENTION_MSG, RETENTION_TARGET_STATUSES, retentionStatusLabel, retentionStatusTone,
} from "./messages";

const money = (n: number) => `$${n.toFixed(2)}`;
const shortDate = (iso: string) => new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });

/**
 * Retention worklist. Shows ONLY policies that went bad post-submission (bad bank, NSF, cancelled,
 * declined, application error), scoped to the signed-in agent's agency/call center. Each row can be
 * "worked": the agent updates the policy's status (e.g. mark it recovered) and leaves a note.
 */
export function RetentionPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const { data: policies, isLoading } = useListRetentionPoliciesQuery();

  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return policies ?? [];
    return (policies ?? []).filter((p) =>
      [p.leadName, p.carrier, p.policyNumber, p.leadPhone, p.closerName]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [policies, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    key: "soldAt", dir: "desc",
    accessors: { premium: (p: RetentionPolicy) => p.monthlyPremium, status: (p: RetentionPolicy) => p.status },
  });

  const stats = useMemo(() => {
    const list = policies ?? [];
    return { open: list.length, premium: list.reduce((sum, p) => sum + (p.monthlyPremium ?? 0), 0) };
  }, [policies]);

  const [working, setWorking] = useState<RetentionPolicy | null>(null);

  return (
    <>
      <PageHeader eyebrow={RETENTION_MSG.eyebrow} title={RETENTION_MSG.title} description={RETENTION_MSG.description} />

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 mb-5">
        <Stat label={RETENTION_MSG.statOpen} value={stats.open} icon={<Icon name="refresh" size={18} />}
          hint={RETENTION_MSG.statOpenHint} tone="warning" />
        <Stat label={RETENTION_MSG.statPremium} value={money(stats.premium)} icon={<Icon name="dollar" size={18} />}
          hint={RETENTION_MSG.statPremiumHint} tone="brand" />
      </div>

      <Card>
        <CardHeader
          title={RETENTION_MSG.title}
          subtitle={policies ? `${policies.length} ${policies.length === 1 ? "policy" : "policies"}` : undefined}
          action={<SearchInput value={search} onChange={setSearch} placeholder={RETENTION_MSG.searchPlaceholder} className="w-64" />}
        />
        <CardBody>
          {isLoading ? (
            <Skeleton className="h-40" />
          ) : !policies || policies.length === 0 ? (
            <EmptyState icon={<Icon name="refresh" size={20} />} title={RETENTION_MSG.emptyTitle} description={RETENTION_MSG.emptyDesc} />
          ) : sorted.length === 0 ? (
            <EmptyState icon={<Icon name="search" size={20} />} title={RETENTION_MSG.noMatchTitle} description={RETENTION_MSG.noMatchDesc} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH>{RETENTION_MSG.colPolicy}</TH>
                  <TH>{RETENTION_MSG.colCustomer}</TH>
                  <TH>{RETENTION_MSG.colCarrier}</TH>
                  <TH numeric sortDir={dirFor("premium")} onClick={() => toggle("premium")}>{RETENTION_MSG.colPremium}</TH>
                  <TH sortDir={dirFor("status")} onClick={() => toggle("status")}>{RETENTION_MSG.colStatus}</TH>
                  <TH>{RETENTION_MSG.colCloser}</TH>
                  <TH sortDir={dirFor("soldAt")} onClick={() => toggle("soldAt")}>{RETENTION_MSG.colSold}</TH>
                  <TH></TH>
                </TR>
              </THead>
              <TBody>
                {sorted.map((p) => (
                  <TR key={p.saleId} className="hover:bg-ink-50/60 transition-colors">
                    <TD className="whitespace-nowrap">
                      <div className="font-medium text-ink-900">#{p.saleNumber}</div>
                      {p.policyNumber && <div className="text-xs text-ink-500 font-mono">{p.policyNumber}</div>}
                    </TD>
                    <TD>
                      <button type="button" onClick={() => navigate(`/leads/${p.leadId}`)}
                        className="text-left font-medium text-brand-600 hover:text-brand-700 hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/30 rounded">
                        {p.leadName || "—"}
                      </button>
                      <div className="text-xs text-ink-500">{[p.leadPhone, p.state].filter(Boolean).join(" · ") || "—"}</div>
                    </TD>
                    <TD className="text-sm text-ink-700">{p.carrier}</TD>
                    <TD numeric className="tabular-nums">{money(p.monthlyPremium)}</TD>
                    <TD>
                      <Badge tone={retentionStatusTone(p.status)} variant="soft">{retentionStatusLabel(p.status)}</Badge>
                      {p.declineReason && <div className="text-xs text-ink-500 mt-0.5 max-w-[14rem] truncate" title={p.declineReason}>{p.declineReason}</div>}
                    </TD>
                    <TD className="text-sm text-ink-600">{p.closerName ?? "—"}</TD>
                    <TD className="text-sm text-ink-500 whitespace-nowrap">{shortDate(p.soldAt)}</TD>
                    <TD className="text-right">
                      <Button size="sm" variant="outline" leftIcon={<Icon name="refresh" size={14} />}
                        onClick={() => setWorking(p)}>{RETENTION_MSG.workCta}</Button>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      {working && (
        <WorkPolicyModal policy={working} onClose={() => setWorking(null)}
          onResolved={(name) => { toast.success(RETENTION_MSG.resolvedOk, RETENTION_MSG.resolvedDesc(name)); setWorking(null); }}
          onError={(detail) => toast.error(RETENTION_MSG.resolveFailed, detail)} />
      )}
    </>
  );
}

/** The "work this policy" modal — pick a new status, add a note, save. */
function WorkPolicyModal({ policy, onClose, onResolved, onError }: {
  policy: RetentionPolicy;
  onClose: () => void;
  onResolved: (leadName: string) => void;
  onError: (detail: string) => void;
}) {
  const [resolve, { isLoading }] = useResolveRetentionMutation();
  const [newStatus, setNewStatus] = useState<string>("ActivePaid");
  const [note, setNote] = useState("");
  const [noteError, setNoteError] = useState(false);

  const noteMandatory = newStatus === "Decline" || newStatus === "ErrorInApplicationInformation";
  const name = policy.leadName || `#${policy.saleNumber}`;

  async function save() {
    if (noteMandatory && !note.trim()) { setNoteError(true); return; }
    try {
      await resolve({ saleId: policy.saleId, newStatus, note: note.trim() || null, leadId: policy.leadId }).unwrap();
      onResolved(name);
    } catch (err: unknown) {
      onError(getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <Modal open onClose={onClose} title={RETENTION_MSG.workTitle(name)}
      description={`#${policy.saleNumber} · ${policy.carrier} · ${money(policy.monthlyPremium)}/mo`}>
      <div className="space-y-4">
        <div className="flex items-center gap-2 text-sm">
          <span className="text-ink-500">{RETENTION_MSG.currentStatus}:</span>
          <Badge tone={retentionStatusTone(policy.status)} variant="soft">{retentionStatusLabel(policy.status)}</Badge>
        </div>

        <Select label={RETENTION_MSG.newStatusLabel} value={newStatus}
          onChange={(e) => { setNewStatus(e.target.value); setNoteError(false); }}>
          {RETENTION_TARGET_STATUSES.map((s) => <option key={s} value={s}>{retentionStatusLabel(s)}</option>)}
        </Select>

        <div>
          <Textarea label={RETENTION_MSG.noteLabel} value={note}
            onChange={(e) => { setNote(e.target.value); if (e.target.value.trim()) setNoteError(false); }}
            placeholder={RETENTION_MSG.notePlaceholder} rows={3} maxLength={1000} />
          <p className={"text-xs mt-1 " + (noteError ? "text-rose-600" : "text-ink-500")}>
            {noteError ? RETENTION_MSG.noteRequired : RETENTION_MSG.noteHelp}
          </p>
        </div>

        <div className="flex justify-end gap-2 pt-1">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button loading={isLoading} onClick={save} leftIcon={<Icon name="save" size={15} />}>{RETENTION_MSG.saveCta}</Button>
        </div>
      </div>
    </Modal>
  );
}
