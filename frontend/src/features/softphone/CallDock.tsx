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
import { Button, Icon, useToast } from "../../shared/ui";

/**
 * Sticky bottom-right call dock. Shows the agent's current call with full controls.
 * Auto-screen-pops on inbound. Plays a synthetic ring tone.
 */
export function CallDock() {
  const navigate = useNavigate();
  const toast = useToast();
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

  useEffect(() => { setCall(initial ?? null); }, [initial]);

  // Re-render every second while a call is live so the duration display ticks smoothly
  // (SignalR events / the 30s poll are too infrequent to drive a running clock).
  const [, setTick] = useState(0);
  useEffect(() => {
    if (!call) return;
    const id = setInterval(() => setTick((t) => t + 1), 1000);
    return () => clearInterval(id);
  }, [call]);

  // Stop the synthetic ring on unmount (forced sign-out / route change while still ringing),
  // otherwise the module-level ring interval + AudioContext keep beeping.
  useEffect(() => () => stopRing(), []);

  useAgentHub((event, payload) => {
    switch (event) {
      case "incoming-call":
        playRing();
        navigate(`/leads/${payload.leadId}`);
        refetch();
        toast.info("Incoming call", payload.phone);
        break;
      case "call-ringing":
      case "call-answered":
      case "call-state-changed": {
        // Stop the ring the moment the call leaves "ringing" (answered/connected) — previously it
        // beeped for the whole connected call, only stopping on call-ended.
        const next = payload as ActiveCall;
        if (next.status !== "ringing") stopRing();
        setCall(next);
        break;
      }
      case "call-ended":
        stopRing();
        setCall(null);
        refetch();
        break;
      case "screen-pop":
        if (payload.leadId) navigate(`/leads/${payload.leadId}`);
        break;
      case "toast": {
        // Server toast kinds: ok → success, err → error, everything else (warn/info) → info.
        const text = payload.text ?? "";
        if (payload.kind === "ok") toast.success(text);
        else if (payload.kind === "err") toast.error(text);
        else toast.info(text);
        break;
      }
    }
  });

  if (!call) return null;

  const isInbound = call.direction === "Inbound";
  const elapsed = call.answeredAt
    ? Math.floor((Date.now() - new Date(call.answeredAt).getTime()) / 1000)
    : Math.floor((Date.now() - new Date(call.initiatedAt).getTime()) / 1000);

  return (
    <div className="fixed bottom-4 right-4 left-4 sm:left-auto z-50 w-auto sm:w-96 sm:max-w-[calc(100vw-2rem)] bg-ink-900 text-white rounded-xl shadow-2xl border border-ink-700 overflow-hidden">
      <div className={`px-4 py-3 ${isInbound ? "bg-emerald-700" : "bg-brand-700"} flex items-center gap-3`}>
        <div className="h-8 w-8 rounded-full bg-white/20 grid place-items-center text-sm font-bold">
          {call.leadName.split(" ").map(n => n[0]).slice(0, 2).join("")}
        </div>
        <div className="flex-1 min-w-0">
          <div className="font-semibold truncate">{call.leadName}</div>
          <div className="text-xs opacity-90 font-mono tabular-nums truncate">{call.phone}</div>
        </div>
        <button className="inline-flex items-center gap-1 text-xs opacity-70 hover:opacity-100 transition-opacity whitespace-nowrap" onClick={() => navigate(`/leads/${call.leadId}`)}>
          Open lead
          <Icon name="arrowRight" size={12} />
        </button>
      </div>

      <div className="px-4 py-3">
        <div className="flex items-center justify-between mb-3">
          <span className={`inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full ${statusClass(call.status)}`}>
            {isInbound && <Icon name="bell" size={12} />}
            {call.status}
            {call.isHeld && " · on hold"}
            {call.isMuted && " · muted"}
          </span>
          <span className="text-sm font-mono tabular-nums whitespace-nowrap">{formatTime(elapsed)}</span>
        </div>

        {call.status === "ringing" && isInbound && (
          <Button
            variant="success"
            size="md"
            fullWidth
            className="mb-2"
            leftIcon={<Icon name="phone" size={16} />}
            onClick={() => answer(call.id)}>
            Answer
          </Button>
        )}

        <div className="grid grid-cols-3 gap-2 mb-2">
          <DockBtn active={call.isMuted} onClick={() => mute({ id: call.id, mute: !call.isMuted })}>
            <Icon name={call.isMuted ? "micOff" : "mic"} size={16} />
            {call.isMuted ? "Unmute" : "Mute"}
          </DockBtn>
          <DockBtn active={call.isHeld} onClick={() => hold({ id: call.id, hold: !call.isHeld })}>
            <Icon name={call.isHeld ? "play" : "pause"} size={16} />
            {call.isHeld ? "Resume" : "Hold"}
          </DockBtn>
          <DockBtn onClick={() => setShowDialpad(s => !s)}>
            <Icon name="grid" size={16} />
            Pad
          </DockBtn>
        </div>

        {showDialpad && (
          <div className="grid grid-cols-3 gap-1 mb-2">
            {["1","2","3","4","5","6","7","8","9","*","0","#"].map(d => (
              <button key={d}
                className="bg-ink-800 hover:bg-ink-700 rounded-lg py-2 font-mono tabular-nums transition-colors focus:outline-none focus:ring-2 focus:ring-brand-500/50"
                onClick={() => dtmf({ id: call.id, digits: d })}>{d}</button>
            ))}
          </div>
        )}

        <form className="flex gap-1 mb-2" onSubmit={async (e) => {
          e.preventDefault();
          if (!smsBody.trim()) return;
          try {
            await sendSms({ leadId: call.leadId, body: smsBody }).unwrap();
            setSmsBody("");
            toast.success("SMS sent");
          } catch {
            toast.error("SMS not sent", "Try again.");
          }
        }}>
          <input className="flex-1 min-w-0 bg-ink-800 border border-ink-700 rounded-lg px-2 py-1 text-sm placeholder-ink-400 transition-colors focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/40"
            placeholder="Quick SMS to lead..." value={smsBody} onChange={(e) => setSmsBody(e.target.value)} />
          <Button type="submit" variant="secondary" size="sm" leftIcon={<Icon name="send" size={14} />}>
            Send
          </Button>
        </form>

        <Button
          variant="danger"
          size="md"
          fullWidth
          leftIcon={<Icon name="phoneOff" size={16} />}
          onClick={() => hangup(call.id)}>
          Hang up
        </Button>
      </div>
    </div>
  );
}

function DockBtn({ children, onClick, active }: { children: ReactNode; onClick: () => void; active?: boolean }) {
  return (
    <button
      onClick={onClick}
      className={`inline-flex items-center justify-center gap-1.5 rounded-lg py-2 text-sm transition-colors focus:outline-none focus:ring-2 focus:ring-brand-500/50 ${active ? "bg-amber-500 text-ink-900" : "bg-ink-800 hover:bg-ink-700"}`}>
      {children}
    </button>
  );
}

function statusClass(status: string) {
  switch (status) {
    case "ringing": return "bg-amber-500/30 text-amber-200";
    case "answered": return "bg-emerald-500/30 text-emerald-200";
    case "ended": return "bg-ink-500/30 text-ink-200";
    default: return "bg-ink-500/30 text-ink-200";
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
