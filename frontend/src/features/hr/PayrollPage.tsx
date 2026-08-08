import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { API_URL } from "../../shared/config";
import {
  useListPayrollQuery, useSavePayrollMutation, useListCallCentersQuery,
  useGetPayrollConfigQuery, useSavePayrollConfigMutation,
} from "../../shared/api/baseApi";
import type { PayrollRow, SavePayrollInput, PayrollConfig, SavePayrollConfigInput } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";

// Salaries are paid in PKR.
const money = (n: number | null | undefined) =>
  n == null ? "—" : "PKR " + Math.round(n).toLocaleString();

/** Auto-derive the four attendance-driven deduction amounts from a call centre's rules. Mirrors the backend. */
function autoDeductions(f: SavePayrollInput, cfg: PayrollConfig | undefined) {
  if (!cfg) return null;
  const perDay = f.workingDays > 0 ? f.basicSalary / f.workingDays : 0;
  const r = (v: number) => Math.round(v);
  return {
    lateComingAmount: r(f.lateComing * cfg.lateComingFine),
    halfDaysAmount: r(f.halfDays * perDay * cfg.halfDayFactor),
    absentDaysAmount: r(f.absentDays * perDay * cfg.absentDayFactor),
    ncnsAmount: r(f.ncns * perDay * cfg.ncnsFactor),
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
  const [callCenterId, setCallCenterId] = useState("");
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
  // Deduction-amount fields HR has manually overridden — auto-calc must never overwrite these.
  const [overridden, setOverridden] = useState<Set<keyof SavePayrollInput>>(new Set());
  useEffect(() => { setForm(editing ? toInput(editing) : null); setOverridden(new Set()); }, [editing]);

  // The editing employee's call-centre deduction rules, so the modal can auto-calculate amounts live.
  const { data: editConfig } = useGetPayrollConfigQuery(editing?.callCenterId ?? "", { skip: !editing?.callCenterId });

  const num = (k: keyof SavePayrollInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => (f ? { ...f, [k]: e.target.value === "" ? 0 : Number(e.target.value) } : f));

  // Editing a deduction Amount directly marks it overridden so auto-calc leaves it alone thereafter.
  const numOverride = (k: keyof SavePayrollInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) => { setOverridden((s) => new Set(s).add(k)); num(k)(e); };

  // A field whose change re-derives the auto deduction amounts (day counts + the basic/working-days basis),
  // but NEVER touches an amount HR has manually overridden.
  const numAuto = (k: keyof SavePayrollInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => {
        if (!f) return f;
        const next = { ...f, [k]: e.target.value === "" ? 0 : Number(e.target.value) };
        const auto = autoDeductions(next, editConfig);
        if (!auto) return next;
        const merged = Object.fromEntries(
          Object.entries(auto).filter(([ak]) => !overridden.has(ak as keyof SavePayrollInput)),
        );
        return { ...next, ...merged };
      });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!editing || !form) return;
    try {
      await save({ employeeId: editing.employeeId, year, month, input: form }).unwrap();
      toast.success("Payroll saved", `${editing.fullName} — ${monthValue}`);
      setEditing(null);
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Try again.");
    }
  }

  // Live totals for the edit modal: net = gross earnings − all deductions, recomputed as HR edits.
  const gross = form ? form.basicSalary + form.punctuality + form.dailyBonus + form.monthlyCommissions + form.transportAllowance + form.specialAllowance : 0;
  const totalDeductions = form ? form.advanceSalary + form.docks + form.lateComingAmount + form.halfDaysAmount + form.absentDaysAmount + form.ncnsAmount : 0;
  const netPay = gross - totalDeductions;

  async function downloadSlip(row: PayrollRow) {
    setDownloadingId(row.employeeId);
    try {
      const res = await fetch(`${API_URL}/api/hr/payroll/slip?employeeId=${row.employeeId}&year=${year}&month=${month}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) { toast.error("Couldn't generate slip", "Try again in a moment."); return; }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = `salary-slip-${row.agentCode}-${monthValue}.pdf`;
      document.body.appendChild(a); a.click(); a.remove();
      URL.revokeObjectURL(url);
      toast.success("Slip downloaded", `${row.fullName} — ${monthValue}`);
    } catch {
      toast.error("Couldn't download the slip", "Check your connection and try again.");
    } finally {
      setDownloadingId(null);
    }
  }

  const list = rows ?? [];
  const totalNet = list.reduce((s, r) => s + r.netPay, 0);

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
          <Button variant="outline" leftIcon={<Icon name="cog" size={15} />} onClick={() => setConfigOpen(true)}>Deduction rules</Button>
          <span className="text-sm text-ink-500 ml-auto">{list.length} {list.length === 1 ? "employee" : "employees"}</span>
        </CardBody>
      </Card>

      <DeductionRulesModal open={configOpen} onClose={() => setConfigOpen(false)}
        callCenters={callCenters ?? []} initialCallCenterId={callCenterId} />

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : list.length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="users" size={20} />} title="No employees" description="Add employees in HR → Employees first (or import from users)." /></CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
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
              {list.map((r) => (
                <TR key={r.employeeId}>
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
            <Ledger title="Deductions" hint={<InfoHint title="Number & amount" side="right">Amounts auto-calculate from this call centre's deduction rules (Number × rate) — edit an Amount to override it. Advance salary carries from last month; docks are ad-hoc penalties. All amounts add up to total deductions.</InfoHint>}>
              <LedgerLine label="Late coming" count={form.lateComing} onCount={numAuto("lateComing")} amount={form.lateComingAmount} onAmount={numOverride("lateComingAmount")} />
              <LedgerLine label="Half days" count={form.halfDays} onCount={numAuto("halfDays")} amount={form.halfDaysAmount} onAmount={numOverride("halfDaysAmount")} />
              <LedgerLine label="Absent days" count={form.absentDays} onCount={numAuto("absentDays")} amount={form.absentDaysAmount} onAmount={numOverride("absentDaysAmount")} />
              <LedgerLine label="NCNS" count={form.ncns} onCount={numAuto("ncns")} amount={form.ncnsAmount} onAmount={numOverride("ncnsAmount")} />
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
      toast.success("Deduction rules saved", callCenters.find((c) => c.id === ccId)?.name);
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't save rules", getErrorDetail(err) ?? "You may not have permission for this call centre.");
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

/** One ledger row: label + optional day-count input (the "number") + the amount input. */
function LedgerLine({ label, count, onCount, amount, onAmount }: {
  label: string;
  count?: number;
  onCount?: (e: React.ChangeEvent<HTMLInputElement>) => void;
  amount: number;
  onAmount: (e: React.ChangeEvent<HTMLInputElement>) => void;
}) {
  return (
    <>
      <span className="text-sm text-ink-700">{label}</span>
      {onCount
        ? <input aria-label={`${label} number`} type="number" min={0} value={count ? count : ""} placeholder="0" onChange={onCount} className={cell} />
        : <span className="text-ink-300 text-right text-sm pr-2">—</span>}
      <input aria-label={`${label} amount`} type="number" min={0} step="0.01" value={amount === 0 ? "" : amount} placeholder="0" onChange={onAmount} className={cell} />
    </>
  );
}
