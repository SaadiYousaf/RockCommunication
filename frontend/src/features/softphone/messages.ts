/**
 * Centralized user-facing copy for the Softphone (live call dock) feature.
 * Keep inline message strings out of the component — reference these instead.
 *
 * Call controls are safety-critical: if the API rejects a hang-up or a hold, the agent MUST be
 * told, otherwise they walk away from a call that is still connected.
 */
export const SOFTPHONE_MSG = {
  // Incoming call
  incomingCall: "Incoming call",

  // Call controls — failures only (a successful control is visible in the dock itself).
  answerFailed: "Couldn't answer the call",
  answerFailedDesc: "The call is still ringing — try again.",
  hangupFailed: "Couldn't hang up",
  hangupFailedDesc: "The call may still be connected — try again.",
  muteFailed: "Couldn't mute the call",
  unmuteFailed: "Couldn't unmute the call",
  holdFailed: "Couldn't put the call on hold",
  resumeFailed: "Couldn't take the call off hold",
  dtmfFailed: "Couldn't send that key",
  dtmfFailedDesc: "The tone wasn't sent to the caller — try again.",

  // Quick SMS
  smsSent: "SMS sent",
  smsFailed: "SMS not sent",
} as const;
