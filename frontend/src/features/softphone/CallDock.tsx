import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  type ActiveCall,
  useActiveCallQuery, useAnswerCallMutation, useHangupCallMutation,
  useHoldCallMutation, useMuteCallMutation, useSendDtmfMutation,
  useSendQuickSmsMutation,
} from "../../shared/api/baseApi";
import { useAgentHub } from "../../shared/hooks/useAgentHub";

/**
 * Sticky bottom-right call dock. Shows the agent's current call with full controls.
 * Auto-screen-pops on inbound. Plays a synthetic ring tone.
 */
export function CallDock() {
  const navigate = useNavigate();
  const { data: initial, refetch } = useActiveCallQuery(undefined, { pollingInterval: 30000 });
  const [call, setCall] = useState<ActiveCall | null>(initial ?? null);
  const [answer] = useAnswerCallMutation();
  const [hangup] = useHangupCallMutation();
  const [hold] = useHoldCallMutation();
  const [mute] = useMuteCallMutation();
  const [dtmf] = useSendDtmfMutation();
  const [sendSms] = useSendQuickSmsMutation();
  const [smsBody, setSmsBody] = useState("");
  const [showDialpad, setShowDialpad] = useState(false);
  const [toast, setToast] = useState<{ kind: string; text: string } | null>(null);

  useEffect(() => { setCall(initial ?? null); }, [initial]);

  useAgentHub((event, payload) => {
    switch (event) {
      case "incoming-call":
        playRing();
        navigate(`/leads/${payload.leadId}`);
        refetch();
        setToast({ kind: "info", text: `Incoming call from ${payload.phone}` });
        setTimeout(() => setToast(null), 4000);
        break;
      case "call-ringing":
      case "call-answered":
      case "call-state-changed":
        setCall(payload as ActiveCall);
        break;
      case "call-ended":
        stopRing();
        setCall(null);
        refetch();
        break;
      case "screen-pop":
        if (payload.leadId) navigate(`/leads/${payload.leadId}`);
        break;
      case "toast":
        setToast({ kind: payload.kind ?? "info", text: payload.text ?? "" });
        setTimeout(() => setToast(null), 3500);
        break;
    }
  });

  if (!call) {
    return toast ? <Toast kind={toast.kind} text={toast.text} /> : null;
  }

  const isInbound = call.direction === "Inbound";
  const elapsed = call.answeredAt
    ? Math.floor((Date.now() - new Date(call.answeredAt).getTime()) / 1000)
    : Math.floor((Date.now() - new Date(call.initiatedAt).getTime()) / 1000);

  return (
    <>
      {toast && <Toast kind={toast.kind} text={toast.text} />}
      <div className="fixed bottom-4 right-4 z-50 w-96 bg-slate-900 text-white rounded-xl shadow-2xl border border-slate-700 overflow-hidden">
        <div className={`px-4 py-3 ${isInbound ? "bg-emerald-700" : "bg-brand-700"} flex items-center gap-3`}>
          <div className="h-8 w-8 rounded-full bg-white/20 grid place-items-center text-sm font-bold">
            {call.leadName.split(" ").map(n => n[0]).slice(0, 2).join("")}
          </div>
          <div className="flex-1 min-w-0">
            <div className="font-semibold truncate">{call.leadName}</div>
            <div className="text-xs opacity-90 font-mono">{call.phone}</div>
          </div>
          <button className="text-xs opacity-70 hover:opacity-100" onClick={() => navigate(`/leads/${call.leadId}`)}>
            Open lead
          </button>
        </div>

        <div className="px-4 py-3">
          <div className="flex items-center justify-between mb-3">
            <span className={`text-xs px-2 py-0.5 rounded-full ${statusClass(call.status)}`}>
              {isInbound ? "🔔 " : ""}{call.status}
              {call.isHeld && " · on hold"}
              {call.isMuted && " · muted"}
            </span>
            <span className="text-sm font-mono">{formatTime(elapsed)}</span>
          </div>

          {call.status === "ringing" && isInbound && (
            <button
              onClick={() => answer(call.id)}
              className="w-full bg-emerald-600 hover:bg-emerald-500 rounded py-2 mb-2 font-semibold">
              📞 Answer
            </button>
          )}

          <div className="grid grid-cols-3 gap-2 mb-2">
            <DockBtn active={call.isMuted} onClick={() => mute({ id: call.id, mute: !call.isMuted })}>
              {call.isMuted ? "🔇 Unmute" : "🎤 Mute"}
            </DockBtn>
            <DockBtn active={call.isHeld} onClick={() => hold({ id: call.id, hold: !call.isHeld })}>
              {call.isHeld ? "▶ Resume" : "⏸ Hold"}
            </DockBtn>
            <DockBtn onClick={() => setShowDialpad(s => !s)}>📞 Pad</DockBtn>
          </div>

          {showDialpad && (
            <div className="grid grid-cols-3 gap-1 mb-2">
              {["1","2","3","4","5","6","7","8","9","*","0","#"].map(d => (
                <button key={d}
                  className="bg-slate-800 hover:bg-slate-700 rounded py-2 font-mono"
                  onClick={() => dtmf({ id: call.id, digits: d })}>{d}</button>
              ))}
            </div>
          )}

          <form className="flex gap-1 mb-2" onSubmit={async (e) => {
            e.preventDefault();
            if (!smsBody.trim()) return;
            await sendSms({ leadId: call.leadId, body: smsBody }).unwrap().catch(() => {});
            setSmsBody("");
            setToast({ kind: "ok", text: "SMS sent" });
            setTimeout(() => setToast(null), 2000);
          }}>
            <input className="flex-1 bg-slate-800 border border-slate-700 rounded px-2 py-1 text-sm"
              placeholder="Quick SMS to lead..." value={smsBody} onChange={(e) => setSmsBody(e.target.value)} />
            <button className="bg-slate-700 rounded px-3 text-sm hover:bg-slate-600">Send</button>
          </form>

          <button
            onClick={() => hangup(call.id)}
            className="w-full bg-rose-600 hover:bg-rose-500 rounded py-2 font-semibold">
            ☎ Hang up
          </button>
        </div>
      </div>
    </>
  );
}

