/**
 * Centralized, human-friendly UI copy. One home for reusable, user-facing messages so the same
 * wording isn't duplicated across pages.
 *
 * RULE: never surface internal identifiers (permission codes like `team.write`, role names, GUIDs)
 * to end users. Use these helpers — they speak in plain language about what the user can/can't do.
 */
export const MESSAGES = {
  /** Global header search placeholder (shared chrome — no copy inline). */
  globalSearchPlaceholder: "Search leads, users, sales…   ⌘K",
  /** Read-only banner: the user can view `resource` but not change it. e.g. readOnly("the team"). */
  readOnly: (resource: string) =>
    `You have read-only access to ${resource}. Contact your administrator if you need to make changes.`,

  /** Tooltip / hint on a disabled action the user lacks permission for. e.g. noPermission("delete roles"). */
  noPermission: (action: string) =>
    `You don't have permission to ${action}. Contact your administrator for access.`,

  /**
   * The ubiquitous failed-action toast title. e.g. failed("save the lead") →
   * "Couldn't save the lead. Please try again." Use for the countless one-off "Couldn't X" toasts.
   */
  failed: (action: string) => `Couldn't ${action}. Please try again.`,

  /** Generic fallbacks used across mutations/toasts. */
  genericError: "Something went wrong. Please try again.",
  loadFailed: "Couldn't load this",
  tryAgain: "Please try again.",
  savedOk: "Saved.",

  /** Clipboard copy — shared by SensitiveValue, the 2FA secret, and anywhere else we offer "Copy". */
  copyFailed: "Couldn't copy",
  copyBlocked: "Your browser blocked clipboard access. Select the value and copy it manually.",

  /** Shared chrome: the header notifications bell (no feature messages.ts owns it). */
  markAllReadFailed: "Couldn't mark everything as read",
} as const;

/**
 * Copy for a request that FAILED to load, used by <ErrorState> and the global API-error toast.
 *
 * Deliberately says nothing about status codes, roles or permissions by name — the reader is a
 * salesperson, not an engineer. "You don't have access to this" is actionable ("ask my admin");
 * "403 Forbidden: RequirePermission(Sales.ViewAll)" is not.
 */
export const LOAD_ERROR = {
  couldNotLoad: "Couldn't load",
  couldNotLoadGeneric: "Couldn't load this",
  retry: "Try again",

  noAccessTitle: "You don't have access to this",
  noAccessBody: "Ask an administrator if you think you should be able to see it.",

  notFoundBody: "It may have been deleted or moved.",
  offlineBody: "Check your internet connection, then try again.",
  serverBody: "The server had a problem. Please try again in a moment.",
  genericBody: "Please try again.",
} as const;
