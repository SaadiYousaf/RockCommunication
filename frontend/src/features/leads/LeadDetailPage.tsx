import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  useAiRecommendationsQuery,
  useCheckComplianceMutation,
  useDialLeadMutation,
  useLeadDetailQuery,
  useLeadTimelineQuery,
  useListScriptsQuery,
  useListVoicemailsQuery,
  useDropVoicemailMutation,
  useScheduleCallbackMutation,
  useSendQuickSmsMutation,
  useSetLeadDispositionMutation,
  useTransitionLeadMutation,
  useUpdateLeadNotesMutation,
  useVerifyJornayaMutation,
} from "../../shared/api/baseApi";
import type { LeadDisposition, WorkflowStage } from "../../shared/api/types";
import { Can, Perm, usePermission } from "../../shared/auth/permissions";
import { Avatar, Badge, type BadgeTone, Button, Card, CardBody, EmptyState, Icon, InfoHint, Select, Skeleton, useSecureEntry, useToast } from "../../shared/ui";
import { getErrorDetail } from "../../shared/api/apiError";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  ALLOWED_TRANSITIONS, TERMINAL_STAGES, STAGE_DESCRIPTIONS, DISPOSITION_DESCRIPTIONS, WORKFLOW_STAGES,
} from "../../shared/constants/leadStage";
import { formatPhone } from "../../shared/lib/format";
import { LEADS_MSG } from "./messages";

const DISPOSITIONS: LeadDisposition[] = ["None","Interested","NotInterested","CallBack","DoNotCall","Sold","NotQualified","Voicemail","NoAnswer","WrongNumber"];
// The linear pipeline shown in the stepper (off-track stages Followup/Winback/Lost sit outside it).
const PIPELINE: WorkflowStage[] = ["New","Fronted","Verified","JrClosed","Closed","Validated","Funded"];