function DockBtn({ children, onClick, active }: { children: ReactNode; onClick: () => void; active?: boolean }) {
  return (
    <button
      onClick={onClick}
      className={`rounded py-2 text-sm transition ${active ? "bg-amber-500 text-slate-900" : "bg-slate-800 hover:bg-slate-700"}`}>
      {children}
    </button>
  );
}

function Toast({ kind, text }: { kind: string; text: string }) {
  const cls = kind === "ok" ? "bg-emerald-600" : kind === "warn" ? "bg-amber-500 text-slate-900" : kind === "err" ? "bg-rose-600" : "bg-brand-600";
  return (
    <div className={`fixed bottom-4 right-4 z-50 ${cls} text-white text-sm rounded shadow-lg px-4 py-2`}>
      {text}
    </div>
  );
}

function statusClass(status: string) {
  switch (status) {
    case "ringing": return "bg-amber-500/30 text-amber-200";
    case "answered": return "bg-emerald-500/30 text-emerald-200";
    case "ended": return "bg-slate-500/30 text-slate-200";
    default: return "bg-slate-500/30 text-slate-200";
  }
}

function formatTime(s: number) {
  const m = Math.floor(s / 60), r = s % 60;
  return `${m}:${r.toString().padStart(2, "0")}`;
}

// Synthetic ring (Web Audio API) so the agent UX is real even with stub telephony.
let ringCtx: AudioContext | null = null;
let ringTimer: number | null = null;
function playRing() {
  try {
    ringCtx?.close();
    ringCtx = new (window.AudioContext || window.webkitAudioContext!)();
    const beep = () => {
      if (!ringCtx) return;
      const osc = ringCtx.createOscillator();
      const gain = ringCtx.createGain();
      osc.frequency.value = 440;
      gain.gain.setValueAtTime(0.001, ringCtx.currentTime);
      gain.gain.linearRampToValueAtTime(0.2, ringCtx.currentTime + 0.05);
      gain.gain.linearRampToValueAtTime(0.001, ringCtx.currentTime + 0.4);
      osc.connect(gain).connect(ringCtx.destination);
      osc.start();
      osc.stop(ringCtx.currentTime + 0.4);
    };
    beep();
    ringTimer = window.setInterval(beep, 1000);
  } catch { /* audio unavailable */ }
}
function stopRing() {
  if (ringTimer) { clearInterval(ringTimer); ringTimer = null; }
  ringCtx?.close().catch(() => {});
  ringCtx = null;
}
