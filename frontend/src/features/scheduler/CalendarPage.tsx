import { useMemo, useState } from "react";
import { useListMeetingsQuery } from "../../shared/api/baseApi";
import type { Meeting } from "./types";
import {
  addDays, addMonths, buildGrid, dayKey, formatDayLong, formatMonthYear, formatTime,
  isoDayKey, startOfCalendarGrid, startOfMonth,
} from "./calendarUtils";
import { ScheduleMeetingModal } from "./ScheduleMeetingModal";
import { MeetingDetailModal } from "./MeetingDetailModal";
import {
  Badge, Button, Card, CardBody, Icon, InfoHint, PageHeader, Skeleton, cn,
} from "../../shared/ui";

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const CHIP_LIMIT = 3;

/**
 * Month calendar — the scheduler home. Navigate months, drop onto a day to schedule, and click a
 * meeting to see its detail / RSVP. Available to any authenticated user; each person only sees
 * meetings they organize or are invited to.
 */
export function CalendarPage() {
  const [viewMonth, setViewMonth] = useState<Date>(() => startOfMonth(new Date()));
  const [expandedDays, setExpandedDays] = useState<Set<string>>(new Set());

  // Modal state: schedule (create/edit) + detail.
  const [schedule, setSchedule] = useState<{ open: boolean; day?: Date; meeting?: Meeting | null }>({ open: false });
  const [detailId, setDetailId] = useState<string | null>(null);

  const grid = useMemo(() => buildGrid(viewMonth), [viewMonth]);
  const gridStart = useMemo(() => startOfCalendarGrid(viewMonth), [viewMonth]);
  const range = useMemo(() => ({
    from: gridStart.toISOString(),
    to: addDays(gridStart, 42).toISOString(),
  }), [gridStart]);

  const { data: meetings, isLoading, isFetching } = useListMeetingsQuery(range);

  // Group meetings onto their start day (local).
  const byDay = useMemo(() => {
    const map = new Map<string, Meeting[]>();
    for (const m of meetings ?? []) {
      const key = isoDayKey(m.startsAt);
      (map.get(key) ?? map.set(key, []).get(key)!).push(m);
    }
    for (const list of map.values()) list.sort((a, b) => a.startsAt.localeCompare(b.startsAt));
    return map;
  }, [meetings]);

  const todayKey = dayKey(new Date());
  const monthIndex = viewMonth.getMonth();

  function toggleExpand(key: string) {
    setExpandedDays((prev) => {
      const next = new Set(prev);
      next.has(key) ? next.delete(key) : next.add(key);
      return next;
    });
  }

  function openScheduleFor(day: Date) { setSchedule({ open: true, day }); }
  function openNew() { setSchedule({ open: true, day: startOfMonth(new Date()).getMonth() === monthIndex ? new Date() : viewMonth }); }
  function openEdit(m: Meeting) { setDetailId(null); setSchedule({ open: true, meeting: m }); }

  return (
    <>
      <PageHeader
        eyebrow="Scheduler"
        title="Calendar"
        description="Your meetings — schedule, invite colleagues or external guests, and track RSVPs."
        actions={
          <Button leftIcon={<Icon name="plus" size={15} />} onClick={openNew}>New meeting</Button>
        }
      />

      {/* Toolbar: month navigation */}
      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-1">
            <Button variant="ghost" size="icon" aria-label="Previous month" title="Previous month" onClick={() => setViewMonth((m) => addMonths(m, -1))}>
              <Icon name="chevronLeft" size={18} />
            </Button>
            <Button variant="ghost" size="icon" aria-label="Next month" title="Next month" onClick={() => setViewMonth((m) => addMonths(m, 1))}>
              <Icon name="chevronRight" size={18} />
            </Button>
          </div>
          <h2 className="text-lg font-semibold text-ink-900 tabular-nums min-w-[168px]">{formatMonthYear(viewMonth)}</h2>
          <Button variant="outline" size="sm" title="Jump back to the current month" onClick={() => setViewMonth(startOfMonth(new Date()))}>Today</Button>
          {isFetching && !isLoading && (
            <span title="Refreshing meetings…" role="status" aria-label="Refreshing meetings" className="inline-flex">
              <Icon name="spinner" size={15} className="text-ink-400 animate-spin" />
            </span>
          )}
          <div className="ml-auto flex items-center gap-1 text-xs text-ink-500">
            {(meetings?.length ?? 0)} meeting{(meetings?.length ?? 0) === 1 ? "" : "s"} this view
            <InfoHint title="Meetings in this view" side="left">
              Counts every meeting across the six weeks currently shown. You only see meetings you
              organize or were invited to — colleagues' other meetings never appear here.
            </InfoHint>
          </div>
        </CardBody>
      </Card>

      {/* Month grid */}
      <Card>
        <CardBody className="p-0 sm:p-0">
          {/* Weekday header */}
          <div className="grid grid-cols-7 border-b hairline">
            {WEEKDAYS.map((d) => (
              <div key={d} className="px-2 py-2 text-center text-[11px] font-semibold uppercase tracking-[0.08em] text-ink-500">
                <span className="hidden sm:inline">{d}</span>
                <span className="sm:hidden">{d[0]}</span>
              </div>
            ))}
          </div>

          {isLoading ? (
            <div className="grid grid-cols-7">
              {Array.from({ length: 42 }, (_, i) => (
                <div key={i} className="min-h-[96px] border-r border-b hairline p-1.5">
                  <Skeleton className="h-4 w-5 mb-2" />
                  {i % 3 === 0 && <Skeleton className="h-4 w-full" />}
                </div>
              ))}
            </div>
          ) : (
            <div className="grid grid-cols-7">
              {grid.map((day) => {
                const key = dayKey(day);
                const inMonth = day.getMonth() === monthIndex;
                const isToday = key === todayKey;
                const dayMeetings = byDay.get(key) ?? [];
                const expanded = expandedDays.has(key);
                const shown = expanded ? dayMeetings : dayMeetings.slice(0, CHIP_LIMIT);
                const hidden = dayMeetings.length - shown.length;
                const dayLabel = formatDayLong(day);
                const countLabel = dayMeetings.length
                  ? `${dayMeetings.length} meeting${dayMeetings.length === 1 ? "" : "s"}. `
                  : "";

                return (
                  <div
                    key={key}
                    onClick={() => openScheduleFor(day)}
                    role="button"
                    tabIndex={0}
                    title={`Schedule a meeting on ${dayLabel}`}
                    aria-label={`${dayLabel}. ${countLabel}Click to schedule a meeting.`}
                    onKeyDown={(e) => { if (e.key === "Enter") openScheduleFor(day); }}
                    className={cn(
                      "min-h-[96px] sm:min-h-[112px] border-r border-b hairline p-1.5 text-left align-top",
                      "cursor-pointer transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500/40",
                      inMonth ? "bg-white hover:bg-brand-50/40" : "bg-ink-50/40 hover:bg-ink-50/70",
                    )}
                  >
                    <div className="flex items-center justify-between">
                      <span
                        className={cn(
                          "inline-flex items-center justify-center h-6 min-w-6 px-1 rounded-full text-xs tabular-nums",
                          isToday ? "bg-brand-600 text-white font-semibold"
                            : inMonth ? "text-ink-700" : "text-ink-400",
                        )}
                      >
                        {day.getDate()}
                      </span>
                    </div>

                    <div className="mt-1 space-y-1">
                      {shown.map((m) => {
                        const cancelled = m.status === "Cancelled";
                        return (
                          <button
                            key={m.id}
                            type="button"
                            onClick={(e) => { e.stopPropagation(); setDetailId(m.id); }}
                            title={`${formatTime(m.startsAt)} · ${m.title}${cancelled ? " (cancelled)" : ""} — open details`}
                            aria-label={`${formatTime(m.startsAt)} ${m.title}${cancelled ? ", cancelled" : ""}. Open meeting details.`}
                            className={cn(
                              "w-full flex items-center gap-1 px-1.5 py-1 rounded-md text-left text-[11px] leading-tight transition-colors",
                              cancelled
                                ? "bg-rose-50 text-rose-500 hover:bg-rose-100 line-through"
                                : m.isOrganizer
                                  ? "bg-brand-100/70 text-brand-800 hover:bg-brand-100"
                                  : "bg-ink-100/70 text-ink-700 hover:bg-ink-100",
                            )}
                          >
                            <span className="tabular-nums shrink-0 font-medium">{formatTime(m.startsAt)}</span>
                            <span className="truncate">{m.title}</span>
                          </button>
                        );
                      })}
                      {hidden > 0 && (
                        <button
                          type="button"
                          onClick={(e) => { e.stopPropagation(); toggleExpand(key); }}
                          className="w-full text-left text-[10.5px] text-ink-500 hover:text-ink-800 px-1.5 py-0.5 font-medium"
                        >
                          +{hidden} more
                        </button>
                      )}
                      {expanded && dayMeetings.length > CHIP_LIMIT && (
                        <button
                          type="button"
                          onClick={(e) => { e.stopPropagation(); toggleExpand(key); }}
                          className="w-full text-left text-[10.5px] text-ink-400 hover:text-ink-700 px-1.5 py-0.5"
                        >
                          Show less
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </CardBody>
      </Card>

      {/* Legend */}
      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-ink-500">
        <span className="inline-flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded bg-brand-100 ring-1 ring-brand-200" /> Organized by you</span>
        <span className="inline-flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded bg-ink-100 ring-1 ring-ink-200" /> Invited</span>
        <span className="inline-flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded bg-rose-50 ring-1 ring-rose-200" /> Cancelled</span>
        <Badge tone="brand" variant="soft" className="ml-auto">Times are local</Badge>
      </div>

      <ScheduleMeetingModal
        open={schedule.open}
        prefillDay={schedule.day}
        meeting={schedule.meeting}
        onClose={() => setSchedule({ open: false })}
      />
      {detailId && (
        <MeetingDetailModal
          meetingId={detailId}
          onClose={() => setDetailId(null)}
          onEdit={openEdit}
        />
      )}
    </>
  );
}
