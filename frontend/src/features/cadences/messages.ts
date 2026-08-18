/**
 * Centralized user-facing copy for the Cadences feature.
 * Keep inline message strings out of the page — reference these instead.
 */
export const CADENCES_MSG = {
  // Cadence mutations
  cadenceUpdated: "Cadence updated",
  cadenceCreated: "Cadence created",
  saveCadenceFailed: "Couldn't save cadence",

  // Empty states
  noCadencesTitle: "No cadences yet",
  noCadencesDesc: "Build a multi-touch sequence to consistently engage leads over time.",
  noEnrollmentsTitle: "No enrollments yet",
  noEnrollmentsDesc: "Enroll a lead into a cadence to see live progress here.",
} as const;
