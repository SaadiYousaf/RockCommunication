import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import {
  useAttendanceDayQuery, useAttendanceSummaryQuery, useMarkAttendanceMutation, useListCallCentersQuery,
  useBulkMarkAttendanceMutation, useFillAttendanceFromClockInsMutation,
} from "../../shared/api/baseApi";
import { ATTENDANCE_STATUSES, hrLabel } from "../../shared/constants/hr";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, InfoHint, Input, PageHeader, Select, Skeleton,
  Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";

type Tab = "daily" | "summary";
const today = () => new Date().toISOString().slice(0, 10);

/**
 * HR → Attendance register. Mark each agent Present/Absent/Late/Half/Leave/NCNS per day
 * (the daily tab), and roll it up per month (the summary tab, which feeds payroll).
 */
export function HrAttendancePage() {
  const [tab, setTab] = useState<Tab>("daily");
  const [callCenterId, setCallCenterId] = useState("");
  const { data: callCenters } = useListCallCentersQuery();
  const cc = callCenterId || undefined;

  return (
    <>
      <PageHeader eyebrow="Human Resources" title="Attendance"
        description="Mark daily attendance per agent; the monthly roll-up feeds payroll." />

      <Card className="mb-4">
        <div className="px-2 pt-2 pb-1 flex items-center justify-between gap-3 flex-wrap">
          <Tabs<Tab> value={tab} onChange={setTab} items={[
            { value: "daily", label: "Daily register" },
            { value: "summary", label: "Monthly summary" },
          ]} />
          <div className="pr-2">
            <Select aria-label="Call centre" value={callCenterId} onChange={(e) => setCallCenterId(e.target.value)} className="w-48">
              <option value="">All call centres</option>
              {(callCenters ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          </div>
        </div>
      </Card>

      {tab === "daily" ? <DailyRegister callCenterId={cc} /> : <MonthlySummary callCenterId={cc} />}
    </>
  );
}

function DailyRegister({ callCenterId }: { callCenterId?: string }) {
  const [date, setDate] = useState(today());
  const { data: rows, isLoading } = useAttendanceDayQuery({ date, callCenterId });
  const [mark] = useMarkAttendanceMutation();
  const [bulk, { isLoading: bulking }] = useBulkMarkAttendanceMutation();
  const [fill, { isLoading: filling }] = useFillAttendanceFromClockInsMutation();
  const toast = useToast();

  async function setStatus(employeeId: string, status: string) {
    try { await mark({ employeeId, date, status }).unwrap(); }
    catch (err: unknown) { toast.error("Couldn't save", getErrorDetail(err) ?? "Try again."); }
  }
  async function markAllPresent() {
    try {
      const r = await bulk({ date, status: "Present", callCenterId, onlyUnmarked: true }).unwrap();
      toast.success(r.count > 0 ? `Marked ${r.count} present` : "Everyone's already marked", "Only unmarked employees were changed.");
    } catch (err: unknown) { toast.error("Couldn't mark all", getErrorDetail(err) ?? "Try again."); }
  }
  async function fillFromClockIns() {
    try {
      const r = await fill({ date, callCenterId }).unwrap();
      toast.success(r.count > 0 ? `Marked ${r.count} present from clock-ins` : "No new clock-ins", "Anyone who clocked in that day was marked Present.");
    } catch (err: unknown) { toast.error("Couldn't fill", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <>
      <div className="mb-4 flex items-center gap-3 flex-wrap">
        <Input type="date" aria-label="Date" value={date} onChange={(e) => setDate(e.target.value)} className="w-44" />
        <span className="text-sm text-ink-500">{rows?.length ?? 0} employees</span>
        <div className="flex items-center gap-2 ml-auto">
          <Button variant="outline" size="sm" leftIcon={<Icon name="phone" size={13} />} loading={filling} onClick={fillFromClockIns}>Fill from clock-ins</Button>
          <Button variant="outline" size="sm" leftIcon={<Icon name="check" size={13} />} loading={bulking} onClick={markAllPresent}>Mark all present</Button>
          <InfoHint title="Attendance shortcuts" side="left">
            "Fill from clock-ins" auto-marks Present anyone whose login clocked in that day. "Mark all present" fills the remaining unmarked employees. Neither overwrites a status you've already set — adjust individuals with the dropdowns.
          </InfoHint>
        </div>
      </div>
      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-11 mb-2" />)}</CardBody></Card>
      ) : (rows ?? []).length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="users" size={20} />} title="No employees" description="Add employees in HR → Employees first." /></CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR><TH>Employee</TH><TH>Agent ID</TH><TH>Designation</TH><TH>Call centre</TH><TH>Status</TH></TR></THead>
            <TBody>
              {(rows ?? []).map((r) => (
                <TR key={r.employeeId}>
                  <TD className="font-medium text-ink-900">{r.fullName}</TD>
                  <TD className="font-mono text-xs text-ink-600">{r.agentCode}</TD>
                  <TD className="text-ink-600">{hrLabel(r.designation)}</TD>
                  <TD className="text-ink-600">{r.callCenterName ?? <span className="text-ink-400">Agency-wide</span>}</TD>
                  <TD>
                    <Select aria-label={`Status for ${r.fullName}`} value={r.status ?? ""} onChange={(e) => setStatus(r.employeeId, e.target.value)} className="w-36">
                      <option value="" disabled>Mark…</option>
                      {ATTENDANCE_STATUSES.map((s) => <option key={s} value={s}>{hrLabel(s)}</option>)}
                    </Select>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
      )}
    </>
  );
}

function MonthlySummary({ callCenterId }: { callCenterId?: string }) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const { data: rows, isLoading } = useAttendanceSummaryQuery({ year, month, callCenterId });
  const monthValue = `${year}-${String(month).padStart(2, "0")}`;

  return (
    <>
      <div className="mb-4 flex items-center gap-3">
        <Input type="month" aria-label="Month" value={monthValue}
          onChange={(e) => { const [y, m] = e.target.value.split("-").map(Number); if (y && m) { setYear(y); setMonth(m); } }}
          className="w-44" />
        <span className="text-sm text-ink-500">{rows?.length ?? 0} employees</span>
      </div>
      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-11 mb-2" />)}</CardBody></Card>
      ) : (rows ?? []).length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="users" size={20} />} title="No employees" description="Add employees in HR → Employees first." /></CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
              <TH>Employee</TH><TH>Agent ID</TH>
              <TH numeric>Present</TH><TH numeric>Late</TH><TH numeric>Half</TH>
              <TH numeric>Leave</TH><TH numeric>Absent</TH><TH numeric>NCNS</TH><TH numeric>Marked</TH>
            </TR></THead>
            <TBody>
              {(rows ?? []).map((r) => (
                <TR key={r.employeeId}>
                  <TD className="font-medium text-ink-900">{r.fullName}</TD>
                  <TD className="font-mono text-xs text-ink-600">{r.agentCode}</TD>
                  <TD numeric className="tabular-nums"><Cell n={r.present} tone="success" /></TD>
                  <TD numeric className="tabular-nums"><Cell n={r.late} tone="warning" /></TD>
                  <TD numeric className="tabular-nums"><Cell n={r.halfDay} tone="warning" /></TD>
                  <TD numeric className="tabular-nums"><Cell n={r.leave} tone="info" /></TD>
                  <TD numeric className="tabular-nums"><Cell n={r.absent} tone="danger" /></TD>
                  <TD numeric className="tabular-nums"><Cell n={r.ncns} tone="danger" /></TD>
                  <TD numeric className="tabular-nums text-ink-500">{r.marked}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
      )}
    </>
  );
}

function Cell({ n, tone }: { n: number; tone: "success" | "warning" | "info" | "danger" }) {
  if (n === 0) return <span className="text-ink-300">0</span>;
  return <Badge tone={tone} variant="soft" className="tabular-nums">{n}</Badge>;
}
