import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  useDialLeadMutation, useMyLeadsQuery, useTransitionLeadMutation,
} from "../../shared/api/baseApi";
import type { LeadDisposition, WorkflowStage } from "../../shared/api/types";
import {
  Avatar, Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, InfoHint, PageHeader,
  Pager, SearchInput, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tabs, usePagination, useToast,
} from "../../shared/ui";
import { STAGE_TONE as stageTone, stageOf } from "../../shared/constants/leadStage";
import { timeAgoShort, waitTone } from "../../shared/lib/time";
import { formatPhone } from "../../shared/lib/format";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { LEADS_MSG } from "./messages";

const NEXT_STAGES: Record<WorkflowStage, WorkflowStage[]> = {
  New: ["Fronted", "Lost"],
  Fronted: ["Verified", "Followup", "Lost"],
  Verified: ["JrClosed", "Closed", "Followup", "Lost"],
  JrClosed: ["Closed", "Followup", "Lost"],
  Closed: ["Validated", "Lost"],
  Validated: ["Funded", "Lost"],
  Funded: ["Followup"],
  Followup: ["Fronted", "Verified", "Closed", "Winback", "Lost"],
  Winback: ["Fronted", "Lost"],
  Lost: ["Winback"],
};

const QUICK_DISPOSITIONS: { kind: WorkflowStage; disp: LeadDisposition; label: string }[] = [
  { kind: "Lost", disp: "NoAnswer", label: "No answer" },
  { kind: "Lost", disp: "Voicemail", label: "Voicemail" },
  { kind: "Followup", disp: "CallBack", label: "Call back" },
  { kind: "Lost", disp: "DoNotCall", label: "DNC" },
  { kind: "Lost", disp: "WrongNumber", label: "Wrong #" },
];

