import type { HTMLAttributes, ReactNode } from "react";
import { cn } from "./cn";

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  /** Adds a hover lift — useful for clickable cards. */
  interactive?: boolean;
  /** Elevated variant — chunkier shadow, larger radius. */
  elevated?: boolean;
  /**
   * Adds a 4px gradient stripe on the left edge in the given tone. Used to
   * visually anchor sections and signal status without taking title space.
   */
  accent?: "brand" | "accent" | "success" | "warning" | "danger" | null;
}

const accentMap: Record<NonNullable<CardProps["accent"]>, string> = {
  brand:   "before:bg-gradient-to-b before:from-brand-500 before:to-brand-700",
  accent:  "before:bg-gradient-to-b before:from-accent-500 before:to-accent-700",
  success: "before:bg-gradient-to-b before:from-emerald-500 before:to-emerald-700",
  warning: "before:bg-gradient-to-b before:from-amber-500 before:to-amber-700",
  danger:  "before:bg-gradient-to-b before:from-rose-500 before:to-rose-700",
};

export function Card({ className, children, interactive, elevated, accent, ...rest }: CardProps) {
  return (
    <div
      className={cn(
        elevated ? "surface-elevated" : "surface",
        "relative transition-[box-shadow,transform,border-color] duration-200 ease-out-quint",
        interactive && "hover:shadow-card-hover hover:border-ink-200 hover:-translate-y-px cursor-pointer",
        accent && "before:content-[''] before:absolute before:left-0 before:top-3 before:bottom-3 before:w-[3px] before:rounded-r-full",
        accent && accentMap[accent],
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  );
}

export function CardHeader({ title, subtitle, action, className, bordered, eyebrow }:
  {
    title: ReactNode;
    subtitle?: ReactNode;
    action?: ReactNode;
    className?: string;
    /** When true, renders a hairline divider under the header — useful for content-heavy cards. */
    bordered?: boolean;
    /** Tiny uppercase label above the title (e.g. category). */
    eyebrow?: ReactNode;
  }) {
  return (
    <div
      className={cn(
        "flex items-start justify-between gap-4 px-5 pt-5 pb-3",
        bordered && "border-b hairline mb-1",
        className,
      )}
    >
      <div className="min-w-0">
        {eyebrow && <div className="section-title mb-1">{eyebrow}</div>}
        <h3 className="text-base font-semibold text-ink-900 truncate">{title}</h3>
        {subtitle && <p className="text-xs text-ink-500 mt-0.5">{subtitle}</p>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  );
}

export function CardBody({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={cn("px-5 pb-5", className)}>{children}</div>;
}

export function CardFooter({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <div className={cn(
      "px-5 py-3 border-t hairline bg-gradient-to-b from-ink-50/40 to-ink-50/80 rounded-b-xl",
      className,
    )}>
      {children}
    </div>
  );
}
