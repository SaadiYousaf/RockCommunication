/**
 * Centralized user-facing copy for the workspace/context feature (the top-bar switcher and the
 * post-login context picker). Toast and empty-state strings live here; the generic
 * "Please try again." fallback comes from the shared MESSAGES helper in the components.
 */
export const CONTEXT_MSG = {
  workspaceSwitched: "Workspace switched",
  nowViewingAll: "Now viewing all call centers",
  switchFailed: "Couldn't switch workspace",
  enterFailed: "Couldn't enter that workspace",
  /** Lowercase noun for the "Couldn't load <x>" error state on the roster step. */
  rosterResourceName: "this workspace's team",
  noMembersTitle: "No members yet",
  noMembersDesc: "No users are assigned to this scope. You can still enter and manage it.",

  // Shown on a brand-new platform, where there is genuinely nothing to pick yet. Without this the
  // first screen the owner ever sees is a single option and no indication of what to do next.
  noAgenciesTitle: "No agencies yet",
  noAgenciesDesc: "Continue platform-wide, then set up your first agency to start adding people and leads.",
} as const;
