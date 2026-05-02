import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * Underlined tab strip. Each tab has a hover preview indicator (faint) and an
 * active-state indicator (brand gradient) on the bottom edge. Counts render as
 * a soft pill that switches tone with selection.
 */
export function Tabs<T extends string>({
  value, onChange, items, className, fullWidth,
}: {
  value: T;
  onChange: (v: T) => void;
  items: { value: T; label: ReactNode; count?: number; icon?: ReactNode; disabled?: boolean }[];
  className?: string;
  /** Stretch tabs to fill the row evenly. */
  fullWidth?: boolean;
}) {
  return (
    <div
      role="tablist"
      className={cn(
        "flex items-center gap-1 border-b hairline relative",
        fullWidth && "w-full",
        className,
      )}
    >
      {items.map((it) => {
        const active = it.value === value;
        return (
          <button
            key={it.value}
            type="button"
            role="tab"
            aria-selected={active}
            disabled={it.disabled}
            onClick={() => !it.disabled && onChange(it.value)}
            className={cn(
              "group relative px-3.5 py-2.5 text-sm font-medium -mb-px",
              "inline-flex items-center gap-2 outline-none",
              "transition-colors duration-150",
              fullWidth && "flex-1 justify-center",
              active
                ? "text-brand-700"
                : "text-ink-500 hover:text-ink-800",
              it.disabled && "opacity-50 cursor-not-allowed",
            )}
          >
            {it.icon}
            <span className="truncate">{it.label}</span>
            {typeof it.count === "number" && (
              <span className={cn(
                "text-[10.5px] font-semibold px-1.5 min-w-[20px] py-0.5 rounded-full leading-none",
                "transition-colors duration-150",
                active
                  ? "bg-brand-100 text-brand-700"
                  : "bg-ink-100 text-ink-600 group-hover:bg-ink-200/70",
              )}>{it.count}</span>
            )}
            {/* Animated indicator — slides under the active tab */}
            <span
              aria-hidden
              className={cn(
                "pointer-events-none absolute left-1.5 right-1.5 -bottom-px h-[2.5px] rounded-full",
                "transition-all duration-200 ease-out-quint",
                active
                  ? "bg-gradient-to-r from-brand-500 via-brand-600 to-accent-500 opacity-100 scale-x-100"
                  : "bg-ink-300 opacity-0 group-hover:opacity-30 scale-x-50",
                "origin-center",
              )}
            />
          </button>
        );
      })}
    </div>
  );
}
