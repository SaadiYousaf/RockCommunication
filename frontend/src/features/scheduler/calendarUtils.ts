/**
 * Calendar/date helpers for the scheduler. All grid math is done in the viewer's LOCAL time
 * zone (meetings are stored/transported as UTC ISO instants and converted at the edges here).
 */

const pad = (n: number) => String(n).padStart(2, "0");

/** First day of the month containing `d`, at local midnight. */
export function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

/** `d` shifted by `n` whole months (local). */
export function addMonths(d: Date, n: number): Date {
  return new Date(d.getFullYear(), d.getMonth() + n, 1);
}

/** `d` shifted by `n` whole days (local midnight preserved). */
export function addDays(d: Date, n: number): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
}

/** The Sunday on/before the 1st of `month` — the first cell of a 6×7 calendar grid. */
export function startOfCalendarGrid(month: Date): Date {
  const first = startOfMonth(month);
  return addDays(first, -first.getDay());
}

/** The 42 day cells (6 weeks) that make up the month view for `month`. */
export function buildGrid(month: Date): Date[] {
  const start = startOfCalendarGrid(month);
  return Array.from({ length: 42 }, (_, i) => addDays(start, i));
}

/** Local `YYYY-MM-DD` key for grouping meetings onto their day cell. */
export function dayKey(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Local day key for a UTC ISO instant (its calendar day in the viewer's zone). */
export function isoDayKey(iso: string): string {
  return dayKey(new Date(iso));
}

export function isSameDay(a: Date, b: Date): boolean {
  return dayKey(a) === dayKey(b);
}

/** A UTC ISO instant → the `<input type="datetime-local">` value in local time. */
export function toLocalInput(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** A `datetime-local` value (local wall-clock) → a UTC ISO instant. */
export function fromLocalInput(local: string): string {
  return new Date(local).toISOString();
}

/** Build a `datetime-local` value for `day` at the given local hour/minute. */
export function localInputAt(day: Date, hour: number, minute: number): string {
  const d = new Date(day.getFullYear(), day.getMonth(), day.getDate(), hour, minute);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** `datetime-local` value shifted by `minutes`. */
export function addMinutesToLocalInput(local: string, minutes: number): string {
  const d = new Date(local);
  d.setMinutes(d.getMinutes() + minutes);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

const TIME_FMT = new Intl.DateTimeFormat(undefined, { hour: "numeric", minute: "2-digit" });
const MONTH_YEAR_FMT = new Intl.DateTimeFormat(undefined, { month: "long", year: "numeric" });
const LONG_FMT = new Intl.DateTimeFormat(undefined, {
  weekday: "short", month: "short", day: "numeric", hour: "numeric", minute: "2-digit",
});
const DAY_FMT = new Intl.DateTimeFormat(undefined, { weekday: "long", month: "long", day: "numeric", year: "numeric" });

/** Local clock time for a UTC instant, e.g. "9:30 AM". */
export function formatTime(iso: string): string {
  return TIME_FMT.format(new Date(iso));
}

/** "September 2026" for the calendar header. */
export function formatMonthYear(d: Date): string {
  return MONTH_YEAR_FMT.format(d);
}

/** "Mon, Sep 14, 9:30 AM" for a UTC instant. */
export function formatLong(iso: string): string {
  return LONG_FMT.format(new Date(iso));
}

/** A full local date, e.g. "Monday, September 14, 2026". */
export function formatDayLong(d: Date): string {
  return DAY_FMT.format(d);
}

/**
 * A meeting's when-line: same-day meetings show one date and a time range; multi-day
 * meetings repeat the date. Times are local.
 */
export function formatRange(startIso: string, endIso: string): string {
  const start = new Date(startIso);
  const end = new Date(endIso);
  const sameDay = isSameDay(start, end);
  const startPart = LONG_FMT.format(start);
  const endPart = sameDay ? TIME_FMT.format(end) : LONG_FMT.format(end);
  return `${startPart} – ${endPart}`;
}
