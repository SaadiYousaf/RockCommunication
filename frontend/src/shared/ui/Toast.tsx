import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { cn } from "./cn";

export type ToastTone = "info" | "success" | "warning" | "danger";

export interface ToastOptions {
  title?: string;
  description?: string;
  tone?: ToastTone;
  duration?: number;
  action?: { label: string; onClick: () => void };
}

interface ToastItem extends Required<Pick<ToastOptions, "tone">> {
  id: string;
  title?: string;
  description?: string;
  duration: number;
  action?: ToastOptions["action"];
}

interface ToastApi {
  show: (opts: ToastOptions) => string;
  success: (title: string, description?: string) => string;
  error: (title: string, description?: string) => string;
  info: (title: string, description?: string) => string;
  warning: (title: string, description?: string) => string;
  dismiss: (id: string) => void;
}

const ToastCtx = createContext<ToastApi | null>(null);

export function useToast() {
  const ctx = useContext(ToastCtx);
  if (!ctx) throw new Error("useToast must be used inside <ToastProvider>");
  return ctx;
}

/**
 * Tone palette mirrors Button/Badge.  Each toast has:
 *  - a vertical accent bar (gradient)
 *  - a tinted square icon chip on the left
 *  - title + description
 *  - dismiss + optional action
 */
const tones: Record<ToastTone, {
  bar: string;
  chipBg: string;
  chipText: string;
  ringTint: string;
  glyph: ReactNode;
}> = {
  info: {
    bar: "bg-gradient-to-b from-brand-400 to-brand-600",
    chipBg: "bg-brand-50",
    chipText: "text-brand-600",
    ringTint: "shadow-[0_8px_24px_-8px_rgba(53,99,255,0.35)]",
    glyph: <Glyph path="M12 16v-5M12 8h.01M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z" />,
  },
  success: {
    bar: "bg-gradient-to-b from-emerald-400 to-emerald-600",
    chipBg: "bg-emerald-50",
    chipText: "text-emerald-600",
    ringTint: "shadow-[0_8px_24px_-8px_rgba(16,185,129,0.35)]",
    glyph: <Glyph path="M5 13l4 4L19 7" />,
  },
  warning: {
    bar: "bg-gradient-to-b from-amber-400 to-amber-600",
    chipBg: "bg-amber-50",
    chipText: "text-amber-600",
    ringTint: "shadow-[0_8px_24px_-8px_rgba(245,158,11,0.35)]",
    glyph: <Glyph path="M12 9v4m0 4h.01M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />,
  },
  danger: {
    bar: "bg-gradient-to-b from-rose-400 to-rose-600",
    chipBg: "bg-rose-50",
    chipText: "text-rose-600",
    ringTint: "shadow-[0_8px_24px_-8px_rgba(244,63,94,0.35)]",
    glyph: <Glyph path="M12 8v4m0 4h.01M12 22a10 10 0 1 1 0-20 10 10 0 0 1 0 20z" />,
  },
};

function Glyph({ path }: { path: string }) {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d={path} />
    </svg>
  );
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const timers = useRef<Record<string, number>>({});

  const dismiss = useCallback((id: string) => {
    setItems((prev) => prev.filter((x) => x.id !== id));
    if (timers.current[id]) { window.clearTimeout(timers.current[id]); delete timers.current[id]; }
  }, []);

  const show = useCallback((opts: ToastOptions) => {
    const id = Math.random().toString(36).slice(2);
    const item: ToastItem = {
      id,
      title: opts.title,
      description: opts.description,
      tone: opts.tone ?? "info",
      duration: opts.duration ?? 4500,
      action: opts.action,
    };
    setItems((prev) => [...prev, item]);
    if (item.duration > 0) {
      timers.current[id] = window.setTimeout(() => dismiss(id), item.duration);
    }
    return id;
  }, [dismiss]);

  const api = useMemo<ToastApi>(() => ({
    show,
    success: (title, description) => show({ title, description, tone: "success" }),
    error:   (title, description) => show({ title, description, tone: "danger" }),
    info:    (title, description) => show({ title, description, tone: "info" }),
    warning: (title, description) => show({ title, description, tone: "warning" }),
    dismiss,
  }), [show, dismiss]);

  useEffect(() => () => Object.values(timers.current).forEach((t) => window.clearTimeout(t)), []);

  return (
    <ToastCtx.Provider value={api}>
      {children}
      <div className="pointer-events-none fixed top-4 right-4 z-[100] flex flex-col gap-2.5 w-[380px] max-w-[calc(100vw-2rem)]">
        {items.map((t) => {
          const tone = tones[t.tone];
          return (
            <div
              key={t.id}
              role="status"
              className={cn(
                "pointer-events-auto group relative overflow-hidden",
                "bg-white/95 backdrop-saturate-160 border border-ink-200/70 rounded-2xl",
                "shadow-pop animate-slide-in",
                tone.ringTint,
              )}
            >
              {/* Left accent bar */}
              <div className={cn("absolute left-0 top-0 bottom-0 w-1", tone.bar)} />
              {/* Subtle top shine */}
              <div className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-white/80 to-transparent" />

              <div className="flex items-start gap-3 p-3.5 pl-5">
                <div className={cn("h-8 w-8 grid place-items-center rounded-lg shrink-0", tone.chipBg, tone.chipText)}>
                  {tone.glyph}
                </div>
                <div className="flex-1 min-w-0">
                  {t.title && <div className="text-sm font-semibold text-ink-900 leading-tight">{t.title}</div>}
                  {t.description && <div className="text-xs text-ink-600 mt-1 leading-relaxed">{t.description}</div>}
                  {t.action && (
                    <button
                      onClick={() => { t.action!.onClick(); dismiss(t.id); }}
                      className="mt-2 text-xs font-semibold text-brand-600 hover:text-brand-700 transition-colors"
                    >
                      {t.action.label} →
                    </button>
                  )}
                </div>
                <button
                  onClick={() => dismiss(t.id)}
                  className="text-ink-400 hover:text-ink-700 hover:bg-ink-100 -m-1 p-1 rounded-md transition-colors shrink-0"
                  aria-label="Dismiss"
                >
                  <Glyph path="M6 18 18 6M6 6l12 12" />
                </button>
              </div>

              {/* Progress bar — visualises auto-dismiss */}
              {t.duration > 0 && (
                <div className="absolute left-0 right-0 bottom-0 h-0.5 bg-ink-100 overflow-hidden">
                  <div
                    className={cn("h-full origin-left", tone.bar)}
                    style={{ animation: `toast-progress ${t.duration}ms linear forwards` }}
                  />
                </div>
              )}
            </div>
          );
        })}
      </div>
      {/* Inline keyframes for the progress bar so we don't have to touch tailwind config */}
      <style>{`@keyframes toast-progress { from { transform: scaleX(1); } to { transform: scaleX(0); } }`}</style>
    </ToastCtx.Provider>
  );
}
