/**
 * Centralized, user-facing copy for the Callbacks feature. One home for the toast / empty-state
 * sentences shown to users, so the same wording isn't duplicated or drifting across the page.
 *
 * RULE: never surface internal identifiers (permission codes, role names, GUIDs). Speak plainly about
 * what the user can/can't do; lean on the shared MESSAGES helpers for read-only / permission copy.
 */
export const CALLBACKS_MSG = {
  exportReadyTitle: "Export ready",
  rowsDownloaded: (count: number) => `${count} rows downloaded.`,

  callbackScheduledTitle: "Callback scheduled",
  callbackScheduledBody: (whenText: string) => `Reminder set for ${whenText}.`,
  scheduleCallbackFailed: "Couldn't schedule callback",
  callbackCompleted: "Callback completed",
  markCompleteFailed: "Couldn't mark complete",

  noCallbacksTitle: "No callbacks scheduled",
  noCallbacksBody: "Schedule a callback to keep customer follow-ups on track.",

  searchPlaceholder: "Search by lead, phone or reason…",
  noMatchTitle: "No matches",
  noMatchDesc: "No callbacks match your search. Try a different term.",
} as const;
