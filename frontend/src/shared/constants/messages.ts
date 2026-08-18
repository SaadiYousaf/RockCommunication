/**
 * Centralized, human-friendly UI copy. One home for reusable, user-facing messages so the same
 * wording isn't duplicated across pages.
 *
 * RULE: never surface internal identifiers (permission codes like `team.write`, role names, GUIDs)
 * to end users. Use these helpers — they speak in plain language about what the user can/can't do.
 */
export const MESSAGES = {
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
  tryAgain: "Please try again.",
  savedOk: "Saved.",
} as const;