export function MyQueuePage() {
  const navigate = useNavigate();
  const toast = useToast();
  // Poll: managers/round-robin can assign leads to me while I'm on this page.
  const { data: leads, isLoading, refetch } = useMyLeadsQuery(undefined, { pollingInterval: 30_000 });
  const [transition, { isLoading: transitioning }] = useTransitionLeadMutation();
  const [dial, { isLoading: dialing }] = useDialLeadMutation();
  const [busyId, setBusyId] = useState<string | null>(null);   // the row whose action is in flight

  const [filter, setFilter] = useState<WorkflowStage | "All" | "Active">("Active");
  const [search, setSearch] = useState("");

  const counts = useMemo(() => {
    const c: Record<string, number> = { All: leads?.length ?? 0, Active: 0 };
    leads?.forEach((l) => {
      const s = stageOf(l.stage);
      c[s] = (c[s] ?? 0) + 1;
      if (s !== "Lost" && s !== "Funded") c.Active++;
    });
    return c;
  }, [leads]);

  const filtered = useMemo(() => {
    if (!leads) return [];
    const q = search.trim().toLowerCase();
    return leads.filter((l) => {
      const s = stageOf(l.stage);
      if (filter === "Active" && (s === "Lost" || s === "Funded")) return false;
      if (filter !== "All" && filter !== "Active" && s !== filter) return false;
      if (!q) return true;
      const name = `${l.firstName} ${l.lastName}`.toLowerCase();
      return name.includes(q) || l.phoneNumber.toLowerCase().includes(q) || (l.email ?? "").toLowerCase().includes(q);
    });
  }, [leads, filter, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: {
      name: (l) => `${l.firstName} ${l.lastName}`.trim(),
      stage: (l) => stageOf(l.stage),
    },
  });

  // Paging is purely presentational — it slices the already-filtered+sorted list for display.
  // Selection and CSV export below still work off the FULL filtered list.
  const pg = usePagination(sorted);

  const sel = useRowSelection(sorted.map((l) => l.id));

  function exportSelected() {
    const chosen = sorted.filter((l) => sel.isSelected(l.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (l) => `${l.firstName} ${l.lastName}`.trim() },
      { header: "Phone", value: (l) => formatPhone(l.phoneNumber) },
      { header: "Stage", value: (l) => stageOf(l.stage) },
      { header: "Next action", value: (l) => String(l.disposition) },
    ], `my-queue-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(LEADS_MSG.exportReadyTitle, LEADS_MSG.exportRows(chosen.length));
  }

  async function dialFromRow(id: string, name: string) {
    setBusyId(id);
    try {
      await dial({ leadId: id }).unwrap();
      toast.success(LEADS_MSG.callingTitle, name);
      navigate(`/leads/${id}`);
    } catch (err: unknown) {
      toast.error(LEADS_MSG.dialFailedTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
    } finally {
      setBusyId(null);
    }
  }

  async function quick(id: string, toStage: WorkflowStage, disposition: LeadDisposition, name: string) {
    setBusyId(id);
    try {
      await transition({ id, toStage, disposition }).unwrap();
      toast.success(LEADS_MSG.dispositionSavedTitle, LEADS_MSG.dispositionSavedDesc(name, disposition));
      refetch();
    } catch (err: unknown) {
      toast.error(LEADS_MSG.queueUpdateFailedTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
    } finally {
      setBusyId(null);
    }
  }

  const tabItems: { value: typeof filter; label: string; count?: number }[] = [
    { value: "Active", label: "Active", count: counts.Active },
    { value: "All", label: "All", count: counts.All },
    { value: "Fronted", label: "Fronted", count: counts.Fronted ?? 0 },
    { value: "Verified", label: "Verified", count: counts.Verified ?? 0 },
    { value: "Followup", label: "Followup", count: counts.Followup ?? 0 },
  ];

  return (
    <>
      <PageHeader
        eyebrow="Workspace"
        title="My Queue"
        description="Leads currently assigned to you. Dial, dispose, and move them through the pipeline."
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Active"   value={counts.Active}        icon={<Icon name="inbox" size={16} />}    tone="brand"    hint="Still in play — excludes Lost & Funded" onClick={() => setFilter("Active")} />
        <Stat label="Total"    value={counts.All}           icon={<Icon name="rows" size={16} />}     tone="neutral"  onClick={() => setFilter("All")} />
        <Stat label="Fronted"  value={counts.Fronted ?? 0}  icon={<Icon name="phoneOut" size={16} />} tone="success"  onClick={() => setFilter("Fronted")} />
        <Stat label="Followup" value={counts.Followup ?? 0} icon={<Icon name="clock" size={16} />}    tone="warning"  onClick={() => setFilter("Followup")} />
      </div>

      <Card className="mb-4">
        <CardBody>
          <SearchInput
            value={search} onChange={setSearch}
            placeholder={LEADS_MSG.myQueueSearchPlaceholder}
          />
        </CardBody>
        <div className="px-5 -mt-2 pb-1 overflow-x-auto">
          <Tabs<typeof filter> value={filter} onChange={setFilter} items={tabItems} />
        </div>
      </Card>

      {isLoading ? (
        <Card><CardBody>
          {[0, 1, 2, 3].map((i) => (
            <div key={i} className="flex items-center gap-3 py-3 border-b hairline last:border-0">
              <Skeleton className="h-9 w-9 rounded-full" />
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-5 w-16 rounded-full ml-auto" />
            </div>
          ))}
        </CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="inbox" size={20} />}
            title={leads && leads.length === 0 ? LEADS_MSG.queueEmptyTitle : LEADS_MSG.queueNoMatchTitle}
            description={leads && leads.length === 0
              ? LEADS_MSG.queueEmptyDesc
              : LEADS_MSG.queueNoMatchDesc}
          />
        </CardBody></Card>
      ) : (
        <>
        <Table>
          <THead>
            <TR>
              <TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH>
              <TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Lead</TH>
              <TH sortDir={dirFor("phoneNumber")} onClick={() => toggle("phoneNumber")}>Phone</TH>
              <TH sortDir={dirFor("stage")} onClick={() => toggle("stage")}>
                <span className="inline-flex items-center gap-1">Stage
                  <InfoHint title="Pipeline stage" side="bottom">
                    The lead's current step in the pipeline: New → Fronted → Verified → Closed → Validated → Funded (or off-track Followup / Winback / Lost).
                  </InfoHint>
                </span>
              </TH>
              <TH sortDir={dirFor("createdAt")} onClick={() => toggle("createdAt")}>
                <span className="inline-flex items-center gap-1">Waiting
                  <InfoHint title="Time in your queue" side="bottom">How long this lead has been waiting for your next action — red is going stale.</InfoHint>
                </span>
              </TH>
              <TH className="text-right">
                <span className="inline-flex items-center gap-1">Quick actions
                  <InfoHint title="Quick actions" side="left">
                    One-tap outcomes that log the call result and move the lead. DNC = Do Not Call (never dial again); Wrong # marks a bad number.
                  </InfoHint>
                </span>
              </TH>
            </TR>
          </THead>
          <TBody>
            {pg.pageItems.map((l) => {
              const stage = stageOf(l.stage);
              const name = `${l.firstName} ${l.lastName}`.trim();
              const next = NEXT_STAGES[stage];
              return (
                <TR key={l.id} className={sel.isSelected(l.id) ? "bg-brand-50/40" : undefined}>
                  <TD><Checkbox aria-label={`Select ${name}`} {...sel.checkboxProps(l.id)} /></TD>
                  <TD>
                    <div className="flex items-center gap-3">
                      <Avatar name={name} size={36} />
                      <div className="min-w-0">
                        <Link to={`/leads/${l.id}`} className="font-medium text-ink-900 hover:text-brand-700">
                          {name}
                        </Link>
                        {l.email && <div className="text-xs text-ink-500 truncate">{l.email}</div>}
                      </div>
                    </div>
                  </TD>
                  <TD className="font-mono text-xs text-ink-700 tabular-nums whitespace-nowrap">{formatPhone(l.phoneNumber)}</TD>
                  <TD><Badge tone={stageTone[stage]} variant="soft" dot>{stage}</Badge></TD>
                  <TD className="whitespace-nowrap">
                    <span title={new Date(l.createdAt).toLocaleString()}>
                      <Badge tone={waitTone(l.createdAt)} variant="soft">{timeAgoShort(l.createdAt)}</Badge>
                    </span>
                  </TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1.5 flex-wrap">
                      <Button size="sm" leftIcon={<Icon name="phoneCall" size={13} />}
                        loading={dialing && busyId === l.id} disabled={busyId === l.id}
                        onClick={() => dialFromRow(l.id, name)}>Dial</Button>
                      {next.includes("Verified") && (
                        <Button size="sm" variant="outline"
                          loading={transitioning && busyId === l.id} disabled={busyId === l.id}
                          onClick={() => quick(l.id, "Verified", "Interested", name)}>Verified</Button>
                      )}
                      {next.includes("Closed") && (
                        <Button size="sm" variant="outline"
                          loading={transitioning && busyId === l.id} disabled={busyId === l.id}
                          onClick={() => quick(l.id, "Closed", "Sold", name)}>Closed</Button>
                      )}
                      {QUICK_DISPOSITIONS.filter(q => next.includes(q.kind)).slice(0, 2).map(q => (
                        <Button key={q.label} size="sm" variant="ghost"
                          loading={transitioning && busyId === l.id} disabled={busyId === l.id}
                          onClick={() => quick(l.id, q.kind, q.disp, name)}>{q.label}</Button>
                      ))}
                      <Link to={`/leads/${l.id}`}>
                        <Button variant="ghost" size="sm" rightIcon={<Icon name="chevronRight" size={13} />}>Open</Button>
                      </Link>
                    </div>
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
        <Pager {...pg} onPage={pg.setPage} unit="leads" />
        </>
      )}
      <BulkActionBar count={sel.selectedCount} itemNoun="lead" onClear={sel.clear}
        actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]} />
    </>
  );
}
