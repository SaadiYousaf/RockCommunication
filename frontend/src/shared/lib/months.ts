/**
 * Month arithmetic for the queue screens, in the viewer's own local time.
 *
 * Shared rather than copied: the submission queue and the closer's queue both work by month, and
 * two implementations of "where does October start" would eventually disagree about a sale made
 * near midnight on the 1st.
 */

/** The "YYYY-MM" an <input type="month"> uses, from a LOCAL date. */
export const monthKey = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;

/** This month, for defaulting a picker. */
export const thisMonth = () => monthKey(new Date());

/**
 * The half-open range for a month: its first instant, and the first instant of the next.
 *
 * Half-open rather than "last day 23:59:59" — something recorded at 23:59:59.4 on the 31st belongs
 * to that month, and an inclusive end either drops it or needs a fractional second nobody remembers
 * to write. Sent as ISO instants so the server does no calendar arithmetic of its own.
 */
export function monthRange(key: string): { from: string; to: string } {
  const [year, month] = key.split("-").map(Number);
  return {
    from: new Date(year, month - 1, 1).toISOString(),
    to: new Date(year, month, 1).toISOString(),
  };
}

/** "October 2026", in the viewer's own locale. */
export const monthLabel = (key: string) => {
  const [year, month] = key.split("-").map(Number);
  return new Date(year, month - 1, 1).toLocaleDateString(undefined, { month: "long", year: "numeric" });
};

/** Steps a month key backwards or forwards. */
export function shiftMonth(key: string, by: number): string {
  const [year, month] = key.split("-").map(Number);
  return monthKey(new Date(year, month - 1 + by, 1));
}
