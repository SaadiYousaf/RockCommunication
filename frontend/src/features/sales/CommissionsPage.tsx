import { API_URL } from "../../shared/config";
import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { useCreatePayrollRunMutation, useMyCommissionsQuery, usePayrollRunsQuery } from "../../shared/api/baseApi";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { isManager } from "../../shared/constants/roles";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { MESSAGES } from "../../shared/constants/messages";
import { SALES_MSG } from "./messages";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, PageHeader,
  SearchInput, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

/** Key used to track the in-flight period export (vs a per-run export keyed by run id). */
const PERIOD_EXPORT = "__period__";


function todayStr(offset = 0) {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return d.toISOString().slice(0, 10);
}

export function CommissionsPage() {
  const auth = useSelector((s: RootState) => s.auth);
  const [from, setFrom] = useState(() => new Date(Date.now() - 30 * 86400 * 1000).toISOString().slice(0, 10));
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10));
  const { data: commissions, isLoading } = useMyCommissionsQuery({ from, to });
  const manager = isManager(auth.user?.roles ?? []);
  const { data: runs } = usePayrollRunsQuery(undefined, { skip: !manager });
  const [createRun, { isLoading: creating }] = useCreatePayrollRunMutation();
  const toast = useToast();
  const confirm = useConfirm();
  // Which export is currently downloading — PERIOD_EXPORT for the range button, or a run id for a row.
  const [exportingKey, setExportingKey] = useState<string | null>(null);

  const total = commissions?.reduce((s, c) => s + c.amount, 0) ?? 0;
  const paid  = commissions?.filter((c) => c.paid).reduce((s, c) => s + c.amount, 0) ?? 0;
  const unpaid = total - paid;

  // Earnings rolled up by rule
  const byRule = useMemo(() => {
    const map = new Map<string, { count: number; amount: number }>();
    (commissions ?? []).forEach((c) => {
      const e = map.get(c.ruleName) ?? { count: 0, amount: 0 };
      e.count++; e.amount += c.amount;
      map.set(c.ruleName, e);
    });
    return Array.from(map.entries())
      .map(([rule, x]) => ({ rule, count: x.count, amount: x.amount }))
      .sort((a, b) => b.amount - a.amount);
  }, [commissions]);

  // Free-text search over the rows already loaded (the stats + "by rule" rollup stay on the full range).
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return commissions ?? [];
    return (commissions ?? []).filter((c) =>
      [c.ruleName, c.note, c.paid ? "Paid" : "Unpaid"]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [commissions, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: { status: (c) => (c.paid ? "Paid" : "Unpaid") },
  });

  const sel = useRowSelection(sorted.map((c) => c.id));

  function exportSelected() {
    const chosen = sorted.filter((c) => sel.isSelected(c.id));
    exportRowsToCsv(chosen, [
      { header: "Agent", value: (c) => c.agentUserId },
      { header: "Sale/customer", value: (c) => c.saleId },
      { header: "Amount", value: (c) => c.amount },
      { header: "Status", value: (c) => (c.paid ? "Paid" : "Unpaid") },
      { header: "Date", value: (c) => new Date(c.earnedAt).toLocaleString() },
    ], `commissions-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(SALES_MSG.exportReadyTitle, SALES_MSG.rowsDownloaded(chosen.length));
  }

  function setRange(days: number) {
    setFrom(todayStr(-days));
    setTo(todayStr(0));
  }

  async function exportPayrollCsv(runId?: string) {
    const params = new URLSearchParams();
    if (runId) params.set("runId", runId);
    else { params.set("from", from); params.set("to", to); }
    // Use the CURRENT tab's token from redux — not localStorage, which is shared across tabs and
    // may hold a different logged-in account, causing a 403 "Export failed" on a valid session.
    const token = auth.accessToken;
    if (!token) { toast.error(SALES_MSG.notAuthenticated); return; }
    setExportingKey(runId ?? PERIOD_EXPORT);
    try {
      const r = await fetch(`${API_URL}/api/sales/payroll-export?${params.toString()}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!r.ok) {
        if (r.status === 403) throw new Error(SALES_MSG.exportPayrollNoPermission);
        throw new Error(SALES_MSG.exportFailedStatus(r.status));
      }
      const blob = await r.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `payroll-${runId ?? `${from}-to-${to}`}.csv`;
      a.click();
      URL.revokeObjectURL(url);
      toast.success(SALES_MSG.exportReadyTitle, SALES_MSG.payrollCsvDownloading);
    } catch (err) {
      toast.error(SALES_MSG.exportFailedTitle, getErrorDetail(err) ?? SALES_MSG.exportFailedTitle);
    } finally {
      setExportingKey(null);
    }
  }

  async function makeRun() {
    const ok = await confirm({
      title: SALES_MSG.runPayrollConfirmTitle,
      description: SALES_MSG.runPayrollConfirmBody(from, to),
      confirmLabel: SALES_MSG.runPayrollConfirmLabel,
    });
    if (!ok) return;
    try {
      await createRun({ periodStart: new Date(from).toISOString(), periodEnd: new Date(to).toISOString() }).unwrap();
      toast.success(SALES_MSG.payrollRunCreatedTitle, SALES_MSG.payrollRunPeriod(from, to));
    } catch (err: unknown) {
      toast.error(SALES_MSG.createPayrollRunFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Revenue"
        title="Commissions"
        description="Review your earnings and let managers run payroll for the period."
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-5">
        <Stat label="Total"  value={`$${total.toFixed(2)}`}  icon={<Icon name="dollar" size={16} />}  tone="brand"
              hint="Everything you earned in the selected date range (paid + unpaid)." />
        <Stat label="Paid"   value={`$${paid.toFixed(2)}`}   icon={<Icon name="success" size={16} />} tone="success"
              hint="Included in a processed payroll run." />
        <Stat label="Unpaid" value={`$${unpaid.toFixed(2)}`} icon={<Icon name="clock" size={16} />}   tone="warning"
              hint="Accrued but not yet in a payroll run." />
      </div>

      <Card className="mb-6">
        <CardBody className="flex flex-wrap items-end gap-4">
          <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} containerClassName="w-44" />
          <Input label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} containerClassName="w-44" />
          <div className="flex gap-1">
            <Button size="sm" variant="ghost" title="Last 7 days" onClick={() => setRange(7)}>7d</Button>
            <Button size="sm" variant="ghost" title="Last 30 days" onClick={() => setRange(30)}>30d</Button>
            <Button size="sm" variant="ghost" title="Last 90 days" onClick={() => setRange(90)}>90d</Button>
          </div>
          <SearchInput
            value={search} onChange={setSearch}
            placeholder={SALES_MSG.commissionsSearchPlaceholder}
            className="w-64" containerClassName="ml-auto"
          />
        </CardBody>
      </Card>

      {byRule.length > 0 && (
        <Card className="mb-6">
          <CardHeader
            title={
              <span className="inline-flex items-center gap-1">
                Earnings by rule
                <InfoHint title="Commission rules" side="bottom">
                  Each commission is generated by a named rule (e.g. closer flat rate, validator bonus,
                  license-agent approval) when a sale is closed/funded/assigned.
                </InfoHint>
              </span>
            }
            subtitle="Where your commission came from in this period."
          />
          <CardBody className="pt-0">
            <div className="space-y-2">
              {byRule.map((r) => {
                const pct = total > 0 ? Math.round((r.amount / total) * 100) : 0;
                return (
                  <div key={r.rule} className="flex items-center gap-3">
                    <div className="font-mono text-xs text-ink-700 w-44 truncate">{r.rule}</div>
                    <div className="flex-1 h-2 rounded-full bg-ink-100 overflow-hidden">
                      <div className="h-full rounded-full bg-brand-500 transition-all" style={{ width: `${pct}%` }} />
                    </div>
                    <div className="text-xs text-ink-600 w-12 text-right tabular-nums">{r.count}×</div>
                    <div className="text-sm font-semibold text-ink-900 w-20 text-right tabular-nums">${r.amount.toFixed(2)}</div>
                    <div className="text-xs text-ink-500 w-10 text-right tabular-nums">{pct}%</div>
                  </div>
                );
              })}
            </div>
          </CardBody>
        </Card>
      )}

      {isLoading ? (
        <Card className="mb-6"><CardBody>
          {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-10 mb-2" />)}
        </CardBody></Card>
      ) : !commissions || commissions.length === 0 ? (
        <Card className="mb-6"><CardBody>
          <EmptyState
            icon={<Icon name="doc" size={20} />}
            title={SALES_MSG.noCommissionsTitle}
            description={SALES_MSG.noCommissionsBody}
          />
        </CardBody></Card>
      ) : sorted.length === 0 ? (
        <Card className="mb-6"><CardBody>
          <EmptyState
            icon={<Icon name="search" size={20} />}
            title={SALES_MSG.noCommissionsMatchTitle}
            description={SALES_MSG.noCommissionsMatchBody}
          />
        </CardBody></Card>
      ) : (
        <>
        <Table>
          <THead>
            <TR>
              <TH className="w-10"><Checkbox aria-label="Select all commissions" {...sel.allCheckboxProps} /></TH>
              <TH sortDir={dirFor("earnedAt")} onClick={() => toggle("earnedAt")}>Earned</TH>
              <TH sortDir={dirFor("ruleName")} onClick={() => toggle("ruleName")}>Rule</TH>
              <TH numeric sortDir={dirFor("amount")} onClick={() => toggle("amount")}>Amount</TH>
              <TH sortDir={dirFor("status")} onClick={() => toggle("status")}>
                <span className="inline-flex items-center gap-1">
                  Status
                  <InfoHint title="Paid vs Unpaid" side="top">
                    Paid = included in a processed payroll run; Unpaid = accrued but not yet run.
                    A payroll run freezes that period's commissions.
                  </InfoHint>
                </span>
              </TH>
              <TH sortDir={dirFor("note")} onClick={() => toggle("note")}>Note</TH>
            </TR>
          </THead>
          <TBody>
            {sorted.map((c) => (
              <TR key={c.id} className={sel.isSelected(c.id) ? "bg-brand-50/40" : undefined}>
                <TD>
                  <Checkbox aria-label={`Select commission ${c.ruleName}`} {...sel.checkboxProps(c.id)} />
                </TD>
                <TD className="text-ink-600 whitespace-nowrap tabular-nums">{new Date(c.earnedAt).toLocaleString()}</TD>
                <TD className="font-mono text-xs text-ink-700">{c.ruleName}</TD>
                <TD numeric className="font-semibold text-ink-900 tabular-nums whitespace-nowrap">${c.amount.toFixed(2)}</TD>
                <TD>
                  {c.paid
                    ? <Badge tone="success" variant="soft" dot>Paid</Badge>
                    : <Badge tone="warning" variant="soft" dot>Unpaid</Badge>}
                </TD>
                <TD className="text-ink-600">{c.note ?? <span className="text-ink-400">—</span>}</TD>
              </TR>
            ))}
          </TBody>
        </Table>
        <BulkActionBar
          count={sel.selectedCount} itemNoun="commission" onClear={sel.clear}
          actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
        />
        </>
      )}

      {manager && (
        <Card className="mt-6">
          <CardHeader
            title="Payroll runs"
            subtitle="Generate payroll for the selected period."
            action={
              <div className="flex gap-2">
                <Button variant="ghost" leftIcon={<Icon name="download" size={16} />}
                  loading={exportingKey === PERIOD_EXPORT}
                  title="Download a CSV of all commissions in the selected date range"
                  onClick={() => exportPayrollCsv()}>
                  Export period CSV
                </Button>
                <Button onClick={makeRun} loading={creating} leftIcon={<Icon name="plus" size={16} />}>
                  Run payroll
                </Button>
              </div>
            }
          />
          <CardBody className="pt-0 px-0">
            {!runs || runs.length === 0 ? (
              <div className="px-5 pb-5">
                <EmptyState
                  icon={<Icon name="card" size={20} />}
                  title={SALES_MSG.noPayrollRunsTitle}
                  description={SALES_MSG.noPayrollRunsBody}
                />
              </div>
            ) : (
              <Table className="border-0 shadow-none rounded-none">
                <THead>
                  <TR>
                    <TH>Period</TH>
                    <TH numeric>Total</TH>
                    <TH>
                      <span className="inline-flex items-center gap-1">
                        Status
                        <InfoHint title="Payroll run status" side="top">
                          Processed = payroll finalized, with its commissions frozen and marked paid.
                          Anything else is still a draft that hasn't paid out.
                        </InfoHint>
                      </span>
                    </TH>
                    <TH>Processed</TH>
                    <TH className="text-right">Export</TH>
                  </TR>
                </THead>
                <TBody>
                  {runs.map((r) => (
                    <TR key={r.id}>
                      <TD className="text-ink-700 whitespace-nowrap tabular-nums">{r.periodStart.slice(0, 10)} → {r.periodEnd.slice(0, 10)}</TD>
                      <TD numeric className="font-semibold text-ink-900 tabular-nums whitespace-nowrap">${r.totalAmount.toFixed(2)}</TD>
                      <TD><Badge tone={r.status === "Processed" ? "success" : "neutral"} variant="soft">{r.status}</Badge></TD>
                      <TD className="text-ink-500 text-xs whitespace-nowrap tabular-nums">
                        {r.processedAt ? new Date(r.processedAt).toLocaleString() : "—"}
                      </TD>
                      <TD>
                        <div className="flex justify-end">
                          <Button variant="ghost" size="sm" leftIcon={<Icon name="download" size={14} />}
                            loading={exportingKey === r.id}
                            title="Download this payroll run as CSV"
                            onClick={() => exportPayrollCsv(r.id)}>CSV</Button>
                        </div>
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </CardBody>
        </Card>
      )}
    </>
  );
}
