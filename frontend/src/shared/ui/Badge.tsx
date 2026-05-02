import type { ReactNode } from "react";
import { cn } from "./cn";

type Tone = "default" | "brand" | "accent" | "success" | "warning" | "danger" | "info" | "neutral";
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
