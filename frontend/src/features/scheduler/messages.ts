/**
 * Copy for scheduling. Kept out of the components per the shared-constants rule.
 */
export const SCHEDULER_MSG = {
  /**
   * Shown under the start time when creating. Says WHY the field is constrained rather than only
   * blocking it — a meeting booked in the past is accepted by nobody's calendar and simply
   * disappears from Upcoming Events, which is confusing rather than obviously wrong.
   */
  startsInFutureHint: "Must be in the future — past meetings don't appear in Upcoming Events.",
} as const;
