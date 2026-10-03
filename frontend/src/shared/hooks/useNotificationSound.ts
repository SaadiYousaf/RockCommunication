import { useCallback, useEffect, useRef } from "react";

/**
 * The alert that plays when something new arrives.
 *
 * Synthesised with the Web Audio API rather than shipped as an audio file. A floor needs this to cut
 * through a room full of people already talking, which means it has to be bright and present — and
 * tuning "bright and present" against a static mp3 means re-exporting an asset every time. Here it
 * is four numbers. It also costs no download, and cannot fail to load.
 *
 * The sound is a rising two-note chime (G5 → C6) with a second voice an octave below for body. A
 * rising interval reads as "something arrived"; a falling one reads as an error, which is not what a
 * new message is.
 */

/** Remembered across sessions so turning the sound off actually sticks. */
const MUTED_KEY = "notification-sound-muted";

export function isNotificationSoundMuted(): boolean {
  try {
    return localStorage.getItem(MUTED_KEY) === "1";
  } catch {
    return false;   // private browsing, blocked storage — default to audible
  }
}

export function setNotificationSoundMuted(muted: boolean): void {
  try {
    localStorage.setItem(MUTED_KEY, muted ? "1" : "0");
  } catch { /* nothing to do — the preference just won't persist */ }
}

type Ctor = typeof AudioContext;

function audioContextCtor(): Ctor | null {
  if (typeof window === "undefined") return null;
  const w = window as unknown as { AudioContext?: Ctor; webkitAudioContext?: Ctor };
  return w.AudioContext ?? w.webkitAudioContext ?? null;
}

/**
 * Returns a function that plays the alert.
 *
 * One AudioContext is kept for the life of the page: creating one per sound leaks them, and
 * browsers cap how many a page may hold — after which the sound simply stops working with no error.
 */
export function useNotificationSound() {
  const ctxRef = useRef<AudioContext | null>(null);

  useEffect(() => {
    return () => {
      ctxRef.current?.close().catch(() => {});
      ctxRef.current = null;
    };
  }, []);

  return useCallback(() => {
    if (isNotificationSoundMuted()) return;

    try {
      const Ctor = audioContextCtor();
      if (!Ctor) return;

      const ctx = ctxRef.current ?? (ctxRef.current = new Ctor());
      // Browsers start a context suspended until the page has been interacted with. Resuming is
      // the documented way back; if the user genuinely hasn't touched the page yet it stays
      // suspended and nothing plays, which is the browser's call to make, not ours.
      if (ctx.state === "suspended") ctx.resume().catch(() => {});

      const now = ctx.currentTime;

      // A master gain the whole chime passes through, so one envelope controls the tail and
      // nothing clips when the two voices overlap.
      const master = ctx.createGain();
      master.gain.setValueAtTime(0.0001, now);
      master.connect(ctx.destination);

      // Takes the edge off the square-ish harmonics so it carries without being shrill.
      const tone = ctx.createBiquadFilter();
      tone.type = "lowpass";
      tone.frequency.setValueAtTime(5200, now);
      tone.connect(master);

      const note = (freq: number, start: number, duration: number, level: number, type: OscillatorType) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = type;
        osc.frequency.setValueAtTime(freq, now + start);

        // Fast attack, exponential decay — a struck-bell shape. A linear fade sounds like a beep;
        // this sounds like something being hit, which is what carries across a room.
        gain.gain.setValueAtTime(0.0001, now + start);
        gain.gain.exponentialRampToValueAtTime(level, now + start + 0.012);
        gain.gain.exponentialRampToValueAtTime(0.0001, now + start + duration);

        osc.connect(gain);
        gain.connect(tone);
        osc.start(now + start);
        osc.stop(now + start + duration + 0.05);
      };

      // G5 then C6 — the rising fourth that reads as an arrival.
      note(783.99, 0, 0.30, 0.42, "triangle");
      note(1046.50, 0.085, 0.42, 0.40, "triangle");
      // An octave below each, quieter: gives the chime a body that survives laptop speakers.
      note(392.00, 0, 0.26, 0.16, "sine");
      note(523.25, 0.085, 0.38, 0.15, "sine");

      master.gain.exponentialRampToValueAtTime(0.9, now + 0.01);
      master.gain.exponentialRampToValueAtTime(0.0001, now + 0.55);
    } catch {
      // Audio is a nicety. It must never take a page down, and a browser that refuses to play is
      // not something to report to the user.
    }
  }, []);
}
