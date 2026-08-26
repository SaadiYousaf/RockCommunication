import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { API_URL } from "../../shared/config";
import {
  useListPayrollQuery, useSavePayrollMutation, useListCallCentersQuery,
  useGetPayrollConfigQuery, useSavePayrollConfigMutation,
} from "../../shared/api/baseApi";
import type { PayrollRow, SavePayrollInput, PayrollConfig, SavePayrollConfigInput } from "../../shared/api/types";
import {
  Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { HR_MSG } from "./messages";

// Salaries are paid in PKR.
const money = (n: number | null | undefined) =>
  n == null ? "—" : "PKR " + Math.round(n).toLocaleString();

// The deduction rates used when an employee isn't tied to a call centre (agency-wide) — mirrors
// the backend's PayrollConfigDefaults so the modal preview matches what the server will compute.
const DEFAULT_DEDUCTION_RATES = { lateComingFine: 0, halfDayFactor: 0.5, absentDayFactor: 1.0, ncnsFactor: 2.0 };

/**
 * Auto-derive the four attendance-driven deduction amounts from a call centre's rules (or the
 * agency-wide defaults). A day's pay = basic ÷ working days; half/absent/NCNS are multiples of it,
 * late is a flat fine. This mirrors the backend exactly — the server is the source of truth, so
 * these amounts are always recomputed and never edited by hand.
 */
function autoDeductions(f: SavePayrollInput, cfg: PayrollConfig | undefined) {
  const c = cfg ?? DEFAULT_DEDUCTION_RATES;
  const perDay = f.workingDays > 0 ? f.basicSalary / f.workingDays : 0;
  const r = (v: number) => Math.round(v);
  return {
    lateComingAmount: r(f.lateComing * c.lateComingFine),
    halfDaysAmount: r(f.halfDays * perDay * c.halfDayFactor),
    absentDaysAmount: r(f.absentDays * perDay * c.absentDayFactor),
    ncnsAmount: r(f.ncns * perDay * c.ncnsFactor),
  };
}

const toInput = (r: PayrollRow): SavePayrollInput => ({
  basicSalary: r.basicSalary, punctuality: r.punctuality, dailyBonus: r.dailyBonus,
  monthlyCommissions: r.monthlyCommissions, transportAllowance: r.transportAllowance,
  specialAllowance: r.specialAllowance, advanceSalary: r.advanceSalary, docks: r.docks,
  workingDays: r.workingDays, presentDays: r.presentDays, leavesApproved: r.leavesApproved,
  lateComing: r.lateComing, halfDays: r.halfDays, absentDays: r.absentDays, ncns: r.ncns,
  lateComingAmount: r.lateComingAmount, halfDaysAmount: r.halfDaysAmount,
  absentDaysAmount: r.absentDaysAmount, ncnsAmount: r.ncnsAmount,
  notes: r.notes ?? "", finalized: r.finalized,
});

/**
 * HR → Payroll. Per-employee monthly pay: attendance counts + commission are auto-derived,
 * basic salary + advance carry from last month, HR fills the rest, and each row exports a
 * PDF salary slip.
 */
export function PayrollPage() {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  // Default the call-centre filter to the admin's chosen workspace (re-sync on switch).
  const scopedCallCenter = useSelector((s: RootState) => s.auth.user?.callCenterId) ?? "";
  const [callCenterId, setCallCenterId] = useState(scopedCallCenter);
  useEffect(() => { setCallCenterId(scopedCallCenter); }, [scopedCallCenter]);
  const [search, setSearch] = useState("");
  const monthValue = `${year}-${String(month).padStart(2, "0")}`;

  const { data: rows, isLoading } = useListPayrollQuery({ year, month, callCenterId: callCenterId || undefined });
  const { data: callCenters } = useListCallCentersQuery();
  const [save, { isLoading: saving }] = useSavePayrollMutation();
  const toast = useToast();
  const token = useSelector((s: RootState) => s.auth.accessToken) ?? "";

  const [editing, setEditing] = useState<PayrollRow | null>(null);
  const [form, setForm] = useState<SavePayrollInput | null>(null);
  const [downloadingId, setDownloadingId] = useState<string | null>(null);
  const [configOpen, setConfigOpen] = useState(false);
  useEffect(() => { setForm(editing ? toInput(editing) : null); }, [editing]);

  // The editing employee's call-centre deduction rules, so the modal can auto-calculate amounts live.
  const { data: editConfig } = useGetPayrollConfigQuery(editing?.callCenterId ?? "", { skip: !editing?.callCenterId });

  // Once the real per-centre rules arrive (or on open), re-derive the four amounts so the preview
  // matches what the server will store. The amounts are computed, never entered by hand.
  useEffect(() => {
    setForm((f) => (f ? { ...f, ...autoDeductions(f, editConfig) } : f));
  }, [editConfig]);

  const num = (k: keyof SavePayrollInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => (f ? { ...f, [k]: e.target.value === "" ? 0 : Number(e.target.value) } : f));

  // A field whose change re-derives the auto deduction amounts (day counts + the basic/working-days basis).
  const numAuto = (k: keyof SavePayrollInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => {
        if (!f) return f;
        const next = { ...f, [k]: e.target.value === "" ? 0 : Number(e.target.value) };
        return { ...next, ...autoDeductions(next, editConfig) };
      });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!editing || !form) return;
    try {
      await save({ employeeId: editing.employeeId, year, month, input: form }).unwrap();
      toast.success(HR_MSG.payrollSaved, HR_MSG.nameMonth(editing.fullName, monthValue));
      setEditing(null);
    } catch (err: unknown) {
      toast.error(HR_MSG.saveFailed, getErrorDetail(err) ?? HR_MSG.retry);
    }
  }

  // Live totals for the edit modal: net = gross earnings − all deductions, recomputed as HR edits.
  const gross = form ? form.basicSalary + form.punctuality + form.dailyBonus + form.monthlyCommissions + form.transportAllowance + form.specialAllowance : 0;
  const totalDeductions = form ? form.advanceSalary + form.docks + form.lateComingAmount + form.halfDaysAmount + form.absentDaysAmount + form.ncnsAmount : 0;
  const netPay = gross - totalDeductions;
  const dailyWage = form && form.workingDays > 0 ? form.basicSalary / form.workingDays : 0;

  async function downloadSlip(row: PayrollRow) {
    setDownloadingId(row.employeeId);
    try {
      const res = await fetch(`${API_URL}/api/hr/payroll/slip?employeeId=${row.employeeId}&year=${year}&month=${month}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) { toast.error(HR_MSG.generateSlipFailed, HR_MSG.generateSlipDesc); return; }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = `salary-slip-${row.agentCode}-${monthValue}.pdf`;
      document.body.appendChild(a); a.click(); a.remove();
      URL.revokeObjectURL(url);
      toast.success(HR_MSG.slipDownloaded, HR_MSG.nameMonth(row.fullName, monthValue));
    } catch {
      toast.error(HR_MSG.downloadSlipFailed, HR_MSG.downloadSlipDesc);
    } finally {
      setDownloadingId(null);
    }
  }

  const list = rows ?? [];
  const totalNet = list.reduce((s, r) => s + r.netPay, 0);

  // Client-side search over the month's already-loaded rows — the query above is untouched.
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const all = rows ?? [];
    if (!q) return all;
    return all.filter((r) =>
      [r.fullName, r.agentCode, r.callCenterName].some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [rows, search]);

  // Selection is scoped to the visible (searched) rows so a bulk action never reaches a hidden one.
  const sel = useRowSelection(filtered.map((r) => r.employeeId));

  function exportSelected() {
    const chosen = filtered.filter((r) => sel.isSelected(r.employeeId));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (r) => r.fullName },
      { header: "Agent ID", value: (r) => r.agentCode },
      { header: "Basic", value: (r) => Math.round(r.basicSalary) },
      { header: "Deductions", value: (r) => Math.round(r.deductions) },
      { header: "Net", value: (r) => Math.round(r.netPay) },
    ], `payroll-${monthValue}.csv`);
    toast.success(HR_MSG.exportReady, HR_MSG.rowsDownloaded(chosen.length));
  }

  // ── Bulk "set pay" — write common fields (and/or mark present) across every selected row.
  // A blank field keeps each employee's current value; finalized rows are locked and skipped.
  const blankBulk = { basicSalary: "", transportAllowance: "", specialAllowance: "", workingDays: "", markPresent: false, finalize: false };
  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkForm, setBulkForm] = useState(blankBulk);
  const [bulkBusy, setBulkBusy] = useState(false);
  const bulkField = (k: "basicSalary" | "transportAllowance" | "specialAllowance" | "workingDays") =>
    (e: React.ChangeEvent<HTMLInputElement>) => setBulkForm((f) => ({ ...f, [k]: e.target.value }));

  async function applyBulk() {
    const chosen = filtered.filter((r) => sel.isSelected(r.employeeId));
    const editable = chosen.filter((r) => !r.finalized);
    const skipped = chosen.length - editable.length;
    const numOrUndef = (s: string) => (s.trim() === "" ? undefined : Number(s));
    const basic = numOrUndef(bulkForm.basicSalary);
    const transport = numOrUndef(bulkForm.transportAllowance);
    const special = numOrUndef(bulkForm.specialAllowance);
    const wdays = numOrUndef(bulkForm.workingDays);

    if (basic === undefined && transport === undefined && special === undefined && wdays === undefined
        && !bulkForm.markPresent && !bulkForm.finalize) {
      toast.error(HR_MSG.nothingToApply, HR_MSG.nothingToApplyDesc);
      return;
    }
    if (editable.length === 0) { toast.error(HR_MSG.nothingToUpdate, HR_MSG.nothingToUpdateDesc); return; }

    setBulkBusy(true);
    const results = await Promise.allSettled(editable.map((r) => {
      const input = toInput(r);
      if (basic !== undefined) input.basicSalary = basic;
      if (transport !== undefined) input.transportAllowance = transport;
      if (special !== undefined) input.specialAllowance = special;
      if (wdays !== undefined) input.workingDays = wdays;
      if (bulkForm.markPresent) {
        // Full attendance: present = working days; clear every attendance-driven deduction
        // (count 0 ⇒ amount 0 regardless of each centre's rates, so this stays config-independent).
        input.presentDays = input.workingDays;
        input.lateComing = 0; input.halfDays = 0; input.absentDays = 0; input.ncns = 0;
        input.lateComingAmount = 0; input.halfDaysAmount = 0; input.absentDaysAmount = 0; input.ncnsAmount = 0;
      }
      if (bulkForm.finalize) input.finalized = true;
      return save({ employeeId: r.employeeId, year, month, input }).unwrap();
    }));
    setBulkBusy(false);

    const failedIds = editable.filter((_, i) => results[i].status === "rejected").map((r) => r.employeeId);
    const ok = editable.length - failedIds.length;
    if (ok > 0) {
      const note = [bulkForm.markPresent ? HR_MSG.markedPresent : null, HR_MSG.paySaved].filter(Boolean).join(" · ");
      toast.success(HR_MSG.updatedEmployees(ok),
        skipped > 0 ? `${note}. ${HR_MSG.finalizedRowsSkipped(skipped)}` : `${note}.`);
    }
    if (failedIds.length > 0) {
      // Keep ONLY the failed rows selected so the user can see and retry exactly "those".
      toast.error(HR_MSG.couldntBeSaved(failedIds.length), HR_MSG.stillSelectedRetry);
      sel.keepOnly(failedIds);
    } else {
      sel.clear(); setBulkOpen(false); setBulkForm(blankBulk);
    }
  }

  return (
    <>
      <PageHeader eyebrow="Human Resources" title="Payroll"
        description="Monthly pay per employee — attendance + commission auto-filled; export a PDF slip for each." />

      <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 mb-5">
        <Stat label="Employees" value={list.length} icon={<Icon name="users" size={16} />} tone="brand" />
        <Stat label="Total net pay" value={money(totalNet)} hint="Earnings − deductions, summed across employees" icon={<Icon name="dollar" size={16} />} tone="success" />
        <Stat label="Finalized" value={list.filter((r) => r.finalized).length} hint="Rows locked for the month and no longer auto-recalculated" icon={<Icon name="check" size={16} />} tone="accent" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <Input type="month" aria-label="Month" value={monthValue}
            onChange={(e) => { const [y, m] = e.target.value.split("-").map(Number); if (y && m) { setYear(y); setMonth(m); } }}
            className="w-44" />
          <Select aria-label="Call centre" value={callCenterId} onChange={(e) => setCallCenterId(e.target.value)} className="w-48">
            <option value="">All call centres</option>
            {(callCenters ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </Select>
          <SearchInput value={search} onChange={setSearch} placeholder={HR_MSG.payrollSearchPlaceholder} className="w-64" />
          <Button variant="outline" leftIcon={<Icon name="cog" size={15} />} onClick={() => setConfigOpen(true)}>Deduction rules</Button>
          <span className="text-sm text-ink-500 ml-auto">{list.length} {list.length === 1 ? "employee" : "employees"}</span>
        </CardBody>
      </Card>

      <DeductionRulesModal open={configOpen} onClose={() => setConfigOpen(false)}
        callCenters={callCenters ?? []} initialCallCenterId={callCenterId} />

      <Modal open={bulkOpen} onClose={() => setBulkOpen(false)} title="Set pay for selected"
        description={`${sel.selectedCount} selected · ${monthValue}. Leave a field blank to keep each employee's current value.`}
        size="lg"
        footer={<>
          <Button variant="ghost" onClick={() => setBulkOpen(false)}>Cancel</Button>
          <Button loading={bulkBusy} onClick={applyBulk} leftIcon={<Icon name="check" size={15} />}>Apply to {sel.selectedCount}</Button>
        </>}>
        <div className="space-y-4">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Input label="Basic salary (PKR)" type="number" min={0} step="1" value={bulkForm.basicSalary} placeholder="Keep current" onChange={bulkField("basicSalary")} />
            <Input label="Working days" type="number" min={0} step="1" value={bulkForm.workingDays} placeholder="Keep current" onChange={bulkField("workingDays")} />
            <Input label="Transport allowance (PKR)" type="number" min={0} step="1" value={bulkForm.transportAllowance} placeholder="Keep current" onChange={bulkField("transportAllowance")} />
            <Input label="Special allowance (PKR)" type="number" min={0} step="1" value={bulkForm.specialAllowance} placeholder="Keep current" onChange={bulkField("specialAllowance")} />
          </div>
          <div className="rounded-xl border border-ink-200 divide-y hairline">
            <label className="flex items-start gap-2.5 p-3 cursor-pointer">
              <input type="checkbox" checked={bulkForm.markPresent} onChange={(e) => setBulkForm((f) => ({ ...f, markPresent: e.target.checked }))}
                className="mt-0.5 rounded border-ink-300 text-brand-600 focus:ring-brand-500" />
              <span>
                <span className="text-sm font-medium text-ink-800">Mark present</span>
                <span className="block text-xs text-ink-500">Sets present days = working days and clears all absent / half-day / NCNS / late-coming deductions.</span>
              </span>
            </label>
            <label className="flex items-start gap-2.5 p-3 cursor-pointer">
              <input type="checkbox" checked={bulkForm.finalize} onChange={(e) => setBulkForm((f) => ({ ...f, finalize: e.target.checked }))}
                className="mt-0.5 rounded border-ink-300 text-brand-600 focus:ring-brand-500" />
              <span>
                <span className="text-sm font-medium text-ink-800">Finalize these rows</span>
                <span className="block text-xs text-ink-500">Locks the month so it's no longer auto-recalculated.</span>
              </span>
            </label>
          </div>
          <p className="text-xs text-ink-500">Rows already finalized are locked and will be skipped.</p>
        </div>
      </Modal>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : list.length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="users" size={20} />} title={HR_MSG.noEmployeesTitle} description={HR_MSG.payrollEmptyDesc} /></CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="search" size={20} />} title={HR_MSG.noMatchesTitle} description={HR_MSG.noEmployeeSearchMatchesDesc} /></CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
              <TH className="w-10"><Checkbox aria-label="Select all employees" {...sel.allCheckboxProps} /></TH>
              <TH>
                <span className="inline-flex items-center gap-1">Employee
                  <InfoHint title="Agent ID" side="bottom">The mono code beneath each name is the employee's unique Agent ID.</InfoHint>
                </span>
              </TH>
              <TH numeric>Basic</TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Commission
                  <InfoHint title="Monthly commission" side="bottom">Auto-derived from this employee's approved sales for the month.</InfoHint>
                </span>
              </TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Allowances
                  <InfoHint title="Allowances" side="bottom">Punctuality, daily bonus, transport and special allowance combined.</InfoHint>
                </span>
              </TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Deductions
                  <InfoHint title="Deductions" side="bottom">Advance salary plus docks withheld this month.</InfoHint>
                </span>
              </TH>
              <TH numeric>
                <span className="inline-flex items-center gap-1">Net pay
                  <InfoHint title="Net pay" side="bottom">Total earnings minus total deductions — the take-home amount.</InfoHint>
                </span>
              </TH>
              <TH>
                <span className="inline-flex items-center gap-1">Status
                  <InfoHint title="Payroll status" side="left">Auto (system estimate), Draft (saved by HR, still editable), or Finalized (locked for the month).</InfoHint>
                </span>
              </TH>
              <TH className="text-right">Actions</TH>
            </TR></THead>
            <TBody>
              {filtered.map((r) => (
                <TR key={r.employeeId} className={sel.isSelected(r.employeeId) ? "bg-brand-50/40" : undefined}>
                  <TD>
                    <Checkbox aria-label={`Select ${r.fullName}`} {...sel.checkboxProps(r.employeeId)} />
                  </TD>
                  <TD>
                    <div className="font-medium text-ink-900">{r.fullName}</div>
                    <div className="font-mono text-xs text-ink-500">{r.agentCode}</div>
                  </TD>
                  <TD numeric className="tabular-nums text-ink-600">{money(r.basicSalary)}</TD>
                  <TD numeric className="tabular-nums text-ink-600">{money(r.monthlyCommissions)}</TD>
                  <TD numeric className="tabular-nums text-ink-600">{money(r.punctuality + r.dailyBonus + r.transportAllowance + r.specialAllowance)}</TD>
                  <TD numeric className="tabular-nums text-rose-600">{money(r.deductions)}</TD>
                  <TD numeric className="tabular-nums font-semibold text-ink-900">{money(r.netPay)}</TD>
                  <TD>{r.finalized ? <Badge tone="success" variant="soft">Finalized</Badge> : r.saved ? <Badge tone="warning" variant="soft">Draft</Badge> : <Badge tone="neutral" variant="soft">Auto</Badge>}</TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => setEditing(r)}>Edit</Button>
                      <Button variant="ghost" size="sm" leftIcon={<Icon name="download" size={14} />} loading={downloadingId === r.employeeId} onClick={() => downloadSlip(r)}>Slip</Button>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
          <BulkActionBar
            count={sel.selectedCount} itemNoun="employee" onClear={sel.clear}
            actions={[
              { key: "pay", label: "Set pay", icon: "dollar", onClick: () => setBulkOpen(true) },
              { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
            ]}
          />
        </div>
      )}

      <Modal open={!!editing} onClose={() => setEditing(null)}
        title={editing ? `Payroll — ${editing.fullName}` : "Payroll"}
        description={`${monthValue} · basic salary & advance carry from last month; attendance & commission auto-filled.`}
        size="xl"
        footer={<>
          <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
          <Button form="pay-form" type="submit" loading={saving}>Save</Button>
        </>}>
        {form && (
          <form id="pay-form" onSubmit={submit} className="space-y-5">
            <Section title={<span className="inline-flex items-center gap-1">Earnings<InfoHint title="Earnings" side="right">Monthly commission is auto-derived from approved sales; the other components are entered by HR.</InfoHint></span>}>
              <Num label="Basic salary" v={form.basicSalary} on={numAuto("basicSalary")} />
              <Num label="Punctuality" v={form.punctuality} on={num("punctuality")} />
              <Num label="Daily bonus" v={form.dailyBonus} on={num("dailyBonus")} />
              <Num label="Monthly commissions" v={form.monthlyCommissions} on={num("monthlyCommissions")} />
              <Num label="Transport allowance" v={form.transportAllowance} on={num("transportAllowance")} />
              <Num label="Special allowance" v={form.specialAllowance} on={num("specialAllowance")} />
            </Section>
            <div className="text-xs text-ink-500 -mb-1">
              Daily wage <span className="font-semibold text-ink-700 tabular-nums">{money(dailyWage)}</span>
              {" "}— basic {money(form.basicSalary)} ÷ {form.workingDays} working days. Day-based deductions (half/absent/NCNS) are computed from this.
            </div>
            <Ledger title="Deductions" hint={<InfoHint title="Number & amount" side="right">The attendance amounts are computed automatically as Number × a day's pay (basic ÷ working days) × the call centre's rule — change the day count or the basic/working days and the amount follows. Advance salary carries from last month; docks are ad-hoc penalties. All amounts add up to total deductions.</InfoHint>}>
              <LedgerLine label="Late coming" count={form.lateComing} onCount={numAuto("lateComing")} amount={form.lateComingAmount} />
              <LedgerLine label="Half days" count={form.halfDays} onCount={numAuto("halfDays")} amount={form.halfDaysAmount} />
              <LedgerLine label="Absent days" count={form.absentDays} onCount={numAuto("absentDays")} amount={form.absentDaysAmount} />
              <LedgerLine label="NCNS" count={form.ncns} onCount={numAuto("ncns")} amount={form.ncnsAmount} />
              <LedgerLine label="Advance salary" amount={form.advanceSalary} onAmount={num("advanceSalary")} />
              <LedgerLine label="Docks" amount={form.docks} onAmount={num("docks")} />
            </Ledger>
            {/* Live net-salary summary — recomputed automatically after every deduction. */}
            <div className="rounded-xl border border-ink-200 bg-ink-50/60 p-3 grid grid-cols-3 gap-2 text-center">
              <div>
                <div className="text-[11px] uppercase tracking-wide text-ink-400">Gross</div>
                <div className="text-sm font-semibold text-ink-800 tabular-nums">{money(gross)}</div>
              </div>
              <div>
                <div className="text-[11px] uppercase tracking-wide text-ink-400">Deductions</div>
                <div className="text-sm font-semibold text-rose-600 tabular-nums">−{money(totalDeductions)}</div>
              </div>
              <div>
                <div className="text-[11px] uppercase tracking-wide text-ink-400">Net pay</div>
                <div className="text-base font-bold text-emerald-600 tabular-nums">{money(netPay)}</div>
              </div>
            </div>
            <Section title={<span className="inline-flex items-center gap-1">Attendance summary<InfoHint title="Attendance summary" side="right">Informational day counts for the month. The deduction lines above carry the money; these are for reference and feed the slip's attendance line.</InfoHint></span>}>
              <Num label="Working days" v={form.workingDays} on={numAuto("workingDays")} />
              <Num label="Present days" v={form.presentDays} on={num("presentDays")} />
              <Num label="Leaves approved" v={form.leavesApproved} on={num("leavesApproved")} />
            </Section>
            <Textarea label="Notes" value={form.notes ?? ""} onChange={(e) => setForm((f) => (f ? { ...f, notes: e.target.value } : f))} placeholder="Any adjustments or context for this month's pay…" />
            <label className="inline-flex items-center gap-2 text-sm text-ink-700 cursor-pointer">
              <input type="checkbox" checked={form.finalized} onChange={(e) => setForm((f) => (f ? { ...f, finalized: e.target.checked } : f))}
                className="rounded border-ink-300 text-brand-600 focus:ring-brand-500" />
              <span className="inline-flex items-center gap-1">Finalize this month
                <InfoHint title="Finalize" side="top">Locks this month's payroll so it's no longer auto-recalculated.</InfoHint>
              </span>
            </label>
          </form>
        )}
      </Modal>
    </>
  );
}

/**
 * Per-call-centre deduction rules. HR/Admin edit any centre; a CallCenterAdmin only their own
 * (enforced server-side). A day's pay = basic ÷ working days; half/absent/NCNS are multiples of it,
 * late-coming is a flat fine.
 */
function DeductionRulesModal({ open, onClose, callCenters, initialCallCenterId }: {
  open: boolean;
  onClose: () => void;
  callCenters: { id: string; name: string }[];
  initialCallCenterId: string;
}) {
  const toast = useToast();
  const [ccId, setCcId] = useState(initialCallCenterId || callCenters[0]?.id || "");
  useEffect(() => {
    if (open) setCcId(initialCallCenterId || callCenters[0]?.id || "");
  }, [open, initialCallCenterId, callCenters]);

  const { data: config, isFetching } = useGetPayrollConfigQuery(ccId, { skip: !ccId });
  const [saveConfig, { isLoading: saving }] = useSavePayrollConfigMutation();

  const [form, setForm] = useState<SavePayrollConfigInput | null>(null);
  useEffect(() => {
    if (config) setForm({
      lateComingFine: config.lateComingFine, halfDayFactor: config.halfDayFactor,
      absentDayFactor: config.absentDayFactor, ncnsFactor: config.ncnsFactor,
    });
  }, [config]);

  const setNum = (k: keyof SavePayrollConfigInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => (f ? { ...f, [k]: e.target.value === "" ? 0 : Number(e.target.value) } : f));

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!ccId || !form) return;
    try {
      await saveConfig({ callCenterId: ccId, input: form }).unwrap();
      toast.success(HR_MSG.deductionRulesSaved, callCenters.find((c) => c.id === ccId)?.name);
      onClose();
    } catch (err: unknown) {
      toast.error(HR_MSG.saveRulesFailed, getErrorDetail(err) ?? HR_MSG.saveRulesPermissionDesc);
    }
  }

  return (
    <Modal open={open} onClose={onClose} title="Deduction rules"
      description="How attendance-driven salary deductions are calculated for a call centre. A day's pay = basic salary ÷ working days."
      size="lg"
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button form="ded-form" type="submit" loading={saving} disabled={!ccId || !form || isFetching}>Save rules</Button>
      </>}>
      <form id="ded-form" onSubmit={submit} className="space-y-4">
        <Select label="Call centre" value={ccId} onChange={(e) => setCcId(e.target.value)}>
          <option value="" disabled>Select a call centre…</option>
          {callCenters.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </Select>
        {!ccId ? (
          <p className="text-sm text-ink-500">Pick a call centre to edit its rules.</p>
        ) : isFetching || !form ? (
          <Skeleton className="h-40" />
        ) : (
          <>
            {!config?.saved && (
              <div className="text-xs rounded-lg bg-amber-50 border border-amber-200 text-amber-800 px-3 py-2">
                Showing the default rules — save to set this call centre's own.
              </div>
            )}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Input label="Late-coming fine (PKR each)" type="number" min={0} step="1"
                value={form.lateComingFine === 0 ? "" : form.lateComingFine} placeholder="0" onChange={setNum("lateComingFine")} />
              <Input label="Half-day factor (× a day's pay)" type="number" min={0} step="0.05"
                value={form.halfDayFactor === 0 ? "" : form.halfDayFactor} placeholder="0.5" onChange={setNum("halfDayFactor")} />
              <Input label="Absent-day factor (× a day's pay)" type="number" min={0} step="0.05"
                value={form.absentDayFactor === 0 ? "" : form.absentDayFactor} placeholder="1" onChange={setNum("absentDayFactor")} />
              <Input label="NCNS factor (× a day's pay)" type="number" min={0} step="0.05"
                value={form.ncnsFactor === 0 ? "" : form.ncnsFactor} placeholder="2" onChange={setNum("ncnsFactor")} />
            </div>
            <p className="text-xs text-ink-500 leading-relaxed">
              Example — on a PKR 60,000 basic over 24 working days (a day's pay is PKR 2,500): one absent day
              deducts {money(2500 * form.absentDayFactor)}, a half day {money(2500 * form.halfDayFactor)},
              an NCNS {money(2500 * form.ncnsFactor)}, and each late-coming {money(form.lateComingFine)}.
            </p>
          </>
        )}
      </form>
    </Modal>
  );
}

