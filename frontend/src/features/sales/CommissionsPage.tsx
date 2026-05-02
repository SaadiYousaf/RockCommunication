import { useMemo, useState } from "react";
import { useCreatePayrollRunMutation, useMyCommissionsQuery, usePayrollRunsQuery } from "../../shared/api/baseApi";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

const API_URL = (import.meta as any).env?.VITE_API_URL ?? "http://localhost:5050";

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
  const isManager = auth.user?.roles.some((r) => ["Admin", "ProgramManager"].includes(r)) ?? false;
  const { data: runs } = usePayrollRunsQuery(undefined, { skip: !isManager });
  const [createRun, { isLoading: creating }] = useCreatePayrollRunMutation();
  const toast = useToast();

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

  function setRange(days: number) {
    setFrom(todayStr(-days));
    setTo(todayStr(0));
  }

  function exportPayrollCsv(runId?: string) {
    const params = new URLSearchParams();
    if (runId) params.set("runId", runId);
    else { params.set("from", from); params.set("to", to); }
    const token = localStorage.getItem("auth")
      ? JSON.parse(localStorage.getItem("auth")!)?.accessToken : null;
    if (!token) { toast.error("Not authenticated"); return; }
    fetch(`${API_URL}/api/sales/payroll-export?${params.toString()}`, {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then((r) => { if (!r.ok) throw new Error("Export failed"); return r.blob(); })
      .then((blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = `payroll-${runId ?? `${from}-to-${to}`}.csv`;
        a.click();
        URL.revokeObjectURL(url);
      })
      .catch((err) => toast.error("Export failed", err.message));
  }

  async function makeRun() {
    try {
      await createRun({ periodStart: new Date(from).toISOString(), periodEnd: new Date(to).toISOString() }).unwrap();
      toast.success("Payroll run created", `Period ${from} → ${to}`);
    } catch (err: any) {
      toast.error("Couldn't create payroll run", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Commissions"
        description="Review your earnings and let managers run payroll for the period."
      />

      <Card className="mb-6">
        <CardBody className="flex flex-wrap items-end gap-4">
          <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} containerClassName="w-44" />
          <Input label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} containerClassName="w-44" />
          <div className="flex gap-1">
            <Button size="sm" variant="ghost" onClick={() => setRange(7)}>7d</Button>
            <Button size="sm" variant="ghost" onClick={() => setRange(30)}>30d</Button>
            <Button size="sm" variant="ghost" onClick={() => setRange(90)}>90d</Button>
          </div>
          <div className="flex gap-3 ml-auto">
            <Tile label="Total"  value={`$${total.toFixed(2)}`}  tone="bg-brand-50 text-brand-600" />
            <Tile label="Paid"   value={`$${paid.toFixed(2)}`}   tone="bg-emerald-50 text-emerald-600" />
            <Tile label="Unpaid" value={`$${unpaid.toFixed(2)}`} tone="bg-amber-50 text-amber-600" />
          </div>
        </CardBody>
      </Card>

      {byRule.length > 0 && (
        <Card className="mb-6">
          <CardHeader title="Earnings by rule" subtitle="Where your commission came from in this period." />
          <CardBody className="pt-0">
            <div className="space-y-2">
              {byRule.map((r) => {
                const pct = total > 0 ? Math.round((r.amount / total) * 100) : 0;
                return (
                  <div key={r.rule} className="flex items-center gap-3">
                    <div className="font-mono text-xs text-ink-700 w-44 truncate">{r.rule}</div>
                    <div className="flex-1 h-2 rounded-full bg-ink-100 overflow-hidden">
                      <div className="h-full bg-brand-500" style={{ width: `${pct}%` }} />
                    </div>
                    <div className="text-xs text-ink-600 w-12 text-right">{r.count}×</div>
                    <div className="text-sm font-semibold text-ink-900 w-20 text-right">${r.amount.toFixed(2)}</div>
                    <div className="text-xs text-ink-500 w-10 text-right">{pct}%</div>
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
            title="No commissions in this range"
            description="Earn commissions by closing and funding sales, then check back here."
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>Earned</TH>
              <TH>Rule</TH>
              <TH>Amount</TH>
              <TH>Status</TH>
              <TH>Note</TH>
            </TR>
          </THead>
          <TBody>
            {commissions.map((c) => (
              <TR key={c.id}>
                <TD className="text-ink-600">{new Date(c.earnedAt).toLocaleString()}</TD>
                <TD className="font-mono text-xs text-ink-700">{c.ruleName}</TD>
                <TD className="font-semibold text-ink-900">${c.amount.toFixed(2)}</TD>
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
      )}

      {isManager && (
        <Card className="mt-6">
          <CardHeader
            title="Payroll runs"
            subtitle="Generate payroll for the selected period."
            action={
              <div className="flex gap-2">
                <Button variant="ghost" leftIcon={<Icon name="doc" size={16} />} onClick={() => exportPayrollCsv()}>
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
                  icon={<Icon name="briefcase" size={20} />}
                  title="No payroll runs yet"
                  description="Run payroll to summarize and freeze commissions for a period."
                />
              </div>
            ) : (
              <Table className="border-0 shadow-none rounded-none">
                <THead>
                  <TR>
                    <TH>Period</TH>
                    <TH>Total</TH>
                    <TH>Status</TH>
                    <TH>Processed</TH>
                    <TH className="text-right">Export</TH>
                  </TR>
                </THead>
                <TBody>
                  {runs.map((r) => (
                    <TR key={r.id}>
                      <TD className="text-ink-700">{r.periodStart.slice(0, 10)} → {r.periodEnd.slice(0, 10)}</TD>
                      <TD className="font-semibold text-ink-900">${r.totalAmount.toFixed(2)}</TD>
                      <TD><Badge tone={r.status === "Processed" ? "success" : "neutral"} variant="soft">{r.status}</Badge></TD>
                      <TD className="text-ink-500 text-xs">
                        {r.processedAt ? new Date(r.processedAt).toLocaleString() : "—"}
                      </TD>
                      <TD>
                        <div className="flex justify-end">
                          <Button variant="ghost" size="sm" leftIcon={<Icon name="doc" size={14} />}
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

function Tile({ label, value, tone }: { label: string; value: string; tone: string }) {
  return (
    <div className={`rounded-lg px-4 py-2.5 ${tone}`}>
      <div className="text-[10px] uppercase tracking-wide font-semibold opacity-80">{label}</div>
      <div className="text-lg font-semibold leading-tight">{value}</div>
    </div>
  );
}
