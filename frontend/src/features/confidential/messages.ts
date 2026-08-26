/**
 * Centralized, user-facing copy for the Confidential portal-logins vault: toast
 * titles/descriptions, confirm-dialog copy, and full-sentence empty states. Short UI labels
 * (buttons, headers, placeholders) stay inline in the component.
 */
export const CONFIDENTIAL_MSG = {
  loginUpdated: "Login updated",
  loginSaved: "Login saved",
  saveFailed: "Couldn't save",
  deleteConfirmTitle: "Delete this login?",
  deleteConfirmDesc: (portalName: string) =>
    `Remove the stored login for "${portalName}"? This can't be undone.`,
  deleteConfirmLabel: "Delete",
  loginDeleted: "Login deleted",
  deleteFailed: "Couldn't delete",
  copied: "Copied",
  copiedDesc: (label: string) => `${label} copied to clipboard.`,
  revealFailed: "Couldn't reveal the password",
  copyFailed: "Couldn't copy",
  copyFailedDesc: "Your browser blocked clipboard access.",
  emptyNoMatchTitle: "No matches",
  emptyNoLoginsTitle: "No logins yet",
  emptyNoMatchDesc: "No stored login matches your search.",
  emptyNoLoginsDesc: "Add a portal login to share it securely with your admins.",
} as const;
