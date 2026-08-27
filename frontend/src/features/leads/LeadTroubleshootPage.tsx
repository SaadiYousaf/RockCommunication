import { useState } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { useLeadDiagnosticsQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, cn, type IconName,
} from "../../shared/ui";
import type { LeadDiagnostics } from "../../shared/api/types";
import { STAGE_TONE as stageTone, stageLabel } from "../../shared/constants/leadStage";
import { timeAgo } from "../../shared/lib/time";
import { LEADS_MSG } from "./messages";


const severityTone: Record<string, { tone: "danger" | "warning" | "info"; icon: IconName; bg: string; ring: string }> = {
  error:   { tone: "danger",  icon: "x",      bg: "bg-rose-50",   ring: "ring-rose-200" },
  warning: { tone: "warning", icon: "shield", bg: "bg-amber-50",  ring: "ring-amber-200" },
  info:    { tone: "info",    icon: "doc",    bg: "bg-brand-50",    ring: "ring-brand-200" },
};

type IssueDetail = {
  what: string;
  impact: string;
  fix: string;
  cta?: string;
  to?: (leadId: string) => string;
};

/**
 * Frontend knowledge base: expands each backend issue code into actionable guidance
 * (the API only sends severity/code/message). Keyed by DiagnosticIssue.code —
 * see LeadDiagnosticsQuery on the backend for the source list of codes.
 */
const ISSUE_DETAILS: Record<string, IssueDetail> = {
  DNC: {
    what: "This phone number is on a Do-Not-Call list (internal or national).",
    impact: "The dialer blocks every outbound attempt and SMS is disallowed — contacting it risks a TCPA violation.",
    fix: "If the number is wrong, correct it on the lead. Otherwise mark the lead Lost — it can't be legally dialed.",
    cta: "Open lead", to: (id) => `/leads/${id}`,
  },
  NO_CONSENT: {
    what: "No TCPA consent record is on file for this lead.",
    impact: "You can't legally send SMS or place outbound calls until consent is captured.",
    fix: "Capture consent on the lead (or confirm the Jornaya/LeadiD token that proves it) before any outreach.",
    cta: "Open lead", to: (id) => `/leads/${id}`,
  },
  TCPA_WINDOW: {
    what: "The current time is outside the TCPA-permitted calling window for this lead's state.",
    impact: "Dialing now risks a TCPA violation, so the dialer holds outbound attempts.",
    fix: "Wait until the local window (typically 8am–9pm local time) reopens, or schedule a callback inside it.",
    cta: "Schedule a callback", to: (id) => `/leads/${id}`,
  },
  JORNAYA_PENDING: {
    what: "This lead's Jornaya/LeadiD token hasn't been verified yet.",
    impact: "Verification is a compliance gate — the lead can't move to Closed until it passes.",
    fix: "Run Jornaya verification from the lead detail page.",
    cta: "Open lead to verify", to: (id) => `/leads/${id}`,
  },
  UNASSIGNED: {
    what: "The lead has advanced past the New stage but no agent owns it.",
    impact: "Unowned leads fall through the cracks — no one actions follow-ups and routing rules skip it.",
    fix: "Assign it to a Fronter (or the right role for its stage) from the lead or the Leads list.",
    cta: "Open lead to assign", to: (id) => `/leads/${id}`,
  },
  UNWRAPPED: {
    what: "One or more calls to this lead ended without a wrap-up (disposition) code.",
    impact: "Agents can be blocked from dialing again until the prior call is wrapped, and reporting is incomplete.",
    fix: "Open Call History and add a wrap-up code to the open call(s).",
    cta: "Go to Call History", to: () => `/calls`,
  },
  NO_CADENCE: {
    what: "This lead isn't enrolled in any automated cadence.",
    impact: "Without a cadence, follow-ups rely on manual effort and the lead can go cold.",
    fix: "Enroll it in a cadence (e.g. 'New lead — 7-touch') to automate touches.",
    cta: "Go to Cadences", to: () => `/cadences`,
  },
  STALE: {
    what: "The lead has sat in its current stage well beyond the expected age.",
    impact: "Stale leads skew pipeline metrics and rarely convert without intervention.",
    fix: "Move it forward, or send it to Follow-up/Lost so the pipeline stays accurate.",
    cta: "Open lead to update stage", to: (id) => `/leads/${id}`,
  },
  WORKFLOW_FAILURE: {
    what: "An automation (workflow rule) failed while processing one of this lead's events.",
    impact: "A step that should have run automatically (assignment, notification, enrollment…) didn't — leaving the lead inconsistent.",
    fix: "Check 'Recent executions' below for the error, fix the rule in Workflows, then re-trigger the event.",
    cta: "Open Workflows", to: () => `/workflows`,
  },
};

