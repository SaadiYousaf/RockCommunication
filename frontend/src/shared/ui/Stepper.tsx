import { Fragment } from "react";
import { cn } from "./cn";
import { Icon } from "./Icon";

export interface Step { key: string; label: string; }

/**
 * A compact horizontal workflow indicator — chips joined by chevrons, with the reached steps
 * filled, the current step emphasised, and future steps muted. Generic: drop in any ordered set
 * of stages (a bug lifecycle, an onboarding flow, a pipeline) and the index that's active.
 */
export function Stepper({ steps, currentIndex, className }: {
  steps: Step[];
  /** Index of the active step; earlier steps render as done, later as upcoming. -1 = none active. */
  currentIndex: number;
  className?: string;
}) {
  return (
    <div className={cn("flex items-center gap-1 overflow-x-auto py-0.5", className)} role="list">
      {steps.map((s, i) => {
        const done = i < currentIndex;
        const active = i === currentIndex;
        return (
          <Fragment key={s.key}>
            {i > 0 && <Icon name="chevronRight" size={12} className="shrink-0 text-ink-300" />}
            <span role="listitem"
              className={cn(
                "inline-flex items-center gap-1 h-7 px-2.5 rounded-full text-xs whitespace-nowrap border transition-colors",
                active ? "border-brand-500 bg-brand-500 text-white shadow-sm font-medium"
                  : done ? "border-brand-200 bg-brand-50 text-brand-700"
                    : "border-ink-200 bg-white text-ink-400")}>
              {done && <Icon name="check" size={12} />}
              {s.label}
            </span>
          </Fragment>
        );
      })}
    </div>
  );
}
