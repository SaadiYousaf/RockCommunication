import type { ReactNode } from "react";

/**
 * A labelled section of a form.
 *
 * WHY: forms here were single flat grids — the intake form asked for twelve fields in one
 * undifferentiated wall. Someone filling it in live on a call is reading down the page, and "who are
 * they", "how do we reach them" and "what proves they consented" are three different questions asked
 * at three different points in the conversation. Grouping them makes the form scannable and gives
 * each part a heading a new employee can orient by.
 */
export function FieldGroup({
  title, hint, children,
}: {
  title: string;
  /** One short line explaining what this group is for, when the title alone is not obvious. */
  hint?: string;
  children: ReactNode;
}) {
  return (
    <section className="space-y-3">
      <div>
        <h3 className="text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500">{title}</h3>
        {hint && <p className="mt-0.5 text-xs text-ink-500">{hint}</p>}
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">{children}</div>
    </section>
  );
}
