import type { ReactNode } from "react";
import { cn } from "./cn";
import { Icon, type IconName } from "./Icon";

export interface BulkAction {
  key: string;
  label: string;
  icon?: IconName;
  onClick: () => void;
  /** Tints the action red for destructive operations (delete, archive). */
  danger?: boolean;
  disabled?: boolean;
  loading?: boolean;
}

/**
 * Floating action bar that rises from the bottom when rows are selected. It's the single,
 * consistent home for bulk operations across every list in the app: a live count, the
 * available actions, and a clear button. Renders nothing when the selection is empty.
 *
 * Position: fixed + centered so it stays reachable regardless of scroll, with a safe-area
 * inset for notched devices. `respectSidebar` nudges it right of the app rail on desktop.
 */
export function BulkActionBar({
  count, itemNoun = "item", actions, onClear, extra, respectSidebar = true,
}: {
  count: number;
  /** Singular noun; pluralised automatically ("3 leads selected"). */
  itemNoun?: string;
  actions: BulkAction[];
  onClear: () => void;
  /** Optional extra controls (e.g. a select-all-across-pages toggle). */
  extra?: ReactNode;
  respectSidebar?: boolean;
}) {
  if (count <= 0) return null;
  const noun = count === 1 ? itemNoun : `${itemNoun}s`;

  return (
    <div
      className={cn(
        "fixed bottom-5 z-40 left-0 right-0 flex justify-center px-4 pointer-events-none",
        respectSidebar && "lg:pl-[var(--app-rail,0px)]",
      )}
      style={{ paddingBottom: "env(safe-area-inset-bottom)" }}
      role="region" aria-label="Bulk actions"
    >
      <div className="pointer-events-auto animate-slide-up flex items-center gap-2 rounded-2xl border border-ink-800/10 bg-ink-900/95 px-3 py-2 text-white shadow-pop backdrop-blur-sm max-w-[calc(100vw-2rem)] overflow-x-auto">
        <span className="inline-flex items-center gap-2 pl-1 pr-1 whitespace-nowrap">
          <span className="grid h-6 min-w-6 place-items-center rounded-full bg-brand-500 px-1.5 text-xs font-semibold tabular-nums">{count}</span>
          <span className="text-sm text-white/80">{noun} selected</span>
        </span>

        {extra}

        <span className="mx-1 h-5 w-px bg-white/15" />

        <div className="flex items-center gap-1">
          {actions.map((a) => (
            <button
              key={a.key} type="button" onClick={a.onClick} disabled={a.disabled || a.loading}
              className={cn(
                "inline-flex items-center gap-1.5 h-8 rounded-lg px-2.5 text-sm font-medium whitespace-nowrap transition-colors disabled:opacity-50",
                a.danger ? "text-rose-200 hover:bg-rose-500/20" : "text-white/90 hover:bg-white/10",
              )}
            >
              {a.loading
                ? <Icon name="spinner" size={15} className="animate-spin" />
                : a.icon && <Icon name={a.icon} size={15} />}
              {a.label}
            </button>
          ))}
        </div>

        <button type="button" onClick={onClear} aria-label="Clear selection" title="Clear selection"
          className="ml-1 grid h-8 w-8 place-items-center rounded-lg text-white/60 hover:bg-white/10 hover:text-white transition-colors">
          <Icon name="x" size={16} />
        </button>
      </div>
    </div>
  );
}
