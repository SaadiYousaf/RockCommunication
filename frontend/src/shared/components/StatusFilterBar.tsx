import { useMemo } from "react";
import { cn } from "../ui/cn";
import type { BadgeTone } from "../ui";
import { STATUS_FILTER_MSG } from "../constants/messages";

/**
 * A row of status chips with live counts, for filtering a queue.
 *
 * Built for queues with more statuses than a tab strip can carry — the submission queue has nine.
 * A dropdown would hide the one number a submission agent actually wants, which is how many sales
 * are sitting in each state; chips put that in front of them and make it one click to isolate any
 * of them.
 *
 * Statuses with nothing in them are hidden rather than shown greyed out. On a nine-status queue a
 * fixed row would usually be mostly empty chips, which is noise standing between the user and the
 * three states that actually have work in them. The currently selected one always stays visible, so
 * filtering down to a status and then clearing the last row doesn't make the control vanish.
 */
export interface StatusFilterOption<S extends string> {
  value: S;
  label: string;
  tone?: BadgeTone;
}

export function StatusFilterBar<S extends string, T>({
  rows,
  statusOf,
  options,
  value,
  onChange,
  className,
}: {
  rows: readonly T[];
  /** Pulls the status off a row, so this works for any queue. */
  statusOf: (row: T) => S;
  /** Every status this queue can show, in the order they should appear. */
  options: readonly StatusFilterOption<S>[];
  /** The selected status, or null for "all". */
  value: S | null;
  onChange: (next: S | null) => void;
  className?: string;
}) {
  const counts = useMemo(() => {
    const map = new Map<S, number>();
    for (const row of rows) {
      const s = statusOf(row);
      map.set(s, (map.get(s) ?? 0) + 1);
    }
    return map;
    // statusOf is defined inline at each call site; depending on it would recount every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows]);

  const shown = options.filter((o) => (counts.get(o.value) ?? 0) > 0 || o.value === value);

  return (
    <div
      role="group"
      aria-label={STATUS_FILTER_MSG.label}
      // Scrolls rather than wraps: a wrapped second row pushes the table down the page every time
      // the mix of statuses changes, which makes the whole queue feel unstable.
      className={cn("flex items-center gap-1.5 overflow-x-auto pb-1", className)}
    >
      <Chip
        active={value === null}
        label={STATUS_FILTER_MSG.all}
        count={rows.length}
        onClick={() => onChange(null)}
      />
      {shown.map((o) => (
        <Chip
          key={o.value}
          active={value === o.value}
          label={o.label}
          count={counts.get(o.value) ?? 0}
          tone={o.tone}
          // Clicking the selected chip clears it — the way back to everything without aiming at
          // a separate "All".
          onClick={() => onChange(value === o.value ? null : o.value)}
        />
      ))}
    </div>
  );
}

/** Dot colour per badge tone — mirrors the Badge palette so the two never drift apart. */
const TONE_DOT: Record<BadgeTone, string> = {
  default: "bg-ink-400",
  neutral: "bg-ink-400",
  brand: "bg-brand-500",
  accent: "bg-accent-500",
  info: "bg-sky-500",
  purple: "bg-violet-500",
  orange: "bg-orange-500",
  pink: "bg-fuchsia-500",
  success: "bg-emerald-500",
  warning: "bg-amber-500",
  danger: "bg-rose-500",
  dark: "bg-ink-800",
  indigo: "bg-indigo-500",
  lime: "bg-lime-500",
  red: "bg-red-600",
};

function Chip({
  active, label, count, tone, onClick,
}: {
  active: boolean;
  label: string;
  count: number;
  tone?: BadgeTone;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        "shrink-0 inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-medium",
        "transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40",
        active
          ? "border-brand-300 bg-brand-50 text-brand-800"
          : "border-ink-200 text-ink-600 hover:border-ink-300 hover:bg-ink-50",
      )}
    >
      {/* Carries the same colour as the status badge in the table, so a chip and a row read as the
          same thing without repeating the badge itself. */}
      {tone && <span aria-hidden className={cn("h-1.5 w-1.5 shrink-0 rounded-full", TONE_DOT[tone])} />}
      <span>{label}</span>
      <span className={cn("tabular-nums", active ? "text-brand-700" : "text-ink-500")}>{count}</span>
    </button>
  );
}
