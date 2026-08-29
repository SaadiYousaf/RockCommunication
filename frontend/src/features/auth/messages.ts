/**
 * Sign-in copy.
 *
 * The rule here is that a failure must never teach an attacker anything. Whether a username exists,
 * whether the password was close, whether the agency was disabled, whether the account is locked —
 * all of it collapses into one of two messages. The distinction we DO draw is only useful to the
 * legitimate owner: "you typed something wrong" (retry) versus "your access is unavailable"
 * (retrying will not help, go talk to a person).
 */
export const AUTH_MSG = {
  /** Lowercase noun for the "Couldn't load <x>" error state on the register-user role picker. */
  rolesResourceName: "the list of roles",

  signInFailedTitle: "Sign in failed",
  signInFailed: "Please check your details and try again.",

  /**
   * Shown for a deactivated account, a disabled agency or call centre, a locked-out account and an
   * expired invitation alike — deliberately one message for all of them. It names no agency, no
   * status, no identifier and no internal rule.
   */
  accountUnavailable:
    "Your account is currently unavailable. Please contact your administrator for assistance.",
} as const;

/**
 * Is this failure "the account cannot be used" rather than "wrong credentials"?
 *
 * Prefers the server's machine-readable `reason`; falls back to the known non-credential messages
 * for responses that predate it. Never matches on free-form English alone.
 */
export function isUnavailableAccount(err: unknown, detail?: string): boolean {
  const body = (err as { data?: { reason?: string; title?: string } } | undefined)?.data;
  if (body?.reason === "tenant_disabled" || body?.reason === "account_disabled") return true;
  if (body?.title === "Account disabled" || body?.title === "Access unavailable") return true;

  // Legacy shapes: the server sends distinct copy for these, and they are all "go ask an admin".
  if (!detail) return false;
  return /deactivated|organization's access|organisation's access|invitation/i.test(detail);
}
