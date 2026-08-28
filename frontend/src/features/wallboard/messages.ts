/**
 * User-facing copy for the wallboard.
 *
 * The tiles are read from across the room, so these hints stay short — they answer "what happens if
 * I click this?" in three or four words, not a sentence.
 */
export const WALLBOARD_MSG = {
  /** Fallback for a linked tile with no more specific hint. */
  viewDetail: "View detail",

  seeAgents: "See agents",
  seeAttendance: "See attendance",
  seeCalls: "See calls",
  seeLeads: "See leads",
  seeSales: "See sales",
  seeQueues: "See queues",
} as const;
