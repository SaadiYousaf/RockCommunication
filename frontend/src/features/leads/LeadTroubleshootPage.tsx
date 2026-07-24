import { useState } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { useLeadDiagnosticsQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, cn, type IconName,
} from "../../shared/ui";
import type { LeadDiagnostics } from "../../shared/api/types";
import { STAGE_TONE as stageTone } from "../../shared/constants/leadStage";


const severityTone: Record<string, { tone: "danger" | "warning" | "info"; icon: IconName; bg: string; ring: string }> = {
  error:   { tone: "danger",  icon: "x",      bg: "bg-rose-50",   ring: "ring-rose-200" },
  warning: { tone: "warning", icon: "shield", bg: "bg-amber-50",  ring: "ring-amber-200" },
  info:    { tone: "info",    icon: "doc",    bg: "bg-brand-50",    ring: "ring-brand-200" },
};

function timeAgo(iso: string | null) {
  if (!iso) return "never";
  const diff = Date.now() - new Date(iso).getTime();
  const m = Math.floor(diff / 60000);
  if (m < 1) return "just now";
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

export function LeadTroubleshootPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [manualId, setManualId] = useState(id ?? "");
  const { data, isLoading, isError, refetch } = useLeadDiagnosticsQuery(id!, { skip: !id });

  if (!id) {
    return (
      <>
        <PageHeader
          title="Lead troubleshooting"
          description="Diagnose why a lead is stuck — compliance, assignment, cadence, calls, and active workflow rules."
        />
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="search" size={20} />}
            title="Pick a lead to diagnose"
            description="Paste a lead ID below or open this page from a lead's detail screen."
            action={
              <form
                onSubmit={(e) => { e.preventDefault(); if (manualId.trim()) navigate(`/leads/${manualId.trim()}/troubleshoot`); }}
                className="flex gap-2 max-w-md"
              >
                <Input
                  placeholder="Lead UUID..."
                  leftIcon={<Icon name="search" size={14} />}
                  value={manualId}
                  onChange={(e) => setManualId(e.target.value)}
                  containerClassName="flex-1"
                />
                <Button type="submit" disabled={!manualId.trim()}>Diagnose</Button>
              </form>
            }
          />
        </CardBody></Card>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title="Lead troubleshooting"
        description="Why is this lead stuck? Compliance posture, assignment, cadence, calls, and rule evaluations."
        breadcrumbs={[{ label: "Leads", to: "/leads" }, { label: "Troubleshoot" }]}
        actions={
          <>
            <Link to={`/leads/${id}`}>
              <Button variant="outline" leftIcon={<Icon name="arrowRight" size={16} className="rotate-180" />}>
                Open lead
              </Button>
            </Link>
            <Button variant="outline" leftIcon={<Icon name="filter" size={16} />} onClick={() => refetch()}>
              Refresh
            </Button>
          </>
        }
      />

      {isLoading ? (
        <div className="space-y-4">
          <Skeleton className="h-32" />
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <Skeleton className="h-48" />
            <Skeleton className="h-48" />
          </div>
        </div>
      ) : isError || !data ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="x" size={20} />}
            title="Couldn't load diagnostics"
            description="The lead may not exist or you may not have permission to see it."
            action={<Button onClick={() => refetch()}>Retry</Button>}
          />
        </CardBody></Card>
      ) : (
        <Diagnostic data={data} />
      )}
    </>
  );
}

