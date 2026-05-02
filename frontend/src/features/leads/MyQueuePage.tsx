import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  useDialLeadMutation, useMyLeadsQuery, useTransitionLeadMutation,
} from "../../shared/api/baseApi";
import type { LeadDisposition, WorkflowStage } from "../../shared/api/types";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, Input, PageHeader,
  Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";

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

const stageMap: Record<number | string, WorkflowStage> = {
  0: "New", 10: "Fronted", 20: "Verified", 30: "JrClosed", 40: "Closed",
  50: "Validated", 60: "Funded", 70: "Followup", 80: "Winback", 90: "Lost",
  New:"New",Fronted:"Fronted",Verified:"Verified",JrClosed:"JrClosed",Closed:"Closed",Validated:"Validated",Funded:"Funded",Followup:"Followup",Winback:"Winback",Lost:"Lost",
};

const stageTone: Record<WorkflowStage, "brand" | "info" | "warning" | "neutral" | "success" | "danger"> = {
  New: "brand", Fronted: "info", Verified: "info", JrClosed: "warning",
  Closed: "warning", Validated: "success", Funded: "success",
  Followup: "neutral", Winback: "neutral", Lost: "danger",
};

const stageOf = (s: any): WorkflowStage => stageMap[s] ?? "New";

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

  async function dialFromRow(id: string, name: string) {
    try {
      await dial({ leadId: id }).unwrap();
      toast.success("Calling…", name);
      navigate(`/leads/${id}`);
    } catch (err: any) {
      toast.error("Couldn't dial", err?.data?.detail ?? "Try again.");
    }
  }

  async function quick(id: string, toStage: WorkflowStage, disposition: LeadDisposition, name: string) {
    try {
      await transition({ id, toStage, disposition }).unwrap();
      toast.success("Disposition saved", `${name} → ${disposition}`);
      refetch();
    } catch (err: any) {
      toast.error("Couldn't update", err?.data?.detail ?? "Try again.");
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
        <Stat label="Active"   value={counts.Active}        icon={<Icon name="inbox" size={16} />}    tone="brand"    onClick={() => setFilter("Active")} />
        <Stat label="Total"    value={counts.All}           icon={<Icon name="rows" size={16} />}     tone="neutral"  onClick={() => setFilter("All")} />
        <Stat label="Fronted"  value={counts.Fronted ?? 0}  icon={<Icon name="phoneOut" size={16} />} tone="success"  onClick={() => setFilter("Fronted")} />
        <Stat label="Followup" value={counts.Followup ?? 0} icon={<Icon name="clock" size={16} />}    tone="warning"  onClick={() => setFilter("Followup")} />
      </div>

      <Card className="mb-4">
        <CardBody>
          <Input
            leftIcon={<Icon name="search" size={16} />}
            placeholder="Search my queue by name, phone, or email..."
            value={search} onChange={(e) => setSearch(e.target.value)}
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
              <TH>Lead</TH>
              <TH>Phone</TH>
              <TH>Stage</TH>
              <TH className="text-right">Quick actions</TH>
            </TR>
          </THead>
          <TBody>
            {filtered.map((l) => {
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
