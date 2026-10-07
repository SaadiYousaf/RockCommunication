import type { ReactNode } from "react";
import { cn } from "./cn";

// Eleven tones, not eight, and every one visually distinct. Several queues in this product carry
// more states than the original palette could tell apart — the submission queue has nine, four of
// which all rendered the same red, so a glance down the column said "something is wrong" without
// saying which thing. The extra hues exist to be read, not to decorate.
type Tone =
  | "default" | "brand" | "accent" | "success" | "warning" | "danger" | "info" | "neutral"
  | "purple" | "orange" | "pink" | "dark" | "indigo" | "lime" | "red";
/** Canonical badge/tone union — reuse this instead of re-declaring `Tone` per page. */
export type BadgeTone = Tone;
type Variant = "soft" | "solid" | "outline";
type Size = "sm" | "md";

const tones: Record<Tone, Record<Variant, string>> = {
  default: {
    soft:    "bg-ink-100 text-ink-700 ring-1 ring-inset ring-ink-200/60",
    solid:   "bg-ink-900 text-white",
    outline: "border border-ink-200 text-ink-700 bg-white",
  },
  brand: {
    soft:    "bg-brand-50 text-brand-700 ring-1 ring-inset ring-brand-200/70",
    solid:   "bg-brand-600 text-white",
    outline: "border border-brand-200 text-brand-700 bg-white",
  },
  accent: {
    soft:    "bg-accent-50 text-accent-700 ring-1 ring-inset ring-accent-200/70",
    solid:   "bg-accent-600 text-white",
    outline: "border border-accent-200 text-accent-700 bg-white",
  },
  success: {
    soft:    "bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200/70",
    solid:   "bg-emerald-600 text-white",
    outline: "border border-emerald-200 text-emerald-700 bg-white",
  },
  warning: {
    soft:    "bg-amber-50 text-amber-800 ring-1 ring-inset ring-amber-200/70",
    solid:   "bg-amber-500 text-white",
    outline: "border border-amber-200 text-amber-800 bg-white",
  },
  danger: {
    soft:    "bg-rose-50 text-rose-700 ring-1 ring-inset ring-rose-200/70",
    solid:   "bg-rose-600 text-white",
    outline: "border border-rose-200 text-rose-700 bg-white",
  },
  // Was an exact copy of `brand`, which made "informational" and "this is our brand colour"
  // impossible to tell apart anywhere the two appeared together. Now genuinely its own blue.
  info: {
    soft:    "bg-sky-50 text-sky-700 ring-1 ring-inset ring-sky-200/70",
    solid:   "bg-sky-600 text-white",
    outline: "border border-sky-200 text-sky-700 bg-white",
  },
  neutral: {
    soft:    "bg-ink-50 text-ink-600 ring-1 ring-inset ring-ink-200/60",
    solid:   "bg-ink-500 text-white",
    outline: "border border-ink-200 text-ink-500 bg-white",
  },
  purple: {
    soft:    "bg-violet-50 text-violet-700 ring-1 ring-inset ring-violet-200/70",
    solid:   "bg-violet-600 text-white",
    outline: "border border-violet-200 text-violet-700 bg-white",
  },
  orange: {
    soft:    "bg-orange-50 text-orange-700 ring-1 ring-inset ring-orange-200/70",
    solid:   "bg-orange-500 text-white",
    outline: "border border-orange-200 text-orange-700 bg-white",
  },
  pink: {
    soft:    "bg-fuchsia-50 text-fuchsia-700 ring-1 ring-inset ring-fuchsia-200/70",
    solid:   "bg-fuchsia-600 text-white",
    outline: "border border-fuchsia-200 text-fuchsia-700 bg-white",
  },
  // Three more, for the submission queue's fifteen outcomes. Picked for distance from what is
  // already here: indigo sits away from sky and violet, lime away from emerald and amber, and a
  // true red away from rose.
  indigo: {
    soft:    "bg-indigo-50 text-indigo-700 ring-1 ring-inset ring-indigo-200/70",
    solid:   "bg-indigo-600 text-white",
    outline: "border border-indigo-200 text-indigo-700 bg-white",
  },
  lime: {
    soft:    "bg-lime-50 text-lime-800 ring-1 ring-inset ring-lime-300/70",
    solid:   "bg-lime-600 text-white",
    outline: "border border-lime-300 text-lime-800 bg-white",
  },
  red: {
    soft:    "bg-red-100 text-red-800 ring-1 ring-inset ring-red-300/70",
    solid:   "bg-red-700 text-white",
    outline: "border border-red-300 text-red-800 bg-white",
  },
  // Deliberately heavier than every other soft badge. It marks a PERSON rather than the fate of a
  // policy, and it has to be the thing the eye lands on in a column of outcomes.
  dark: {
    soft:    "bg-ink-800 text-white ring-1 ring-inset ring-ink-900",
    solid:   "bg-ink-900 text-white",
    outline: "border border-ink-700 text-ink-800 bg-white",
  },
};

const sizes: Record<Size, string> = {
  sm: "px-1.5 py-0.5 text-[10.5px] gap-1 rounded-full font-medium",
  md: "px-2 py-0.5 text-xs gap-1 rounded-full font-medium",
};

export function Badge({
  children, tone = "default", variant = "soft", dot, size = "md", className,
}: {
  children: ReactNode;
  tone?: Tone;
  variant?: Variant;
  dot?: boolean;
  size?: Size;
  className?: string;
}) {
  return (
    <span className={cn(
      "inline-flex items-center whitespace-nowrap select-none",
      sizes[size],
      tones[tone][variant],
      className,
    )}>
      {dot && (
        <span
          aria-hidden
          className={cn(
            "h-1.5 w-1.5 rounded-full shrink-0",
            variant === "solid" ? "bg-white/85" : "bg-current opacity-75",
          )}
        />
      )}
      {children}
    </span>
  );
}
