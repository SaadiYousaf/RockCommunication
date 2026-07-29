import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { API_URL } from "../../shared/config";
import {
  useListPayrollQuery, useSavePayrollMutation, useListCallCentersQuery,
} from "../../shared/api/baseApi";
import type { PayrollRow, SavePayrollInput } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";

const money = (n: number | null | undefined) =>
  n == null ? "—" : n.toLocaleString(undefined, { style: "currency", currency: "USD", maximumFractionDigits: 0 });

const toInput = (r: PayrollRow): SavePayrollInput => ({
  basicSalary: r.basicSalary, punctuality: r.punctuality, dailyBonus: r.dailyBonus,
  monthlyCommissions: r.monthlyCommissions, transportAllowance: r.transportAllowance,
  specialAllowance: r.specialAllowance, advanceSalary: r.advanceSalary, docks: r.docks,
  workingDays: r.workingDays, presentDays: r.presentDays, leavesApproved: r.leavesApproved,
  lateComing: r.lateComing, halfDays: r.halfDays, absentDays: r.absentDays, ncns: r.ncns,
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
  useEffect(() => { setForm(editing ? toInput(editing) : null); }, [editing]);

  const num = (k: keyof SavePayrollInput) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => (f ? { ...f, [k]: e.target.value === "" ? 0 : Number(e.target.value) } : f));

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

  async function downloadSlip(row: PayrollRow) {
    try {
      const res = await fetch(`${API_URL}/api/hr/payroll/slip?employeeId=${row.employeeId}&year=${year}&month=${month}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) { toast.error("Couldn't generate slip"); return; }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = `salary-slip-${row.agentCode}-${monthValue}.pdf`;
      document.body.appendChild(a); a.click(); a.remove();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Couldn't download the slip");
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
        <Stat label="Total net pay" value={money(totalNet)} icon={<Icon name="dollar" size={16} />} tone="success" />
        <Stat label="Finalized" value={list.filter((r) => r.finalized).length} icon={<Icon name="check" size={16} />} tone="accent" />
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
          <span className="text-sm text-ink-500 ml-auto">{list.length} employees</span>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : list.length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="users" size={20} />} title="No employees" description="Add employees in HR → Employees first (or import from users)." /></CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
              <TH>Employee</TH><TH numeric>Basic</TH><TH numeric>Commission</TH><TH numeric>Allowances</TH>
              <TH numeric>Deductions</TH><TH numeric>Net pay</TH><TH>Status</TH><TH className="text-right">Actions</TH>
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
                      <Button variant="ghost" size="sm" leftIcon={<Icon name="download" size={14} />} onClick={() => downloadSlip(r)}>Slip</Button>
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
            <Section title="Earnings">
              <Num label="Basic salary" v={form.basicSalary} on={num("basicSalary")} />
              <Num label="Punctuality" v={form.punctuality} on={num("punctuality")} />
              <Num label="Daily bonus" v={form.dailyBonus} on={num("dailyBonus")} />
              <Num label="Monthly commissions" v={form.monthlyCommissions} on={num("monthlyCommissions")} />
              <Num label="Transport allowance" v={form.transportAllowance} on={num("transportAllowance")} />
              <Num label="Special allowance" v={form.specialAllowance} on={num("specialAllowance")} />
            </Section>
            <Section title="Deductions">
              <Num label="Advance salary" v={form.advanceSalary} on={num("advanceSalary")} />
              <Num label="Docks" v={form.docks} on={num("docks")} />
            </Section>
            <Section title="Attendance (days)">
              <Num label="Working days" v={form.workingDays} on={num("workingDays")} />
              <Num label="Present days" v={form.presentDays} on={num("presentDays")} />
              <Num label="Late coming" v={form.lateComing} on={num("lateComing")} />
              <Num label="Half days" v={form.halfDays} on={num("halfDays")} />
              <Num label="Leaves approved" v={form.leavesApproved} on={num("leavesApproved")} />
              <Num label="Absent days" v={form.absentDays} on={num("absentDays")} />
              <Num label="NCNS" v={form.ncns} on={num("ncns")} />
            </Section>
            <Textarea label="Notes" value={form.notes ?? ""} onChange={(e) => setForm((f) => (f ? { ...f, notes: e.target.value } : f))} />
            <label className="inline-flex items-center gap-2 text-sm text-ink-700 cursor-pointer">
              <input type="checkbox" checked={form.finalized} onChange={(e) => setForm((f) => (f ? { ...f, finalized: e.target.checked } : f))}
                className="rounded border-ink-300 text-brand-600 focus:ring-brand-500" />
              Finalize this month
            </label>
          </form>
        )}
      </Modal>
    </>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500 mb-2 pb-1 border-b hairline">{title}</div>
      <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">{children}</div>
    </div>
  );
}

function Num({ label, v, on }: { label: string; v: number; on: (e: React.ChangeEvent<HTMLInputElement>) => void }) {
  return <Input label={label} type="number" min={0} step="0.01" value={v} onChange={on} />;
}
