import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  useDialLeadMutation, useMyLeadsQuery, useTransitionLeadMutation,
} from "../../shared/api/baseApi";
import type { LeadDisposition, WorkflowStage } from "../../shared/api/types";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, InfoHint, PageHeader,
  SearchInput, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";
import { STAGE_TONE as stageTone, stageOf } from "../../shared/constants/leadStage";
import { timeAgoShort, waitTone } from "../../shared/lib/time";
import { useTableSort } from "../../shared/hooks/useTableSort";

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




function formatPhone(p: string) {
  const d = (p || "").replace(/\D/g, "");
  if (d.length === 10) return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
  return p;
}

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
  const { data: leads, isLoading, refetch } = useMyLeadsQuery();
  const [transition] = useTransitionLeadMutation();
  const [dial] = useDialLeadMutation();

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

  async function dialFromRow(id: string, name: string) {
    try {
      await dial({ leadId: id }).unwrap();
      toast.success("Calling…", name);
      navigate(`/leads/${id}`);
    } catch (err: unknown) {
      toast.error("Couldn't dial", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function quick(id: string, toStage: WorkflowStage, disposition: LeadDisposition, name: string) {
    try {
      await transition({ id, toStage, disposition }).unwrap();
      toast.success("Disposition saved", `${name} → ${disposition}`);
      refetch();
    } catch (err: unknown) {
      toast.error("Couldn't update", getErrorDetail(err) ?? "Try again.");
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
            placeholder="Search my queue by name, phone, or email…"
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
            title={leads && leads.length === 0 ? "No leads in your queue" : "Nothing matches your filter"}
            description={leads && leads.length === 0
              ? "Leads assigned to you will appear here. Speak to your team lead about pulling some."
              : "Try clearing the search or switching the tab."}
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
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
            {sorted.map((l) => {
              const stage = stageOf(l.stage);
              const name = `${l.firstName} ${l.lastName}`.trim();
              const next = NEXT_STAGES[stage];
              return (
                <TR key={l.id}>
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
                  <TD className="font-mono text-xs text-ink-700">{formatPhone(l.phoneNumber)}</TD>
                  <TD><Badge tone={stageTone[stage]} variant="soft" dot>{stage}</Badge></TD>
                  <TD className="whitespace-nowrap">
                    <span title={new Date(l.createdAt).toLocaleString()}>
                      <Badge tone={waitTone(l.createdAt)} variant="soft">{timeAgoShort(l.createdAt)}</Badge>
                    </span>
                  </TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1.5 flex-wrap">
                      <Button size="sm" leftIcon={<Icon name="phoneCall" size={13} />}
                        onClick={() => dialFromRow(l.id, name)}>Dial</Button>
                      {next.includes("Verified") && (
                        <Button size="sm" variant="outline"
                          onClick={() => quick(l.id, "Verified", "Interested", name)}>Verified</Button>
                      )}
                      {next.includes("Closed") && (
                        <Button size="sm" variant="outline"
                          onClick={() => quick(l.id, "Closed", "Sold", name)}>Closed</Button>
                      )}
                      {QUICK_DISPOSITIONS.filter(q => next.includes(q.kind)).slice(0, 2).map(q => (
                        <Button key={q.label} size="sm" variant="ghost"
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
      )}
    </>
  );
}