/** Maps a recommendation's action label to an icon + the place where it's actioned. */
const REC_META: Record<string, { icon: IconName; to: (leadId: string) => string }> = {
  "Mark Lost (DNC)":          { icon: "flag",      to: (id) => `/leads/${id}` },
  "Assign to a Fronter":      { icon: "users",     to: (id) => `/leads/${id}` },
  "Run Jornaya verification": { icon: "shield",    to: (id) => `/leads/${id}` },
  "Wrap up open call":        { icon: "phone",     to: () => `/calls` },
  "Enroll in cadence":        { icon: "filter",    to: () => `/cadences` },
  "Validate the sale":        { icon: "check",     to: (id) => `/leads/${id}` },
  "Submit for funding":       { icon: "briefcase", to: (id) => `/leads/${id}` },
  "Dial the lead":            { icon: "phone",     to: (id) => `/leads/${id}` },
};

export function LeadTroubleshootPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [manualId, setManualId] = useState(id ?? "");
  const { data, isLoading, isFetching, isError, refetch } = useLeadDiagnosticsQuery(id!, { skip: !id });

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
            title={LEADS_MSG.pickLeadTitle}
            description={LEADS_MSG.pickLeadDesc}
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
                <Button type="submit" disabled={!manualId.trim()}
                  title={!manualId.trim() ? "Enter a lead ID to diagnose" : "Run diagnostics for this lead"}>
                  Diagnose
                </Button>
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
            <Button variant="outline" leftIcon={<Icon name="refresh" size={16} />} onClick={() => refetch()}
              loading={isFetching} title="Re-run the diagnostics for this lead">
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
            title={LEADS_MSG.diagnosticsFailedTitle}
            description={LEADS_MSG.diagnosticsFailedDesc}
            action={<Button onClick={() => refetch()}>Retry</Button>}
          />
        </CardBody></Card>
      ) : (
        <Diagnostic data={data} leadId={id} />
      )}
    </>
  );
}

