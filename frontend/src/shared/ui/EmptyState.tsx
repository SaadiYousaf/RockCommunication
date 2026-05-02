import type { ReactNode } from "react";
import { cn } from "./cn";

type Tone = "brand" | "accent" | "success" | "warning" | "danger" | "neutral";

const toneIconMap: Record<Tone, string> = {
  brand:   "from-brand-50 to-brand-100 text-brand-600 ring-brand-200/60",
  accent:  "from-accent-50 to-accent-100 text-accent-600 ring-accent-200/60",
  success: "from-emerald-50 to-emerald-100 text-emerald-700 ring-emerald-200/60",
  warning: "from-amber-50 to-amber-100 text-amber-700 ring-amber-200/60",
  danger:  "from-rose-50 to-rose-100 text-rose-700 ring-rose-200/60",
  neutral: "from-ink-50 to-ink-100 text-ink-600 ring-ink-200/60",
};

export function EmptyState({
  icon, title, description, action, className, tone = "brand", compact,
}: {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
  className?: string;
  tone?: Tone;
  /** Tighter padding for inline use inside other cards. */
  compact?: boolean;
}) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center text-center",
        compact ? "py-8 px-4" : "py-14 px-6",
        className,
      )}
    >
      {icon && (
        <div className="relative mb-5">
          {/* Soft halo behind the icon for visual lift */}
          <div className={cn(
            "absolute inset-0 -m-3 rounded-3xl blur-xl opacity-50",
            "bg-gradient-to-br",
            toneIconMap[tone].split(" ").slice(0, 2).join(" "),
          )} aria-hidden />
          <div className={cn(
            "relative h-14 w-14 rounded-2xl grid place-items-center bg-gradient-to-br ring-1 ring-inset shadow-sm",
            toneIconMap[tone],
          )}>
            {icon}
          </div>
        </div>
      )}
      <h3 className="text-base font-semibold text-ink-900 tracking-tight">{title}</h3>
      {description && (
        <p className="text-sm text-ink-500 mt-1.5 max-w-sm leading-relaxed">{description}</p>
      )}
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}
