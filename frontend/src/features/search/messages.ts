/**
 * Centralized user-facing copy for the Global Search feature.
 * Keep inline message strings out of the page — reference these instead.
 */
export const SEARCH_MSG = {
  // CSV export
  exportReadyTitle: "Export ready",
  exportReadyDesc: (count: number) => `${count} rows downloaded.`,

  // Dialing
  dialingTitle: "Dialing",
  dialingDesc: (name: string) => `Calling ${name}…`,
  dialFailed: "Dial failed",

  // Empty states
  startTypingTitle: "Start typing",
  startTypingDesc: "Search by lead phone, email, partial name, or username/role for users.",
  noMatchesTitle: "No matches",
  noMatchesDesc: (query: string) => `Nothing matched "${query}". Try a different phone, email, or name fragment.`,
} as const;