export function LeadDetailPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { data: lead, refetch: refetchLead, isLoading: leadLoading, isError: leadError } = useLeadDetailQuery(id);
  const { data: timeline } = useLeadTimelineQuery(id);
  const { data: scripts } = useListScriptsQuery({ stage: lead?.stage });
  const { data: recs } = useAiRecommendationsQuery(id);
  const { data: voicemails } = useListVoicemailsQuery();

  const [transition] = useTransitionLeadMutation();
  const [setDisposition] = useSetLeadDispositionMutation();
  const [verifyJornaya, { isLoading: verifying }] = useVerifyJornayaMutation();
  const [dial, { isLoading: dialing }] = useDialLeadMutation();
  const [checkCompliance, { isLoading: checkingCompliance }] = useCheckComplianceMutation();
  const [updateNotes] = useUpdateLeadNotesMutation();
  const { handlers: secureNotes } = useSecureEntry();
  const [sendSms, { isLoading: sendingSms }] = useSendQuickSmsMutation();
  const [scheduleCallback, { isLoading: schedulingCallback }] = useScheduleCallbackMutation();
  const [dropVm] = useDropVoicemailMutation();
  const canEditNotes = usePermission(Perm.LeadsWrite);
  const toast = useToast();
  const confirm = useConfirm();

  const [compliance, setCompliance] = useState<{ allowed: boolean; blockReason: string | null; warnings: string[] } | null>(null);
  const [notes, setNotes] = useState("");
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [smsBody, setSmsBody] = useState("");
  const [showSms, setShowSms] = useState(false);
  const [showCallback, setShowCallback] = useState(false);
  const [callbackAt, setCallbackAt] = useState("");
  const [callbackReason, setCallbackReason] = useState("");

  useEffect(() => {
    if (lead?.notes !== undefined) setNotes(lead.notes ?? "");
  }, [lead?.id]);

  if (leadLoading) {
    return (
      <div className="space-y-5">
        <Skeleton className="h-40" />
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
          <Skeleton className="h-64 lg:col-span-2" />
          <Skeleton className="h-64" />
        </div>
      </div>
    );
  }
  if (leadError || !lead) {
    // e.g. a non-existent id (lead IDs are GUIDs) or one outside your access — not an endless spinner.
    return (
      <Card><CardBody>
        <EmptyState
          icon={<Icon name="search" size={20} />}
          title={LEADS_MSG.leadNotFoundTitle}
          description={LEADS_MSG.leadNotFoundDesc}
          action={<Button variant="outline" onClick={() => navigate("/leads")} leftIcon={<Icon name="arrowRight" size={14} className="rotate-180" />}>Back to Leads</Button>}
        />
      </CardBody></Card>
    );
  }

  async function safeDial() {
    setCompliance(null);
    if (!lead) return;
    // Fail CLOSED: if the compliance/DNC check can't be reached, block the call rather than
    // dialing blind (a DNC/TCPA violation risk).
    try {
      const result = await checkCompliance({ phone: lead.phoneNumber, state: lead.state ?? undefined }).unwrap();
      setCompliance(result);
      if (!result.allowed) { toast.error(LEADS_MSG.callBlockedTitle, result.blockReason ?? LEADS_MSG.complianceCheckFailed); return; }
    } catch {
      toast.error(LEADS_MSG.cantVerifyComplianceTitle, LEADS_MSG.cantVerifyComplianceDesc);
      return;
    }
    try {
      await dial({ leadId: id }).unwrap();
    } catch (err: unknown) {
      toast.error(LEADS_MSG.placeCallFailedTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
    }
  }

  async function doTransition(toStage: WorkflowStage) {
    if (TERMINAL_STAGES.includes(toStage)) {
      const ok = await confirm({ title: LEADS_MSG.moveConfirmTitle(toStage), description: LEADS_MSG.moveConfirmDesc, danger: true, confirmLabel: LEADS_MSG.moveConfirmLabel(toStage) });
      if (!ok) return;
    }
    try {
      await transition({ id, toStage, disposition: lead!.disposition as LeadDisposition }).unwrap();
      toast.success(LEADS_MSG.stageUpdatedTitle, LEADS_MSG.movedTo(toStage));
      refetchLead();
    } catch (err: unknown) {
      toast.error(LEADS_MSG.moveStageFailedTitle, getErrorDetail(err) ?? LEADS_MSG.transitionNotAllowed);
    }
  }

  async function doVerifyJornaya() {
    try {
      await verifyJornaya(id).unwrap();
      toast.success(LEADS_MSG.jornayaVerifiedTitle, LEADS_MSG.jornayaVerifiedDesc);
      refetchLead();
    } catch (err: unknown) {
      toast.error(LEADS_MSG.verifyJornayaFailedTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
    }
  }

  async function doDisposition(disposition: LeadDisposition) {
    try {
      await setDisposition({ id, disposition }).unwrap();
      toast.success(LEADS_MSG.dispositionSetTitle, disposition);
      refetchLead();
    } catch (err: unknown) {
      toast.error(LEADS_MSG.setDispositionFailedTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
    }
  }

  async function saveNotes() {
    if (!canEditNotes || notes === (lead?.notes ?? "")) return;   // nothing to save / read-only
    try {
      await updateNotes({ id, notes }).unwrap();
      setSavedAt(new Date().toLocaleTimeString());
    } catch (err: unknown) {
      toast.error(LEADS_MSG.notesNotSavedTitle, getErrorDetail(err) ?? LEADS_MSG.notesNotSavedDesc);
    }
  }

  async function submitSms(e: React.FormEvent) {
    e.preventDefault();
    if (!smsBody.trim()) return;
    try {
      await sendSms({ leadId: id, body: smsBody }).unwrap();
      toast.success(LEADS_MSG.smsSentTitle);
      setSmsBody("");
      setShowSms(false);
    } catch (err) {
      toast.error(LEADS_MSG.smsNotSentTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
    }
  }

  async function submitCallback(e: React.FormEvent) {
    e.preventDefault();
    if (!callbackAt) return;
    try {
      await scheduleCallback({ leadId: id, scheduledFor: new Date(callbackAt).toISOString(), reason: callbackReason || undefined }).unwrap();
      toast.success(LEADS_MSG.callbackScheduledTitle);
      setCallbackAt(""); setCallbackReason(""); setShowCallback(false);
      refetchLead();
    } catch (err) {
      toast.error(LEADS_MSG.scheduleCallbackFailedTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
    }
  }

  // Colour the score by band so it reads at a glance without leaving the simple palette.
  const scoreColor = lead.score >= 60 ? "text-emerald-700" : lead.score >= 30 ? "text-brand-700" : "text-amber-600";

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="surface p-5">
        <div className="flex items-start gap-4">
          <Avatar name={lead.fullName} size={48} />
          <div className="flex-1 min-w-0">
            <div className="section-title mb-1">Lead</div>
            <div className="flex items-center gap-2 flex-wrap">
              <h1 className="text-2xl font-semibold text-balance">{lead.fullName}</h1>
              <StageBadge stage={lead.stage} />
              <DispositionBadge disposition={lead.disposition} />
              {lead.jornayaVerified && (
                <span className="inline-flex items-center gap-1">
                  <Badge tone="success"><Icon name="success" size={12} />Jornaya verified</Badge>
                  <InfoHint title="Jornaya verified" side="bottom">
                    The lead's Jornaya LeadiD token was validated — independent proof of when and where the prospect submitted their info and consented. Supports TCPA compliance.
                  </InfoHint>
                </span>
              )}
              {lead.consentCaptured && (
                <span className="inline-flex items-center gap-1">
                  <Badge tone="success"><Icon name="success" size={12} />TCPA consent</Badge>
                  <InfoHint title="TCPA consent" side="bottom">
                    The prospect gave prior express written consent to be contacted (TCPA). Required before auto-dialing or texting; without it, a call can be blocked.
                  </InfoHint>
                </span>
              )}
            </div>
            <div className="text-sm text-ink-600 flex items-center gap-3 flex-wrap mt-2">
              <span className="font-mono tabular-nums">{formatPhone(lead.phoneNumber)}</span>
              {lead.email && <a href={`mailto:${lead.email}`} className="text-brand-700 hover:underline">{lead.email}</a>}
              {lead.state && <span>{[lead.city, lead.state, lead.postalCode].filter(Boolean).join(", ")}</span>}
              {lead.age && <span>{lead.age} y/o</span>}
              {lead.assignedUserName && <span>· Assigned to <strong>{lead.assignedUserName}</strong></span>}
            </div>
          </div>
          <div className="flex flex-col items-end">
            <div className={`text-3xl font-bold tabular-nums ${scoreColor}`}>{lead.score}</div>
            <div className="text-xs text-ink-500 uppercase tracking-wider flex items-center gap-1">
              Lead score
              <InfoHint title="Lead score (0–100)" side="left">
                A rules-based heuristic of how likely this lead is to convert — higher is better. Built from the weighted factors in the AI insights breakdown (recency, engagement, data completeness, disposition…). It's a heuristic, not an ML prediction.
              </InfoHint>
            </div>
          </div>
        </div>

        {/* Pipeline progress */}
        <Stepper current={lead.stage as WorkflowStage} />

        <div className="flex flex-wrap gap-2 mt-4">
          <Button onClick={safeDial} loading={checkingCompliance || dialing} leftIcon={<Icon name="phone" size={16} />}>Dial</Button>
          <Button variant="secondary" onClick={() => setShowSms(s => !s)} leftIcon={<Icon name="chat" size={16} />}>SMS</Button>
          <Button variant="secondary" onClick={() => setShowCallback(s => !s)} leftIcon={<Icon name="calendar" size={16} />}>Schedule callback</Button>
          {voicemails && voicemails.length > 0 && (
            <Select
              aria-label="Drop voicemail"
              className="h-10 w-auto text-sm"
              defaultValue=""
              onChange={async (e) => {
                if (!e.target.value) return;
                const sel = e.target;
                try {
                  await dropVm({ leadId: id, voicemailAssetId: sel.value }).unwrap();
                  toast.success(LEADS_MSG.voicemailDroppedTitle);
                } catch (err) {
                  toast.error(LEADS_MSG.dropVoicemailFailedTitle, getErrorDetail(err) ?? LEADS_MSG.retry);
                }
                sel.value = "";
              }}>
              <option value="">Drop voicemail…</option>
              {voicemails.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
            </Select>
          )}
          {lead.jornayaVerified ? (
            <Button
              variant="secondary"
              disabled
              leftIcon={<Icon name="success" size={16} />}
              title={lead.jornayaVerifiedAt ? `Verified ${new Date(lead.jornayaVerifiedAt).toLocaleString()}` : undefined}
            >
              Verified{lead.jornayaVerifiedBy ? ` by ${lead.jornayaVerifiedBy}` : ""}
            </Button>
          ) : (
            <Button variant="success" onClick={doVerifyJornaya} loading={verifying} leftIcon={<Icon name="shield" size={16} />}>Verify Jornaya</Button>
          )}
          <div className="flex-1" />
          <Can permission={Perm.LeadsTransition}>
            <span className="inline-flex items-center gap-1 text-xs text-ink-500 mr-1 self-center">
              Move to
              <InfoHint title="Pipeline stages" side="bottom">
                <ul className="space-y-0.5">
                  {WORKFLOW_STAGES.map((s) => (
                    <li key={s}><strong className="text-ink-800">{s}</strong> — {STAGE_DESCRIPTIONS[s]}</li>
                  ))}
                </ul>
              </InfoHint>
            </span>
            {(ALLOWED_TRANSITIONS[lead.stage as WorkflowStage] ?? []).map(s => (
              <button key={s} onClick={() => doTransition(s)}
                className={`px-3 py-2 rounded-lg text-xs border transition-colors ${TERMINAL_STAGES.includes(s)
                  ? "bg-white border-rose-300 text-rose-700 hover:bg-rose-50"
                  : "bg-white border-ink-300 hover:bg-ink-50 hover:border-ink-400"}`}>
                → {s}
              </button>
            ))}
            {(ALLOWED_TRANSITIONS[lead.stage as WorkflowStage] ?? []).length === 0 && (
              <span className="text-xs text-ink-400 self-center">No further moves from {lead.stage}</span>
            )}
          </Can>
        </div>

        {showSms && (
          <form onSubmit={submitSms} className="mt-3 flex gap-2 surface-muted p-2">
            <input className="input-base flex-1" placeholder="Quick SMS…"
              value={smsBody} onChange={e => setSmsBody(e.target.value)} autoFocus />
            <Button type="submit" size="sm" loading={sendingSms} leftIcon={<Icon name="send" size={14} />}>Send</Button>
          </form>
        )}
        {showCallback && (
          <form onSubmit={submitCallback} className="mt-3 flex flex-wrap gap-2 surface-muted p-2">
            <input type="datetime-local" className="input-base w-auto" value={callbackAt} onChange={e => setCallbackAt(e.target.value)} required />
            <input className="input-base flex-1 min-w-[8rem]" placeholder="Reason (optional)" value={callbackReason} onChange={e => setCallbackReason(e.target.value)} />
            <Button type="submit" size="sm" loading={schedulingCallback} leftIcon={<Icon name="calendar" size={14} />}>Schedule</Button>
          </form>
        )}

        {compliance && !compliance.allowed && (
          <div className="mt-3 bg-rose-50 border border-rose-200 text-rose-800 rounded p-3 text-sm">
            <strong>Call blocked:</strong> {compliance.blockReason}
          </div>
        )}
      </div>

      {/* Two-column body */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        {/* Left 2 columns */}
        <div className="lg:col-span-2 space-y-5">
          {/* Disposition picker */}
          <Can permission={Perm.LeadsTransition}>
            <div className="surface p-5">
              <div className="text-xs uppercase tracking-wider text-ink-500 mb-2 flex items-center gap-1">
                Set disposition
                <InfoHint title="Dispositions" side="right">
                  The outcome of the last contact attempt (doesn't change the pipeline stage).
                  <ul className="mt-1 space-y-0.5">
                    {Object.entries(DISPOSITION_DESCRIPTIONS).map(([d, desc]) => (
                      <li key={d}><strong className="text-ink-800">{d}</strong> — {desc}</li>
                    ))}
                  </ul>
                </InfoHint>
              </div>
              <div className="flex flex-wrap gap-1">
                {DISPOSITIONS.map(d => (
                  <button key={d}
                    onClick={() => doDisposition(d)}
                    className={`text-xs px-2.5 py-1 rounded-lg transition-colors ${d === lead.disposition ? "bg-brand-700 text-white shadow-sm" : "bg-ink-100 hover:bg-ink-200 text-ink-700"}`}>
                    {d}
                  </button>
                ))}
              </div>
            </div>
          </Can>

          {/* Notes */}
          <div className="surface p-5">
            <div className="flex items-center justify-between mb-2">
              <div className="text-sm font-semibold inline-flex items-center gap-1.5"><Icon name="edit" size={14} className="text-ink-400" /> Notes</div>
              {savedAt && <span className="text-xs text-emerald-700 inline-flex items-center gap-1"><Icon name="check" size={12} /> Saved {savedAt}</span>}
            </div>
            <textarea
              className="w-full border border-ink-200 rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-brand-400 focus:border-brand-300 transition-shadow"
              rows={5} placeholder={canEditNotes ? "Type call notes here…" : LEADS_MSG.notesReadOnly}
              value={notes} onChange={e => setNotes(e.target.value)}
              onBlur={saveNotes}
              readOnly={!canEditNotes}
              title={!canEditNotes ? LEADS_MSG.notesNoPermission : undefined}
              {...secureNotes}
            />
            <div className="text-xs text-ink-500 mt-1">Notes auto-save when you click out.</div>
          </div>

          {/* Script */}
          {scripts && scripts.length > 0 && (
            <div className="bg-amber-50 border border-amber-200 rounded-lg p-4">
              <div className="text-xs uppercase tracking-wider text-amber-800 mb-1">Script — {lead.stage}</div>
              <div className="font-medium text-amber-900 mb-2">{scripts[0].name}</div>
              <pre className="whitespace-pre-wrap text-sm text-ink-800">{scripts[0].body}</pre>
            </div>
          )}

          {/* Recent calls */}
          <div className="surface p-5">
            <div className="flex items-center justify-between mb-3">
              <div className="text-sm font-semibold inline-flex items-center gap-1.5"><Icon name="phone" size={14} className="text-ink-400" /> Recent calls <span className="text-ink-400 font-normal">({lead.callCount})</span>
                <InfoHint title="Call log" side="right">
                  Each entry shows the call's direction (in/out), its outcome status, an optional wrap-up code the agent logged at the end of the call, and the talk time in seconds.
                </InfoHint>
              </div>
            </div>
            {lead.recentCalls.length === 0 ? (
              <EmptyState compact icon={<Icon name="phone" size={18} />} title={LEADS_MSG.noCallsTitle} description={LEADS_MSG.noCallsDesc} />
            ) : (
              <div className="space-y-2">
                {lead.recentCalls.map(c => (
                  <div key={c.id} className="flex items-center gap-3 py-2 border-b last:border-0 border-ink-100 rounded-md px-1 -mx-1 hover:bg-ink-50/60 transition-colors">
                    <Icon name={c.direction === "Inbound" ? "phoneIn" : "phoneOut"} size={15}
                      className={c.direction === "Inbound" ? "text-emerald-600" : "text-brand-600"} />
                    <div className="flex-1 text-sm">
                      <div className="font-medium">
                        {c.direction} · {c.status}
                        {c.wrapUpCode && <span className="ml-2 text-xs bg-ink-100 px-1.5 py-0.5 rounded">{c.wrapUpCode}</span>}
                      </div>
                      <div className="text-xs text-ink-500">
                        <span className="tabular-nums whitespace-nowrap">{new Date(c.initiatedAt).toLocaleString()}</span>
                        {c.answeredAt && c.endedAt && <> · <span className="tabular-nums">{Math.round((new Date(c.endedAt).getTime() - new Date(c.answeredAt).getTime()) / 1000)}s</span></>}
                      </div>
                      {c.notes && <div className="text-xs text-ink-700 mt-0.5">{c.notes}</div>}
                    </div>
                    {c.recordingUrl && <audio controls src={c.recordingUrl} className="h-7 w-48" />}
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Timeline */}
          <div className="surface p-5">
            <div className="text-sm font-semibold mb-2 inline-flex items-center gap-1.5"><Icon name="activity" size={14} className="text-ink-400" /> Activity timeline</div>
            <ul className="space-y-1">
              {timeline?.entries.map((e, i) => (
                <li key={i} className="text-xs border-l-2 border-ink-200 pl-3 py-1 hover:border-brand-300 transition-colors">
                  <span className="bg-ink-100 px-1.5 py-0.5 rounded text-[10px] uppercase mr-2 tracking-wide">{e.type}</span>
                  <span className="text-ink-500 tabular-nums">{new Date(e.at).toLocaleString()}</span>
                  <span className="ml-2 text-ink-700">{e.description}</span>
                </li>
              ))}
            </ul>
            {(!timeline || timeline.entries.length === 0) && (
              <EmptyState compact icon={<Icon name="activity" size={18} />} title={LEADS_MSG.noActivityTitle} description={LEADS_MSG.noActivityDesc} />
            )}
          </div>
        </div>

        {/* Right column */}
        <div className="space-y-5">
          {/* AI insights */}
          <div className="surface p-5">
            <div className="text-sm font-semibold mb-2 flex items-center gap-1.5">
              <Icon name="sparkles" size={14} className="text-ink-400" /> AI insights
              <InfoHint title="How the score is built" side="left">
                Each line below is a scoring rule that added (+) or subtracted (–) points to reach the total. Green raises the score, red lowers it — factors include recency, engagement, data completeness and disposition.
              </InfoHint>
            </div>
            <div className="flex items-center gap-3 mb-3">
              <div className={`text-3xl font-bold tabular-nums ${scoreColor}`}>{lead.score}</div>
              <div className="text-xs text-ink-600">Heuristic score</div>
            </div>
            <div className="space-y-1">
              {lead.scoreBreakdown.map((b, i) => (
                <div key={i} className="text-xs flex items-center justify-between">
                  <span className="text-ink-600">{b.note ?? b.rule}</span>
                  <span className={`font-mono tabular-nums ${b.points >= 0 ? "text-emerald-700" : "text-rose-700"}`}>
                    {b.points >= 0 ? "+" : ""}{b.points}
                  </span>
                </div>
              ))}
              {lead.scoreBreakdown.length === 0 && <div className="text-xs text-ink-400">No factors yet.</div>}
            </div>
          </div>

          {/* Recommendations */}
          {recs && recs.items.length > 0 && (
            <div className="surface p-5">
              <div className="text-sm font-semibold mb-2 inline-flex items-center gap-1.5"><Icon name="target" size={14} className="text-ink-400" /> Recommended next steps
                <InfoHint title="Recommended next steps" side="left">
                  AI-suggested actions for this lead. The % on each is the model's confidence — higher percentages are stronger suggestions.
                </InfoHint>
              </div>
              <ul className="space-y-2">
                {recs.items.map((r, i) => (
                  <li key={i} className="text-sm flex items-start gap-2">
                    <span className="text-xs bg-emerald-100 text-emerald-800 rounded px-1.5 py-0.5 font-medium mt-0.5 shrink-0 tabular-nums">
                      {Math.round(r.confidence * 100)}%
                    </span>
                    <div>
                      <div className="font-medium">{r.action}</div>
                      <div className="text-xs text-ink-600">{r.reason}</div>
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Sale info */}
          {lead.sale && (
            <div className="bg-emerald-50 border border-emerald-200 rounded-lg p-4">
              <div className="text-sm font-semibold mb-2 text-emerald-900 inline-flex items-center gap-1.5"><Icon name="briefcase" size={14} /> Sale</div>
              <div className="text-sm text-ink-700 space-y-1">
                <div>Carrier: <strong>{lead.sale.carrier}</strong></div>
                {lead.sale.policyNumber && <div>Policy: <span className="font-mono tabular-nums">{lead.sale.policyNumber}</span></div>}
                <div>Premium: <strong className="tabular-nums">${lead.sale.monthlyPremium}/mo</strong> · <span className="tabular-nums">${lead.sale.annualPremium}/yr</span></div>
                <div className="text-xs text-ink-500">
                  Sold <span className="tabular-nums whitespace-nowrap">{new Date(lead.sale.soldAt).toLocaleDateString()}</span>
                  {lead.sale.validatedAt && ` · validated`}
                  {lead.sale.fundedAt && ` · funded`}
                  {lead.sale.isInternalSale && (
                    <span className="text-amber-700 inline-flex items-center gap-0.5"> · internal
                      <InfoHint title="Internal sale" side="top">
                        Flagged as an in-house / internal deal rather than a standard external customer sale. Tracked separately for commission and reporting.
                      </InfoHint>
                    </span>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* Open callbacks */}
          {lead.openCallbackCount > 0 && (
            <div className="surface p-5">
              <div className="text-sm font-semibold mb-2 inline-flex items-center gap-1.5"><Icon name="calendar" size={14} className="text-ink-400" /> Open callbacks <span className="text-ink-400 font-normal">({lead.openCallbackCount})</span></div>
              <ul className="space-y-2">
                {lead.callbacks.filter(cb => !cb.completed).map(cb => (
                  <li key={cb.id} className="text-sm">
                    <div className="font-medium tabular-nums whitespace-nowrap">{new Date(cb.scheduledFor).toLocaleString()}</div>
                    <div className="text-xs text-ink-600">
                      {cb.reason ?? "—"} · {cb.assignedUserName ?? "unassigned"}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Lead meta */}
          <div className="surface p-5 text-sm">
            <div className="font-semibold mb-2">Details</div>
            <dl className="text-xs">
              <Row label="Lead ID" mono value={
                <button
                  type="button"
                  onClick={() => { navigator.clipboard?.writeText(lead.id); toast.success(LEADS_MSG.leadIdCopiedTitle, LEADS_MSG.leadIdCopiedDesc); }}
                  className="inline-flex items-center gap-1 hover:text-brand-700"
                  title="Copy lead ID"
                >
                  {lead.id}
                  <Icon name="copy" size={11} />
                </button>
              } />
              <Row
                label={
                  <span className="inline-flex items-center gap-1">Source
                    <InfoHint title="Lead source" side="left">
                      Where this lead came from — the vendor, campaign, or channel that generated it. Used for cost-per-lead and conversion reporting.
                    </InfoHint>
                  </span>
                }
                value={lead.source}
              />
              <Row label="Created" value={new Date(lead.createdAt).toLocaleString()} />
              {lead.updatedAt && <Row label="Updated" value={new Date(lead.updatedAt).toLocaleString()} />}
              <Row label="DOB" value={lead.dateOfBirth ? new Date(lead.dateOfBirth).toLocaleDateString() : null} />
              <Row label="Address" value={lead.address} />
              <Row
                label={
                  <span className="inline-flex items-center gap-1">Skill required
                    <InfoHint title="Skill required" side="left">
                      A routing tag on the lead (e.g. a language or product-line skill). Calls are matched to agents who have this skill.
                    </InfoHint>
                  </span>
                }
                value={lead.requiredSkillCode}
              />
              <Row label="Jornaya token" value={lead.jornayaLeadId} mono />
            </dl>
          </div>
        </div>
      </div>
    </div>
  );
}

/** Horizontal progress stepper across the linear pipeline (New → … → Funded). */
function Stepper({ current }: { current: WorkflowStage }) {
  const offTrack = !PIPELINE.includes(current);
  const currentIdx = PIPELINE.indexOf(current);
  return (
    <div className="mt-4 flex items-center gap-1 overflow-x-auto pb-1">
      {PIPELINE.map((s, i) => {
        const done = !offTrack && i < currentIdx;
        const active = !offTrack && i === currentIdx;
        return (
          <div key={s} className="flex items-center gap-1 shrink-0">
            <span className={`flex items-center gap-1 px-2.5 py-1 rounded-full text-[11px] font-medium ${
              active ? "bg-brand-600 text-white"
                : done ? "bg-emerald-100 text-emerald-800"
                : "bg-ink-100 text-ink-500"}`}>
              {done && <Icon name="success" size={11} />}{s}
            </span>
            {i < PIPELINE.length - 1 && <span className={`w-4 h-px ${done ? "bg-emerald-300" : "bg-ink-200"}`} />}
          </div>
        );
      })}
      {offTrack && (
        <span className={`ml-2 shrink-0 text-[11px] font-medium px-2.5 py-1 rounded-full ${
          current === "Lost" ? "bg-rose-100 text-rose-800" : "bg-amber-100 text-amber-800"}`}>
          {current} · off the main pipeline
        </span>
      )}
    </div>
  );
}

function StageBadge({ stage }: { stage: string }) {
  const tone: BadgeTone = stage === "Funded" ? "success" : stage === "Lost" ? "danger"
    : stage === "Closed" || stage === "Validated" ? "info"
    : stage === "Followup" || stage === "Winback" ? "warning" : "default";
  return <Badge tone={tone}>{stage}</Badge>;
}

function DispositionBadge({ disposition }: { disposition: string }) {
  if (disposition === "None") return null;
  const tone: BadgeTone = disposition === "Sold" ? "success"
    : disposition === "DoNotCall" || disposition === "NotInterested" ? "danger"
    : disposition === "Interested" ? "info" : "default";
  return <Badge tone={tone}>{disposition}</Badge>;
}

function Row({ label, value, mono }: { label: ReactNode; value: ReactNode; mono?: boolean }) {
  if (!value) return null;
  return (
    <div className="flex items-baseline justify-between gap-3 py-1.5 border-b border-ink-100 last:border-0">
      <dt className="text-ink-500 shrink-0">{label}</dt>
      <dd className={`text-ink-800 text-right break-all tabular-nums ${mono ? "font-mono text-[11px]" : ""}`}>{value}</dd>
    </div>
  );
}

