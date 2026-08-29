import { useEffect, useRef, useState, type ReactNode } from "react";
import { Icon } from "./Icon";
import { cn } from "./cn";

export interface RowMenuItem {
  key: string;
  label: ReactNode;
  onClick: () => void;
  icon?: ReactNode;
  danger?: boolean;
  disabled?: boolean;
}

/**
 * The "⋯" overflow for a table row.
 *
 * WHY THIS EXISTS: the kit had no menu primitive, so every list answered "where do I put this
 * action?" by adding another button. My Leads reached six per row — Dial, Verified, Closed, two
 * dispositions and Open — which asks the reader to work out which pipeline step they are performing
 * before they can do anything. One primary action stays visible; everything else lives here.
 *
 * Deliberately small: no portal, no focus trap. It closes on outside click, on Escape, and after any
 * choice, which is the whole contract a row menu needs.
 */
export function RowMenu({ items, label = "More actions" }: { items: RowMenuItem[]; label?: string }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

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

  const usable = items.filter((i) => !i.disabled);
  if (usable.length === 0) return null;

  return (
    <div className="relative inline-block" ref={ref}>
      <button
        type="button"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className={cn(
          "h-8 w-8 grid place-items-center rounded-lg text-ink-500 transition-colors",
          "hover:bg-ink-100 hover:text-ink-800",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40",
          open && "bg-ink-100 text-ink-800",
        )}
      >
        <Icon name="menu" size={15} />
      </button>

      {open && (
        <div
          role="menu"
          className="absolute right-0 z-30 mt-1 min-w-[13rem] rounded-xl border border-ink-200 bg-white py-1 shadow-pop"
        >
          {items.map((i) => (
            <button
              key={i.key}
              role="menuitem"
              type="button"
              disabled={i.disabled}
              onClick={() => { setOpen(false); i.onClick(); }}
              className={cn(
                "flex w-full items-center gap-2 px-3 py-2 text-left text-sm transition-colors",
                "focus-visible:outline-none focus-visible:bg-ink-50",
                i.disabled
                  ? "cursor-not-allowed text-ink-300"
                  : i.danger
                    ? "text-rose-600 hover:bg-rose-50"
                    : "text-ink-700 hover:bg-ink-50",
              )}
            >
              {i.icon}
              <span className="truncate">{i.label}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