function Diagnostic({ data }: { data: LeadDiagnostics }) {
  const errors  = data.issues.filter((i) => i.severity === "error").length;
  const warnings = data.issues.filter((i) => i.severity === "warning").length;
  const okStatus = errors === 0 && warnings === 0;

  return (
    <div className="space-y-5">
      {/* Hero */}
      <Card elevated className="overflow-hidden">
        <div className={cn(
          "relative p-6",
          okStatus
            ? "bg-gradient-to-br from-emerald-600 via-emerald-700 to-ink-900"
            : errors > 0
            ? "bg-gradient-to-br from-rose-600 via-rose-700 to-ink-900"
            : "bg-gradient-to-br from-amber-600 via-amber-700 to-ink-900",
          "text-white",
        )}>
          <div className="absolute inset-0 bg-grid opacity-20" />
          <div className="relative flex items-start justify-between gap-4 flex-wrap">
            <div className="flex items-center gap-4 min-w-0">
              <Avatar name={data.lead.name} size={56} />
              <div className="min-w-0">
                <div className="text-[11px] font-semibold uppercase tracking-[0.18em] text-white/70 mb-1">
                  {okStatus ? "All clear" : errors > 0 ? `${errors} blocker${errors === 1 ? "" : "s"}` : `${warnings} warning${warnings === 1 ? "" : "s"}`}
                </div>
                <div className="text-2xl font-semibold tracking-tight truncate">{data.lead.name}</div>
                <div className="text-xs text-white/70 mt-1 flex flex-wrap items-center gap-2">
                  <span className="font-mono">{data.lead.phone}</span>
                  {data.lead.email && <><span className="text-white/40">·</span><span>{data.lead.email}</span></>}
                  {data.lead.state && <><span className="text-white/40">·</span><span>{data.lead.state}</span></>}
                </div>
              </div>
            </div>

            <div className="grid grid-cols-3 gap-3">
              <HeroTile label="Stage"      value={data.lead.stage} />
              <HeroTile label="Score"      value={String(data.lead.score)} />
              <HeroTile label="Age"        value={`${data.lead.ageDays}d`} />
            </div>
          </div>
        </div>
      </Card>

      {/* Issues + Recommendations */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card className="lg:col-span-2">
          <CardHeader title="Issues found" subtitle={`${data.issues.length} item(s)`} />
          <CardBody className="pt-0">
            {data.issues.length === 0 ? (
              <div className="flex items-center gap-3 p-4 rounded-xl bg-emerald-50 border hairline">
                <div className="h-10 w-10 rounded-lg bg-emerald-100 text-emerald-700 grid place-items-center">
                  <Icon name="check" size={18} />
                </div>
                <div>
                  <div className="font-semibold text-emerald-900">No blockers detected</div>
                  <div className="text-sm text-emerald-800/80">This lead is healthy — proceed with the recommended action.</div>
                </div>
              </div>
            ) : (
              <ul className="space-y-2">
                {data.issues.map((issue, i) => {
                  const s = severityTone[issue.severity] ?? severityTone.info;
                  return (
                    <li key={i} className={cn("flex items-start gap-3 p-3.5 rounded-xl border hairline", s.bg, "ring-1 ring-inset", s.ring)}>
                      <div className={`h-9 w-9 rounded-lg grid place-items-center shrink-0 bg-white ring-1 ring-inset ${s.ring}`}>
                        <Badge tone={s.tone} variant="soft" className="!px-0 !bg-transparent">
                          <Icon name={s.icon} size={16} />
                        </Badge>
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <Badge tone={s.tone} variant="soft" dot>{issue.severity.toUpperCase()}</Badge>
                          <code className="font-mono text-[11px] text-ink-700 bg-white/70 px-1.5 py-0.5 rounded">{issue.code}</code>
                        </div>
                        <div className="text-sm text-ink-800 mt-1 leading-relaxed">{issue.message}</div>
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Next best action" subtitle="Suggested step" />
          <CardBody className="pt-0 space-y-3">
            {data.recommendations.map((r, i) => (
              <div key={i} className="rounded-xl border hairline p-4 hover:shadow-card transition-shadow">
                <div className="flex items-start gap-3">
                  <div className="h-9 w-9 rounded-lg bg-brand-50 text-brand-600 grid place-items-center shrink-0">
                    <Icon name="arrowRight" size={16} />
                  </div>
                  <div>
                    <div className="font-semibold text-ink-900">{r.action}</div>
                    <div className="text-xs text-ink-500 mt-1 leading-snug">{r.why}</div>
                  </div>
                </div>
              </div>
            ))}
          </CardBody>
        </Card>
      </div>

      {/* Compliance + Jornaya + Assignment */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Card>
          <CardHeader title="Compliance" subtitle="DNC / TCPA / consent" />
          <CardBody className="pt-0 space-y-2">
            <Row label="On DNC" value={
              data.compliance.onDnc
                ? <Badge tone="danger" variant="soft" dot>Blocked</Badge>
                : <Badge tone="success" variant="soft" dot>Clear</Badge>
            } />
            {data.compliance.onDnc && data.compliance.dncReason && (
              <Row label="DNC reason" value={<span className="text-sm text-ink-700">{data.compliance.dncReason}</span>} />
            )}
            <Row label="TCPA window" value={
              data.compliance.tcpaWindowOk
                ? <Badge tone="success" variant="soft" dot>OK</Badge>
                : <Badge tone="warning" variant="soft" dot>Outside</Badge>
            } />
            <Row label="Consent" value={
              data.compliance.consentCaptured
                ? <Badge tone="success" variant="soft" dot>Captured</Badge>
                : <Badge tone="warning" variant="soft" dot>Missing</Badge>
            } />
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Jornaya / LeadiD" />
          <CardBody className="pt-0 space-y-2">
            <Row label="Verified" value={
              data.jornaya.verified
                ? <Badge tone="success" variant="soft" dot>Verified</Badge>
                : <Badge tone="warning" variant="soft" dot>Pending</Badge>
            } />
            {data.jornaya.verifiedAt && (
              <Row label="Verified at" value={<span className="text-xs text-ink-600">{new Date(data.jornaya.verifiedAt).toLocaleString()}</span>} />
            )}
            {data.jornaya.leadId && (
              <Row label="LeadID" value={<code className="font-mono text-[11px] text-ink-700">{data.jornaya.leadId}</code>} />
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Assignment" />
          <CardBody className="pt-0 space-y-2">
            <Row label="Assigned" value={
              data.assignment.assigned
                ? <Badge tone="success" variant="soft" dot>{data.assignment.assignedUserName ?? "Yes"}</Badge>
                : <Badge tone="danger" variant="soft" dot>Unassigned</Badge>
            } />
            <Row label="Team" value={data.assignment.team
              ? <Badge tone="info" variant="soft">{data.assignment.team}</Badge>
              : <span className="text-ink-400">—</span>} />
            <Row label="Required skill" value={data.assignment.requiredSkill
              ? <Badge tone="brand" variant="soft" className="font-mono">{data.assignment.requiredSkill}</Badge>
              : <span className="text-ink-400">—</span>} />
          </CardBody>
        </Card>
      </div>

      {/* Cadence + Call activity */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader
            title="Cadence enrollments"
            subtitle={`${data.cadence.activeEnrollments} active`}
          />
          <CardBody className="pt-0">
            {data.cadence.enrollments.length === 0 ? (
              <EmptyState
                icon={<Icon name="filter" size={18} />}
                title="No enrollments"
                description="This lead isn't currently in any automated cadence."
              />
            ) : (
              <ul className="space-y-2">
                {data.cadence.enrollments.map((e) => (
                  <li key={e.enrollmentId} className="flex items-center gap-3 p-3 rounded-lg border hairline">
                    <div className="h-9 w-9 rounded-lg bg-accent-50 text-accent-600 grid place-items-center">
                      <Icon name="filter" size={16} />
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="text-sm font-medium text-ink-900 truncate">{e.cadenceName}</div>
                      <div className="text-xs text-ink-500">
                        Step {e.currentStep} / {e.totalSteps} · next run {timeAgo(e.nextRunAt)}
                      </div>
                    </div>
                    <Badge tone={e.status === "Active" ? "success" : "neutral"} variant="soft" dot>{e.status}</Badge>
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Call activity"
            subtitle={`${data.callActivity.totalCalls} call(s) · ${data.callActivity.answeredCalls} answered`}
            action={data.callActivity.unwrappedCalls > 0 && (
              <Badge tone="warning" variant="soft" dot>
                {data.callActivity.unwrappedCalls} unwrapped
              </Badge>
            )}
          />
          <CardBody className="pt-0">
            {data.callActivity.recent.length === 0 ? (
              <EmptyState
                icon={<Icon name="phone" size={18} />}
                title="No calls yet"
                description="Once an agent dials this lead it'll show up here."
              />
            ) : (
              <ul className="space-y-2">
                {data.callActivity.recent.map((c) => (
                  <li key={c.id} className="flex items-center gap-3 p-3 rounded-lg border hairline">
                    <div className="h-9 w-9 rounded-lg bg-brand-50 text-brand-600 grid place-items-center">
                      <Icon name="phone" size={16} />
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="text-sm font-medium text-ink-900 truncate">
                        {c.agentName ?? "Unknown agent"}
                        <span className="ml-2 text-xs text-ink-500">{timeAgo(c.initiatedAt)}</span>
                      </div>
                      <div className="text-xs text-ink-500 mt-0.5 flex items-center gap-1.5 flex-wrap">
                        <Badge tone={c.direction === "Inbound" ? "info" : "brand"} variant="soft">{c.direction}</Badge>
                        <Badge tone="neutral" variant="soft">{c.status}</Badge>
                        {c.wrapUpCode
                          ? <code className="font-mono text-[11px] text-ink-700">{c.wrapUpCode}</code>
                          : <Badge tone="warning" variant="soft">No wrap-up</Badge>}
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>
      </div>

      {/* Workflow rules + executions */}
      <Card>
        <CardHeader title="Workflow rules" subtitle="Rules that should fire for this lead's events." />
        <CardBody className="pt-0 px-0">
          {data.workflows.activeRules.length === 0 ? (
            <div className="px-5 pb-5">
              <EmptyState
                icon={<Icon name="filter" size={18} />}
                title="No matching rules"
                description="No workflow rules are configured for the events this lead would emit."
              />
            </div>
          ) : (
            <Table className="border-0 shadow-none rounded-none">
              <THead><TR>
                <TH>Rule</TH><TH>Event</TH><TH>Status</TH>
              </TR></THead>
              <TBody>
                {data.workflows.activeRules.map((r) => (
                  <TR key={r.ruleId}>
                    <TD className="font-medium text-ink-900">{r.name}</TD>
                    <TD>
                      <code className="font-mono text-[11px] bg-ink-100 px-1.5 py-0.5 rounded text-ink-800">{r.eventType}</code>
                    </TD>
                    <TD>
                      {r.active
                        ? <Badge tone="success" variant="soft" dot>Active</Badge>
                        : <Badge tone="neutral" variant="soft">Disabled</Badge>}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      {data.workflows.recentExecutions.length > 0 && (
        <Card>
          <CardHeader title="Recent executions for this lead" />
          <CardBody className="pt-0 px-0">
            <Table className="border-0 shadow-none rounded-none">
              <THead><TR>
                <TH>When</TH><TH>Event</TH><TH>Status</TH><TH>Error</TH>
              </TR></THead>
              <TBody>
                {data.workflows.recentExecutions.map((e, i) => (
                  <TR key={i}>
                    <TD className="text-xs text-ink-600">{new Date(e.startedAt).toLocaleString()}</TD>
                    <TD>
                      <code className="font-mono text-[11px] bg-ink-100 px-1.5 py-0.5 rounded text-ink-800">{e.eventType}</code>
                    </TD>
                    <TD>
                      <Badge
                        tone={e.status === "Succeeded" || e.status === "Success" ? "success"
                          : e.status === "Failed" ? "danger"
                          : "neutral"}
                        variant="soft" dot
                      >{e.status}</Badge>
                    </TD>
                    <TD className="text-xs text-rose-600 max-w-md truncate">
                      {e.error?.split("\n")[0] ?? <span className="text-ink-400">—</span>}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </CardBody>
        </Card>
      )}
    </div>
  );

  function HeroTile({ label, value }: { label: string; value: string }) {
    return (
      <div className="rounded-xl bg-white/10 ring-1 ring-white/15 backdrop-blur p-3 text-center">
        <div className="text-[10px] uppercase tracking-[0.16em] text-white/70">{label}</div>
        <div className="text-lg font-semibold mt-1">
          {label === "Stage"
            ? <Badge tone={stageTone[value] ?? "neutral"} variant="solid" dot>{value}</Badge>
            : value}
        </div>
      </div>
    );
  }
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1.5">
      <div className="text-xs font-medium text-ink-500 uppercase tracking-wider">{label}</div>
      <div>{value}</div>
    </div>
  );
}
