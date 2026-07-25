import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
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
import { Icon, InfoHint, useSecureEntry, useToast } from "../../shared/ui";
import { getErrorDetail } from "../../shared/api/apiError";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  ALLOWED_TRANSITIONS, TERMINAL_STAGES, STAGE_DESCRIPTIONS, DISPOSITION_DESCRIPTIONS, WORKFLOW_STAGES,
} from "../../shared/constants/leadStage";

const DISPOSITIONS: LeadDisposition[] = ["None","Interested","NotInterested","CallBack","DoNotCall","Sold","NotQualified","Voicemail","NoAnswer","WrongNumber"];
// The linear pipeline shown in the stepper (off-track stages Followup/Winback/Lost sit outside it).
const PIPELINE: WorkflowStage[] = ["New","Fronted","Verified","JrClosed","Closed","Validated","Funded"];

export function LeadDetailPage() {
  const { id = "" } = useParams();
  const { data: lead, refetch: refetchLead } = useLeadDetailQuery(id);
  const { data: timeline } = useLeadTimelineQuery(id);
  const { data: scripts } = useListScriptsQuery({ stage: lead?.stage });
  const { data: recs } = useAiRecommendationsQuery(id);
  const { data: voicemails } = useListVoicemailsQuery();

  const [transition] = useTransitionLeadMutation();
  const [setDisposition] = useSetLeadDispositionMutation();
  const [verifyJornaya] = useVerifyJornayaMutation();
  const [dial] = useDialLeadMutation();
  const [checkCompliance] = useCheckComplianceMutation();
  const [updateNotes] = useUpdateLeadNotesMutation();
  const { handlers: secureNotes } = useSecureEntry();
  const [sendSms] = useSendQuickSmsMutation();
  const [scheduleCallback] = useScheduleCallbackMutation();
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

  if (!lead) {
    return <div className="text-slate-500">Loading lead…</div>;
  }

  async function safeDial() {
    setCompliance(null);
    if (!lead) return;
    // Fail CLOSED: if the compliance/DNC check can't be reached, block the call rather than
    // dialing blind (a DNC/TCPA violation risk).
    try {
      const result = await checkCompliance({ phone: lead.phoneNumber, state: lead.state ?? undefined }).unwrap();
      setCompliance(result);
      if (!result.allowed) { toast.error("Call blocked", result.blockReason ?? "Compliance check failed."); return; }
    } catch {
      toast.error("Can't verify compliance", "The DNC/TCPA check is unavailable — the call was not placed.");
      return;
    }
    try {
      await dial({ leadId: id }).unwrap();
    } catch (err: unknown) {
      toast.error("Couldn't place call", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function doTransition(toStage: WorkflowStage) {
    if (TERMINAL_STAGES.includes(toStage)) {
      const ok = await confirm({ title: `Move to ${toStage}?`, description: "This takes the lead off the active pipeline.", danger: true, confirmLabel: `Move to ${toStage}` });
      if (!ok) return;
    }
    try {
      await transition({ id, toStage, disposition: lead!.disposition as LeadDisposition }).unwrap();
      toast.success("Stage updated", `Moved to ${toStage}.`);
      refetchLead();
    } catch (err: unknown) {
      toast.error("Couldn't move stage", getErrorDetail(err) ?? "That transition isn't allowed.");
    }
  }

  async function doDisposition(disposition: LeadDisposition) {
    try {
      await setDisposition({ id, disposition }).unwrap();
      toast.success("Disposition set", disposition);
      refetchLead();
    } catch (err: unknown) {
      toast.error("Couldn't set disposition", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function saveNotes() {
    if (!canEditNotes || notes === (lead?.notes ?? "")) return;   // nothing to save / read-only
    try {
      await updateNotes({ id, notes }).unwrap();
      setSavedAt(new Date().toLocaleTimeString());
    } catch (err: unknown) {
      toast.error("Notes not saved", getErrorDetail(err) ?? "Your changes weren't saved — try again.");
    }
  }

  async function submitSms(e: React.FormEvent) {
    e.preventDefault();
    if (!smsBody.trim()) return;
    await sendSms({ leadId: id, body: smsBody }).unwrap().catch(() => {});
    setSmsBody("");
    setShowSms(false);
  }

  async function submitCallback(e: React.FormEvent) {
    e.preventDefault();
    if (!callbackAt) return;
    await scheduleCallback({ leadId: id, scheduledFor: new Date(callbackAt).toISOString(), reason: callbackReason || undefined }).unwrap().catch(() => {});
    setCallbackAt(""); setCallbackReason(""); setShowCallback(false);
    refetchLead();
  }

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
        <div className="flex items-start gap-4">
          <div className="h-12 w-12 rounded-full bg-gradient-to-br from-brand-500 to-indigo-600 grid place-items-center text-white font-bold text-lg">
            {(lead.firstName?.[0] ?? "?")}{(lead.lastName?.[0] ?? "")}
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <h1 className="text-2xl font-semibold">{lead.fullName}</h1>
              <StageBadge stage={lead.stage} />
              <DispositionBadge disposition={lead.disposition} />
              {lead.jornayaVerified && (
                <span className="inline-flex items-center gap-1">
                  <Badge tone="emerald"><Icon name="success" size={12} className="mr-1" />Jornaya verified</Badge>
                  <InfoHint title="Jornaya verified" side="bottom">
                    The lead's Jornaya LeadiD token was validated — independent proof of when and where the prospect submitted their info and consented. Supports TCPA compliance.
                  </InfoHint>
                </span>
              )}
              {lead.consentCaptured && (
                <span className="inline-flex items-center gap-1">
                  <Badge tone="emerald"><Icon name="success" size={12} className="mr-1" />TCPA consent</Badge>
                  <InfoHint title="TCPA consent" side="bottom">
                    The prospect gave prior express written consent to be contacted (TCPA). Required before auto-dialing or texting; without it, a call can be blocked.
                  </InfoHint>
                </span>
              )}
            </div>
            <div className="text-sm text-slate-600 flex items-center gap-3 flex-wrap mt-2">
              <span className="font-mono">{formatPhone(lead.phoneNumber)}</span>
              {lead.email && <a href={`mailto:${lead.email}`} className="text-brand-700 hover:underline">{lead.email}</a>}
              {lead.state && <span>{[lead.city, lead.state, lead.postalCode].filter(Boolean).join(", ")}</span>}
              {lead.age && <span>{lead.age} y/o</span>}
              {lead.assignedUserName && <span>· Assigned to <strong>{lead.assignedUserName}</strong></span>}
            </div>
          </div>
          <div className="flex flex-col items-end">
            <div className="text-3xl font-bold text-brand-700">{lead.score}</div>
            <div className="text-xs text-slate-500 uppercase tracking-wider flex items-center gap-1">
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
          <button onClick={safeDial}
            className="bg-brand-700 hover:bg-brand-800 text-white px-4 py-2 rounded text-sm font-medium flex items-center gap-2">
            📞 Dial
          </button>
          <button onClick={() => setShowSms(s => !s)}
            className="bg-slate-200 hover:bg-slate-300 px-4 py-2 rounded text-sm font-medium">
            💬 SMS
          </button>
          <button onClick={() => setShowCallback(s => !s)}
            className="bg-slate-200 hover:bg-slate-300 px-4 py-2 rounded text-sm font-medium">
            📅 Schedule callback
          </button>
          {voicemails && voicemails.length > 0 && (
            <select
              className="bg-slate-200 hover:bg-slate-300 px-3 py-2 rounded text-sm cursor-pointer"
              defaultValue=""
              onChange={async (e) => {
                if (!e.target.value) return;
                await dropVm({ leadId: id, voicemailAssetId: e.target.value }).unwrap().catch(() => {});
                e.target.value = "";
              }}>
              <option value="">📥 Drop voicemail…</option>
              {voicemails.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
            </select>
          )}
          <button onClick={async () => { await verifyJornaya(id); refetchLead(); }}
            className="bg-emerald-600 hover:bg-emerald-700 text-white px-4 py-2 rounded text-sm font-medium">
            🛡 Verify Jornaya
          </button>
          <div className="flex-1" />
          <Can permission={Perm.LeadsTransition}>
            <span className="inline-flex items-center gap-1 text-xs text-slate-500 mr-1 self-center">
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
                className={`px-3 py-2 rounded text-xs border ${TERMINAL_STAGES.includes(s)
                  ? "bg-white border-rose-300 text-rose-700 hover:bg-rose-50"
                  : "bg-white border-slate-300 hover:bg-slate-50"}`}>
                → {s}
              </button>
            ))}
            {(ALLOWED_TRANSITIONS[lead.stage as WorkflowStage] ?? []).length === 0 && (
              <span className="text-xs text-slate-400 self-center">No further moves from {lead.stage}</span>
            )}
          </Can>
        </div>

        {showSms && (
          <form onSubmit={submitSms} className="mt-3 flex gap-2 bg-slate-50 rounded p-2">
            <input className="flex-1 border rounded px-3 py-1.5 text-sm" placeholder="Quick SMS…"
              value={smsBody} onChange={e => setSmsBody(e.target.value)} autoFocus />
            <button className="bg-brand-700 text-white rounded px-4 text-sm">Send</button>
          </form>
        )}
        {showCallback && (
          <form onSubmit={submitCallback} className="mt-3 flex flex-wrap gap-2 bg-slate-50 rounded p-2">
            <input type="datetime-local" className="border rounded px-2 py-1.5 text-sm" value={callbackAt} onChange={e => setCallbackAt(e.target.value)} required />
            <input className="flex-1 border rounded px-3 py-1.5 text-sm" placeholder="Reason (optional)" value={callbackReason} onChange={e => setCallbackReason(e.target.value)} />
            <button className="bg-brand-700 text-white rounded px-4 text-sm">Schedule</button>
          </form>
        )}

        {compliance && !compliance.allowed && (
          <div className="mt-3 bg-rose-50 border border-rose-200 text-rose-800 rounded p-3 text-sm">
            <strong>Call blocked:</strong> {compliance.blockReason}
          </div>
        )}
      </div>

      {/* Two-column body */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Left 2 columns */}
        <div className="lg:col-span-2 space-y-4">
          {/* Disposition picker */}
          <Can permission={Perm.LeadsTransition}>
            <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
              <div className="text-xs uppercase tracking-wider text-slate-500 mb-2 flex items-center gap-1">
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
                    className={`text-xs px-2.5 py-1 rounded ${d === lead.disposition ? "bg-brand-700 text-white" : "bg-slate-100 hover:bg-slate-200"}`}>
                    {d}
                  </button>
                ))}
              </div>
            </div>
          </Can>

          {/* Notes */}
          <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
            <div className="flex items-center justify-between mb-2">
              <div className="text-sm font-semibold">Notes</div>
              {savedAt && <span className="text-xs text-emerald-700">Saved {savedAt}</span>}
            </div>
            <textarea
              className="w-full border border-slate-300 rounded px-3 py-2 text-sm font-mono"
              rows={5} placeholder="Type call notes here…"
              value={notes} onChange={e => setNotes(e.target.value)}
              onBlur={saveNotes}
              readOnly={!canEditNotes}
              {...secureNotes}
            />
            <div className="text-xs text-slate-500 mt-1">Notes auto-save when you click out.</div>
          </div>

          {/* Script */}
          {scripts && scripts.length > 0 && (
            <div className="bg-amber-50 border border-amber-200 rounded-lg p-4">
              <div className="text-xs uppercase tracking-wider text-amber-800 mb-1">Script — {lead.stage}</div>
              <div className="font-medium text-amber-900 mb-2">{scripts[0].name}</div>
              <pre className="whitespace-pre-wrap text-sm text-slate-800">{scripts[0].body}</pre>
            </div>
          )}

          {/* Recent calls */}
          <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
            <div className="flex items-center justify-between mb-3">
              <div className="text-sm font-semibold">Recent calls ({lead.callCount})</div>
            </div>
            {lead.recentCalls.length === 0 ? (
              <div className="text-sm text-slate-500">No calls yet.</div>
            ) : (
              <div className="space-y-2">
                {lead.recentCalls.map(c => (
                  <div key={c.id} className="flex items-center gap-3 py-2 border-b last:border-0 border-slate-100">
                    <div className={`w-2 h-2 rounded-full ${c.direction === "Inbound" ? "bg-emerald-500" : "bg-brand-500"}`} />
                    <div className="flex-1 text-sm">
                      <div className="font-medium">
                        {c.direction} · {c.status}
                        {c.wrapUpCode && <span className="ml-2 text-xs bg-slate-100 px-1.5 py-0.5 rounded">{c.wrapUpCode}</span>}
                      </div>
                      <div className="text-xs text-slate-500">
                        {new Date(c.initiatedAt).toLocaleString()}
                        {c.answeredAt && c.endedAt && <> · {Math.round((new Date(c.endedAt).getTime() - new Date(c.answeredAt).getTime()) / 1000)}s</>}
                      </div>
                      {c.notes && <div className="text-xs text-slate-700 mt-0.5">{c.notes}</div>}
                    </div>
                    {c.recordingUrl && <audio controls src={c.recordingUrl} className="h-7 w-48" />}
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Timeline */}
          <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
            <div className="text-sm font-semibold mb-2">Activity timeline</div>
            <ul className="space-y-1">
              {timeline?.entries.map((e, i) => (
                <li key={i} className="text-xs border-l-2 border-slate-200 pl-3 py-1">
                  <span className="bg-slate-100 px-1.5 py-0.5 rounded text-[10px] uppercase mr-2">{e.type}</span>
                  <span className="text-slate-500">{new Date(e.at).toLocaleString()}</span>
                  <span className="ml-2">{e.description}</span>
                </li>
              ))}
            </ul>
          </div>
        </div>

        {/* Right column */}
        <div className="space-y-4">
          {/* AI insights */}
          <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
            <div className="text-sm font-semibold mb-2 flex items-center gap-1">
              AI insights
              <InfoHint title="How the score is built" side="left">
                Each line below is a scoring rule that added (+) or subtracted (–) points to reach the total. Green raises the score, red lowers it — factors include recency, engagement, data completeness and disposition.
              </InfoHint>
            </div>
            <div className="flex items-center gap-3 mb-3">
              <div className="text-3xl font-bold text-brand-700">{lead.score}</div>
              <div className="text-xs text-slate-600">Heuristic score</div>
            </div>
            <div className="space-y-1">
              {lead.scoreBreakdown.map((b, i) => (
                <div key={i} className="text-xs flex items-center justify-between">
                  <span className="text-slate-600">{b.note ?? b.rule}</span>
                  <span className={`font-mono ${b.points >= 0 ? "text-emerald-700" : "text-rose-700"}`}>
                    {b.points >= 0 ? "+" : ""}{b.points}
                  </span>
                </div>
              ))}
              {lead.scoreBreakdown.length === 0 && <div className="text-xs text-slate-400">No factors yet.</div>}
            </div>
          </div>

          {/* Recommendations */}
          {recs && recs.items.length > 0 && (
            <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
              <div className="text-sm font-semibold mb-2">Recommended next steps</div>
              <ul className="space-y-2">
                {recs.items.map((r, i) => (
                  <li key={i} className="text-sm flex items-start gap-2">
                    <span className="text-xs bg-emerald-100 text-emerald-800 rounded px-1.5 py-0.5 font-medium mt-0.5 shrink-0">
                      {Math.round(r.confidence * 100)}%
                    </span>
                    <div>
                      <div className="font-medium">{r.action}</div>
                      <div className="text-xs text-slate-600">{r.reason}</div>
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Sale info */}
          {lead.sale && (
            <div className="bg-emerald-50 border border-emerald-200 rounded-lg p-4">
              <div className="text-sm font-semibold mb-2 text-emerald-900">Sale</div>
              <div className="text-sm text-slate-700 space-y-1">
                <div>Carrier: <strong>{lead.sale.carrier}</strong></div>
                {lead.sale.policyNumber && <div>Policy: <span className="font-mono">{lead.sale.policyNumber}</span></div>}
                <div>Premium: <strong>${lead.sale.monthlyPremium}/mo</strong> · ${lead.sale.annualPremium}/yr</div>
                <div className="text-xs text-slate-500">
                  Sold {new Date(lead.sale.soldAt).toLocaleDateString()}
                  {lead.sale.validatedAt && ` · validated`}
                  {lead.sale.fundedAt && ` · funded`}
                  {lead.sale.isInternalSale && " · ⚠ internal"}
                </div>
              </div>
            </div>
          )}

          {/* Open callbacks */}
          {lead.openCallbackCount > 0 && (
            <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4">
              <div className="text-sm font-semibold mb-2">Open callbacks ({lead.openCallbackCount})</div>
              <ul className="space-y-2">
                {lead.callbacks.filter(cb => !cb.completed).map(cb => (
                  <li key={cb.id} className="text-sm">
                    <div className="font-medium">{new Date(cb.scheduledFor).toLocaleString()}</div>
                    <div className="text-xs text-slate-600">
                      {cb.reason ?? "—"} · {cb.assignedUserName ?? "unassigned"}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Lead meta */}
          <div className="bg-white rounded-lg shadow-sm border border-slate-200 p-4 text-sm">
            <div className="font-semibold mb-2">Details</div>
            <dl className="space-y-1 text-xs">
              <Row label="Source" value={lead.source} />
              <Row label="Created" value={new Date(lead.createdAt).toLocaleString()} />
              {lead.updatedAt && <Row label="Updated" value={new Date(lead.updatedAt).toLocaleString()} />}
              <Row label="DOB" value={lead.dateOfBirth ? new Date(lead.dateOfBirth).toLocaleDateString() : null} />
              <Row label="Address" value={lead.address} />
              <Row label="Skill required" value={lead.requiredSkillCode} />
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
                : "bg-slate-100 text-slate-500"}`}>
              {done && <Icon name="success" size={11} />}{s}
            </span>
            {i < PIPELINE.length - 1 && <span className={`w-4 h-px ${done ? "bg-emerald-300" : "bg-slate-200"}`} />}
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
  const tone = stage === "Funded" ? "emerald" : stage === "Lost" ? "rose"
    : stage === "Closed" || stage === "Validated" ? "sky"
    : stage === "Followup" || stage === "Winback" ? "amber" : "slate";
  return <Badge tone={tone}>{stage}</Badge>;
}

function DispositionBadge({ disposition }: { disposition: string }) {
  if (disposition === "None") return null;
  const tone = disposition === "Sold" ? "emerald"
    : disposition === "DoNotCall" || disposition === "NotInterested" ? "rose"
    : disposition === "Interested" ? "sky" : "slate";
  return <Badge tone={tone}>{disposition}</Badge>;
}

function Badge({ children, tone = "slate" }: { children: ReactNode; tone?: string }) {
  const cls: Record<string, string> = {
    sky: "bg-brand-100 text-brand-800",
    emerald: "bg-emerald-100 text-emerald-800",
    amber: "bg-amber-100 text-amber-800",
    rose: "bg-rose-100 text-rose-800",
    slate: "bg-slate-100 text-slate-700",
  };
  return <span className={`text-xs px-2 py-0.5 rounded-full ${cls[tone]}`}>{children}</span>;
}

function Row({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  if (!value) return null;
  return (
    <div className="flex items-baseline gap-2">
      <dt className="text-slate-500 w-24 shrink-0">{label}</dt>
      <dd className={`flex-1 text-slate-800 break-all ${mono ? "font-mono" : ""}`}>{value}</dd>
    </div>
  );
}

function formatPhone(p: string) {
  const d = (p || "").replace(/\D/g, "");
  if (d.length === 10) return `(${d.slice(0,3)}) ${d.slice(3,6)}-${d.slice(6)}`;
  return p;
}