function Section({ title, children }: { title: React.ReactNode; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500 mb-2 pb-1 border-b hairline">{title}</div>
      <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">{children}</div>
    </div>
  );
}

function Num({ label, v, on }: { label: string; v: number; on: (e: React.ChangeEvent<HTMLInputElement>) => void }) {
  // Show empty (placeholder "0") when zero so a value can't be typed after a leading "0" (e.g. "060000").
  return <Input label={label} type="number" min={0} step="0.01" value={v === 0 ? "" : v} placeholder="0" onChange={on} />;
}

/** A ledger block with explicit "Number" and "Amount" column headings (spec: number + amount against each line). */
function Ledger({ title, hint, children }: { title: string; hint?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500 mb-2 pb-1 border-b hairline flex items-center gap-1">{title}{hint}</div>
      <div className="grid grid-cols-[1fr_4.5rem_7rem] gap-x-3 gap-y-1.5 items-center">
        <span />
        <span className="text-[10px] font-semibold uppercase tracking-wide text-ink-400 text-right">Number</span>
        <span className="text-[10px] font-semibold uppercase tracking-wide text-ink-400 text-right">Amount</span>
        {children}
      </div>
    </div>
  );
}

const cell = "h-9 w-full rounded-lg border border-ink-200 px-2 text-sm text-right tabular-nums outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-500/20";

/**
 * One ledger row: label + optional day-count input (the "number") + the amount.
 * When <c>onAmount</c> is omitted the amount is COMPUTED (read-only) — it auto-derives from the
 * day count and the daily wage, so it can't be edited into an inconsistent value.
 */
function LedgerLine({ label, count, onCount, amount, onAmount }: {
  label: string;
  count?: number;
  onCount?: (e: React.ChangeEvent<HTMLInputElement>) => void;
  amount: number;
  onAmount?: (e: React.ChangeEvent<HTMLInputElement>) => void;
}) {
  return (
    <>
      <span className="text-sm text-ink-700">{label}</span>
      {onCount
        ? <input aria-label={`${label} number`} type="number" min={0} value={count ? count : ""} placeholder="0" onChange={onCount} className={cell} />
        : <span className="text-ink-300 text-right text-sm pr-2">—</span>}
      {onAmount
        ? <input aria-label={`${label} amount`} type="number" min={0} step="0.01" value={amount === 0 ? "" : amount} placeholder="0" onChange={onAmount} className={cell} />
        : <span aria-label={`${label} amount`} className="h-9 flex items-center justify-end pr-2 text-sm text-ink-600 tabular-nums">{money(amount)}</span>}
    </>
  );
}
