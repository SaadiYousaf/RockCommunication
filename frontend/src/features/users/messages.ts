/**
 * Centralized, user-facing copy for the Users feature page: toast titles/descriptions and
 * full-sentence empty states. Short UI labels (buttons, headers, placeholders) stay inline.
 */
export const USERS_MSG = {
  // Search placeholders (no user-facing copy inline).
  searchPlaceholder: "Search by name, email, or role…",
  exportReady: "Export ready",
  exportReadyDesc: (n: number) => `${n} rows downloaded.`,
  loadFailedTitle: "Couldn't load users",
  loadFailedDesc: "Please refresh the page or contact your admin.",
  emptyTitleNoUsers: "No users yet",
  emptyTitleNoMatch: "No users match",
  emptyDescNoUsers: "Users will appear here once created.",
  emptyDescNoMatch: "Try a different search or role filter.",
} as const;
