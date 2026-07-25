import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { cn } from "./cn";
import { Icon } from "./Icon";

/**
 * Contextual help: a small (i) icon that reveals a popover explaining a term or field.
 * Opens on hover AND click/tap (touch-friendly); closes on Escape, outside-click, or blur.
 * Holds rich, wrapping content (unlike the single-line Tooltip).
 *
 *   <InfoHint title="Lead score">Higher = more likely to convert…</InfoHint>
 */
export function InfoHint({
  title, children, side = "top", size = 14, className, label = "More information",
}: {
  title?: ReactNode;
  children: ReactNode;
  side?: "top" | "bottom" | "left" | "right";
  size?: number;
  className?: string;
  label?: string;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLSpanElement>(null);
  const hoverTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const id = useId();

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const sidePos = {
    top:    "bottom-full left-1/2 -translate-x-1/2 mb-2",
    bottom: "top-full left-1/2 -translate-x-1/2 mt-2",
    left:   "right-full top-1/2 -translate-y-1/2 mr-2",
    right:  "left-full top-1/2 -translate-y-1/2 ml-2",
  }[side];

  const arrowPos = {
    top:    "-bottom-1 left-1/2 -translate-x-1/2 border-r border-b",
    bottom: "-top-1 left-1/2 -translate-x-1/2 border-l border-t",
    left:   "-right-1 top-1/2 -translate-y-1/2 border-t border-r",
    right:  "-left-1 top-1/2 -translate-y-1/2 border-b border-l",
  }[side];

  return (
    <span
      ref={ref}
      className={cn("relative inline-flex align-middle", className)}
      onMouseEnter={() => { if (hoverTimer.current) clearTimeout(hoverTimer.current); hoverTimer.current = setTimeout(() => setOpen(true), 120); }}
      onMouseLeave={() => { if (hoverTimer.current) clearTimeout(hoverTimer.current); setOpen(false); }}
    >
      <button
        type="button"
        aria-label={label}
        aria-expanded={open}
        aria-describedby={open ? id : undefined}
        className="text-ink-400 hover:text-brand-600 rounded-full leading-none focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
        onClick={(e) => { e.preventDefault(); e.stopPropagation(); setOpen((o) => !o); }}
        onBlur={() => setOpen(false)}
      >
        <Icon name="info" size={size} />
      </button>
      {open && (
        <span
          id={id}
          role="tooltip"
          className={cn(
            "absolute z-[60] w-max max-w-xs text-left font-normal normal-case tracking-normal",
            "px-3 py-2.5 rounded-xl text-xs leading-relaxed",
            "bg-white text-ink-600 shadow-pop ring-1 ring-ink-200",
            "animate-fade-in",
            sidePos,
          )}
        >
          {title && <div className="font-semibold text-ink-900 mb-1 text-[12.5px]">{title}</div>}
          <div>{children}</div>
          <span aria-hidden className={cn("absolute w-2 h-2 rotate-45 bg-white ring-1 ring-ink-200", arrowPos)} />
        </span>
      )}
    </span>
  );
}