function Diagnostic({ data, leadId }: { data: LeadDiagnostics; leadId: string }) {
  const errors  = data.issues.filter((i) => i.severity === "error").length;
  const warnings = data.issues.filter((i) => i.severity === "warning").length;
  const okStatus = errors === 0 && warnings === 0;
  // Which issue rows are expanded. Errors start open so blockers are visible at a glance.
  const [openIssues, setOpenIssues] = useState<Set<number>>(
    () => new Set(data.issues.map((iss, i) => (iss.severity === "error" ? i : -1)).filter((i) => i >= 0)),
  );
  const toggleIssue = (i: number) =>
    setOpenIssues((prev) => {
      const next = new Set(prev);
      if (next.has(i)) next.delete(i); else next.add(i);
      return next;
    });

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
                  <span className="font-mono tabular-nums">{data.lead.phone}</span>
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
          <CardHeader title="Issues found" subtitle={`${data.issues.length} item${data.issues.length === 1 ? "" : "s"}`} />
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
              <>
              <ul className="space-y-2">
                {data.issues.map((issue, i) => {
                  const s = severityTone[issue.severity] ?? severityTone.info;
                  const detail = ISSUE_DETAILS[issue.code];
                  const open = openIssues.has(i);
                  return (
                    <li key={i} className={cn("rounded-xl border hairline ring-1 ring-inset overflow-hidden", s.bg, s.ring)}>
                      <button
                        type="button"
                        onClick={() => toggleIssue(i)}
                        aria-expanded={open}
                        className="w-full flex items-start gap-3 p-3.5 text-left hover:bg-white/50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500/40"
                      >
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
                        <Icon
                          name={open ? "chevronUp" : "chevronDown"}
                          size={16}
                          className="mt-1 shrink-0 text-ink-400"
                          aria-hidden
                        />
                      </button>

                      {open && (
                        <div className="px-3.5 pb-3.5 animate-fade-in">
                          <div className="rounded-lg bg-white/80 ring-1 ring-inset ring-white/70 shadow-xs p-3.5 space-y-3">
                            {detail ? (
                              <>
                                <DetailBlock label="What it means" text={detail.what} />
                                <DetailBlock label="Why it's blocking" text={detail.impact} />
                                <DetailBlock label="How to fix it" text={detail.fix} />
                                {detail.cta && detail.to && (
                                  <Link to={detail.to(leadId)} className="inline-block pt-0.5">
                                    <Button size="sm" leftIcon={<Icon name="arrowRight" size={14} />}>
                                      {detail.cta}
                                    </Button>
                                  </Link>
                                )}
                              </>
                            ) : (
                              <div className="text-sm text-ink-600">
                                No extra guidance is available for this check. Review the lead and the detail sections below.
                              </div>
                            )}
                          </div>
                        </div>
                      )}
                    </li>
                  );
                })}
              </ul>
              {data.issues.length > 0 && (
                <p className="text-[11px] text-ink-400 mt-2.5 px-1">Tap any item for details and how to fix it.</p>
              )}
              </>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Next best action" subtitle="Suggested step" />
          <CardBody className="pt-0 space-y-3">
            {data.recommendations.length === 0 ? (
              <div className="text-sm text-ink-500 p-4 text-center">Nothing to action right now.</div>
            ) : data.recommendations.map((r, i) => {
              const meta = REC_META[r.action];
              const to = meta ? meta.to(leadId) : `/leads/${leadId}`;
              return (
                <Link
                  key={i}
                  to={to}
                  className="group block rounded-xl border hairline p-4 hover:border-brand-200 hover:bg-brand-50/40 hover:shadow-card transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
                >
                  <div className="flex items-start gap-3">
                    <div className="h-9 w-9 rounded-lg bg-brand-50 text-brand-600 grid place-items-center shrink-0 group-hover:bg-brand-100 transition-colors">
                      <Icon name={meta?.icon ?? "arrowRight"} size={16} />
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="font-semibold text-ink-900">{r.action}</div>
                      <div className="text-xs text-ink-500 mt-1 leading-snug">{r.why}</div>
                    </div>
                    <Icon
                      name="arrowRight"
                      size={16}
                      className="mt-1 shrink-0 text-ink-300 group-hover:text-brand-500 group-hover:translate-x-0.5 transition-all"
                      aria-hidden
                    />
                  </div>
                </Link>
              );
            })}
          </CardBody>
        </Card>
      </div>

      {/* Compliance + Jornaya + Assignment */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Card>
          <CardHeader
            title={
              <span className="inline-flex items-center gap-1">Compliance
                <InfoHint title="Compliance" side="bottom">
                  Whether this lead can legally be dialed right now: DNC (do-not-call) status, the TCPA-permitted local calling window, and whether consent to contact is on file.
                </InfoHint>
              </span>
            }
            subtitle="DNC / TCPA / consent"
          />
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
          <CardHeader
            title={
              <span className="inline-flex items-center gap-1">Jornaya / LeadiD
                <InfoHint title="Jornaya / LeadiD" side="bottom">
                  A consent token that independently proves the prospect agreed to be contacted; it must be verified before a sale.
                </InfoHint>
              </span>
            }
          />
          <CardBody className="pt-0 space-y-2">
            <Row label="Verified" value={
              data.jornaya.verified
                ? <Badge tone="success" variant="soft" dot>Verified</Badge>
                : <Badge tone="warning" variant="soft" dot>Pending</Badge>
            } />
            {data.jornaya.verifiedAt && (
              <Row label="Verified at" value={<span className="text-xs text-ink-600 tabular-nums whitespace-nowrap">{new Date(data.jornaya.verifiedAt).toLocaleString()}</span>} />
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
            <Row label={
              <span className="inline-flex items-center gap-1">Required skill
                <InfoHint title="Required skill" side="top">
                  A skill code (e.g. ES for Spanish) this lead needs — routing only hands it to agents who hold that skill.
                </InfoHint>
              </span>
            } value={data.assignment.requiredSkill
              ? <Badge tone="brand" variant="soft" className="font-mono">{data.assignment.requiredSkill}</Badge>
              : <span className="text-ink-400">—</span>} />
          </CardBody>
        </Card>
      </div>

      {/* Cadence + Call activity */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader
            title={
              <span className="inline-flex items-center gap-1">Cadence enrollments
                <InfoHint title="Cadence enrollments" side="bottom">
                  A cadence is an automated follow-up sequence that schedules the lead's next touches (calls/SMS) over time.
                </InfoHint>
              </span>
            }
            subtitle={`${data.cadence.activeEnrollments} active`}
          />
          <CardBody className="pt-0">
            {data.cadence.enrollments.length === 0 ? (
              <EmptyState
                icon={<Icon name="workflow" size={18} />}
                title={LEADS_MSG.noEnrollmentsTitle}
                description={LEADS_MSG.noEnrollmentsDesc}
              />
            ) : (
              <ul className="space-y-2">
                {data.cadence.enrollments.map((e) => (
                  <li key={e.enrollmentId} className="flex items-center gap-3 p-3 rounded-lg border hairline">
                    <div className="h-9 w-9 rounded-lg bg-accent-50 text-accent-600 grid place-items-center">
                      <Icon name="workflow" size={16} />
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="text-sm font-medium text-ink-900 truncate">{e.cadenceName}</div>
                      <div className="text-xs text-ink-500 tabular-nums">
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
            title={
              <span className="inline-flex items-center gap-1">Call activity
                <InfoHint title="Wrap-up codes" side="bottom">
                  A wrap-up code is the outcome an agent records after each call. "Unwrapped" calls have none yet — they leave reporting incomplete and can block the next dial.
                </InfoHint>
              </span>
            }
            subtitle={`${data.callActivity.totalCalls} call${data.callActivity.totalCalls === 1 ? "" : "s"} · ${data.callActivity.answeredCalls} answered`}
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
                title={LEADS_MSG.noCallsTitle}
                description={LEADS_MSG.troubleshootNoCallsDesc}
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
                        <span className="ml-2 text-xs text-ink-500 tabular-nums whitespace-nowrap">{timeAgo(c.initiatedAt)}</span>
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
                icon={<Icon name="workflow" size={18} />}
                title={LEADS_MSG.noMatchingRulesTitle}
                description={LEADS_MSG.noMatchingRulesDesc}
              />
            </div>
          ) : (
            <Table className="border-0 shadow-none rounded-none">
              <THead><TR>
                <TH>Rule</TH>
                <TH>
                  <span className="inline-flex items-center gap-1">Event
                    <InfoHint title="Trigger event" side="bottom">
                      The system event that makes this rule run — e.g. a lead being created, a stage change, or a call ending.
                    </InfoHint>
                  </span>
                </TH>
                <TH>Status</TH>
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
                    <TD className="text-xs text-ink-600 tabular-nums whitespace-nowrap">{new Date(e.startedAt).toLocaleString()}</TD>
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
        <div className="text-lg font-semibold mt-1 tabular-nums">
          {label === "Stage"
            ? <Badge tone={stageTone[value] ?? "neutral"} variant="solid" dot>{stageLabel(value)}</Badge>
            : value}
        </div>
      </div>
    );
  }
}

function DetailBlock({ label, text }: { label: string; text: string }) {
  return (
    <div>
      <div className="text-[10px] font-semibold uppercase tracking-[0.12em] text-ink-400">{label}</div>
      <div className="text-sm text-ink-700 mt-0.5 leading-relaxed">{text}</div>
    </div>
  );
}

function Row({ label, value }: { label: React.ReactNode; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1.5">
      <div className="text-xs font-medium text-ink-500 uppercase tracking-wider">{label}</div>
      <div>{value}</div>
    </div>
  );
}
