import { getErrorDetail } from "../../shared/api/apiError";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useAttendanceDayQuery, useAttendanceSummaryQuery, useMarkAttendanceMutation, useListCallCentersQuery,
  useBulkMarkAttendanceMutation, useFillAttendanceFromClockInsMutation,
} from "../../shared/api/baseApi";
import { ATTENDANCE_STATUSES, hrLabel } from "../../shared/constants/hr";
import {
  Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, InfoHint, Input, PageHeader, SearchInput,
  Select, Skeleton, Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { HR_MSG } from "./messages";

type Tab = "daily" | "summary";
const today = () => new Date().toISOString().slice(0, 10);

/**
 * HR → Attendance register. Mark each agent Present/Absent/Late/Half/Leave/NCNS per day
 * (the daily tab), and roll it up per month (the summary tab, which feeds payroll).
 */
export function HrAttendancePage() {
  const [tab, setTab] = useState<Tab>("daily");
  // Default the call-centre filter to the admin's chosen workspace (and re-sync when they switch),
  // so the page opens focused on the call center they're working in.
  const scopedCallCenter = useSelector((s: RootState) => s.auth.user?.callCenterId) ?? "";
  const [callCenterId, setCallCenterId] = useState(scopedCallCenter);
  useEffect(() => { setCallCenterId(scopedCallCenter); }, [scopedCallCenter]);
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
  const confirm = useConfirm();
  const [bulkStatus, setBulkStatus] = useState("Present");
  const [search, setSearch] = useState("");

  // Client-side search over the day's already-loaded register — the query above is untouched.
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const all = rows ?? [];
    if (!q) return all;
    return all.filter((r) =>
      [r.fullName, r.agentCode, hrLabel(r.designation), r.callCenterName]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [rows, search]);
  // Selection is scoped to the visible (searched) rows so a bulk action never reaches a hidden one.
  const sel = useRowSelection(filtered.map((r) => r.employeeId));
  const [applying, setApplying] = useState(false);

  async function setStatus(employeeId: string, status: string) {
    try { await mark({ employeeId, date, status }).unwrap(); }
    catch (err: unknown) { toast.error(HR_MSG.saveFailed, getErrorDetail(err) ?? HR_MSG.retry); }
  }

  // Bulk-set the chosen status on ONLY the ticked employees (the "Apply to all" button above
  // still covers everyone). Reuses the per-row mutation so there's no new endpoint to trust.
  async function applyToSelected() {
    const ids = sel.selectedIds;
    if (ids.length === 0) return;
    const label = hrLabel(bulkStatus);
    if (!(await confirm({
      title: HR_MSG.setToConfirmTitle(ids.length, label),
      description: HR_MSG.setToConfirmDesc(ids.length, label, date),
      confirmLabel: HR_MSG.setToConfirmLabel(label),
    }))) return;
    setApplying(true);
    try {
      await Promise.all(ids.map((employeeId) => mark({ employeeId, date, status: bulkStatus }).unwrap()));
      toast.success(HR_MSG.setCountToLabel(ids.length, label));
      sel.clear();
    } catch (err: unknown) { toast.error(HR_MSG.applyFailed, getErrorDetail(err) ?? HR_MSG.retry); }
    finally { setApplying(false); }
  }

  function exportSelected() {
    const chosen = filtered.filter((r) => sel.isSelected(r.employeeId));
    exportRowsToCsv(chosen, [
      { header: "Employee", value: (r) => r.fullName },
      { header: "Agent ID", value: (r) => r.agentCode },
      { header: "Designation", value: (r) => hrLabel(r.designation) },
      { header: "Call centre", value: (r) => r.callCenterName ?? "Agency-wide" },
      { header: "Status", value: (r) => (r.status ? hrLabel(r.status) : "") },
    ], `attendance-${date}.csv`);
    toast.success(HR_MSG.exportReady, HR_MSG.rowsDownloaded(chosen.length));
  }
  async function applyToAll() {
    const label = hrLabel(bulkStatus);
    const n = rows?.length ?? 0;
    if (!(await confirm({
      title: HR_MSG.setEveryoneTitle(label),
      description: HR_MSG.setEveryoneDesc(n, label, date),
      confirmLabel: HR_MSG.setAllConfirmLabel(label),
    }))) return;
    try {
      const r = await bulk({ date, status: bulkStatus, callCenterId, onlyUnmarked: false }).unwrap();
      toast.success(HR_MSG.setCountToLabel(r.count, label), HR_MSG.everyoneUpdatedDesc);
    } catch (err: unknown) { toast.error(HR_MSG.applyAllFailed, getErrorDetail(err) ?? HR_MSG.retry); }
  }
  async function fillFromClockIns() {
    try {
      const r = await fill({ date, callCenterId }).unwrap();
      toast.success(r.count > 0 ? HR_MSG.markedPresentFromClockIns(r.count) : HR_MSG.noNewClockIns, HR_MSG.clockInsDesc);
    } catch (err: unknown) { toast.error(HR_MSG.fillFailed, getErrorDetail(err) ?? HR_MSG.retry); }
  }

  return (
    <>
      <div className="mb-4 flex items-center gap-3 flex-wrap">
        <Input type="date" aria-label="Date" value={date} onChange={(e) => setDate(e.target.value)} className="w-44" />
        <SearchInput value={search} onChange={setSearch} placeholder={HR_MSG.attendanceSearchPlaceholder} className="w-64" />
        <span className="text-sm text-ink-500">{rows?.length ?? 0} {(rows?.length ?? 0) === 1 ? "employee" : "employees"}</span>
        <div className="flex items-center gap-2 ml-auto">
          <Button variant="outline" size="sm" leftIcon={<Icon name="phone" size={13} />} loading={filling} onClick={fillFromClockIns}>Fill from clock-ins</Button>
          <div className="flex items-center gap-1.5">
            <Select aria-label="Status to apply to everyone" value={bulkStatus} onChange={(e) => setBulkStatus(e.target.value)} className="w-32">
              {ATTENDANCE_STATUSES.map((s) => <option key={s} value={s}>{hrLabel(s)}</option>)}
            </Select>
            <Button variant="outline" size="sm" leftIcon={<Icon name="check" size={13} />} loading={bulking} onClick={applyToAll}>Apply to all</Button>
          </div>
          <InfoHint title="Bulk actions" side="left">
            "Fill from clock-ins" marks Present anyone whose login clocked in that day (only the unmarked). "Apply to all" sets the chosen status — Present, Absent, Leave, NCNS, anything — for every employee on this date, overwriting existing marks. Fine-tune anyone with their own dropdown.
          </InfoHint>
        </div>
      </div>
      <p className="text-[11px] text-ink-400 flex items-center gap-1.5 mb-3">
        <Icon name="refresh" size={11} /> Marks save instantly here; the dashboard "Team status" widget updates every minute.
      </p>
      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-11 mb-2" />)}</CardBody></Card>
      ) : (rows ?? []).length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="users" size={20} />} title={HR_MSG.noEmployeesTitle} description={HR_MSG.addEmployeesFirst} /></CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="search" size={20} />} title={HR_MSG.noMatchesTitle} description={HR_MSG.noEmployeeSearchMatchesDesc} /></CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
              <TH className="w-10">
                <Checkbox aria-label="Select all employees" {...sel.allCheckboxProps} />
              </TH>
              <TH>Employee</TH>
              <TH><span className="inline-flex items-center gap-1">Agent ID<InfoHint title="Agent ID" side="top">The employee's unique agent code, shared across the dialer, sales, and payroll systems.</InfoHint></span></TH>
              <TH>Designation</TH>
              <TH><span className="inline-flex items-center gap-1">Call centre<InfoHint title="Call centre" side="top">The centre this agent works from; "Agency-wide" means they aren't tied to a single centre.</InfoHint></span></TH>
              <TH><span className="inline-flex items-center gap-1">Status<InfoHint title="Attendance status" side="left">Late = arrived after start; Half day = half a shift worked; Leave = pre-approved time off; NCNS = no call, no show (an unexcused absence).</InfoHint></span></TH>
            </TR></THead>
            <TBody>
              {filtered.map((r) => (
                <TR key={r.employeeId} className={sel.isSelected(r.employeeId) ? "bg-brand-50/40" : undefined}>
                  <TD>
                    <Checkbox aria-label={`Select ${r.fullName}`} {...sel.checkboxProps(r.employeeId)} />
                  </TD>
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
          <BulkActionBar
            count={sel.selectedCount} itemNoun="employee" onClear={sel.clear}
            extra={
              <div className="flex items-center gap-1.5 pl-1">
                <Select aria-label="Status to apply to selected" value={bulkStatus} onChange={(e) => setBulkStatus(e.target.value)}
                  className="h-8 w-32 !bg-white/10 !border-white/20 !text-white text-sm">
                  {ATTENDANCE_STATUSES.map((s) => <option key={s} value={s} className="text-ink-900">{hrLabel(s)}</option>)}
                </Select>
              </div>
            }
            actions={[
              { key: "apply", label: "Set status", icon: "check", onClick: applyToSelected, loading: applying },
              { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
            ]}
          />
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
  const [search, setSearch] = useState("");

  // Client-side search over the month's already-loaded roll-up — the query above is untouched.
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const all = rows ?? [];
    if (!q) return all;
    return all.filter((r) =>
      [r.fullName, r.agentCode, hrLabel(r.designation), r.callCenterName]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [rows, search]);

  return (
    <>
      <div className="mb-4 flex items-center gap-3 flex-wrap">
        <Input type="month" aria-label="Month" value={monthValue}
          onChange={(e) => { const [y, m] = e.target.value.split("-").map(Number); if (y && m) { setYear(y); setMonth(m); } }}
          className="w-44" />
        <SearchInput value={search} onChange={setSearch} placeholder={HR_MSG.attendanceSearchPlaceholder} className="w-64" />
        <span className="text-sm text-ink-500">{rows?.length ?? 0} {(rows?.length ?? 0) === 1 ? "employee" : "employees"}</span>
      </div>
      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-11 mb-2" />)}</CardBody></Card>
      ) : (rows ?? []).length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="users" size={20} />} title={HR_MSG.noEmployeesTitle} description={HR_MSG.addEmployeesFirst} /></CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody><EmptyState icon={<Icon name="search" size={20} />} title={HR_MSG.noMatchesTitle} description={HR_MSG.noEmployeeSearchMatchesDesc} /></CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead><TR>
              <TH>Employee</TH>
              <TH><span className="inline-flex items-center gap-1">Agent ID<InfoHint title="Agent ID" side="top">The employee's unique agent code, shared across the dialer, sales, and payroll systems.</InfoHint></span></TH>
              <TH numeric>Present</TH><TH numeric>Late</TH>
              <TH numeric><span className="inline-flex items-center gap-1">Half<InfoHint title="Half day" side="top">Days where only half a shift was worked.</InfoHint></span></TH>
              <TH numeric><span className="inline-flex items-center gap-1">Leave<InfoHint title="Leave" side="top">Days of pre-approved (excused) time off.</InfoHint></span></TH>
              <TH numeric>Absent</TH>
              <TH numeric><span className="inline-flex items-center gap-1">NCNS<InfoHint title="NCNS" side="top">No call, no show — absent with no notice or approval.</InfoHint></span></TH>
              <TH numeric><span className="inline-flex items-center gap-1">Marked<InfoHint title="Days marked" side="left">Total days with any attendance status recorded this month — the base rolled up to payroll.</InfoHint></span></TH>
            </TR></THead>
            <TBody>
              {filtered.map((r) => (
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
