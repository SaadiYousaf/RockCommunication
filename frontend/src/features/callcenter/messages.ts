/**
 * Centralized, user-facing copy for the Call Center feature. One home for the toast / confirm /
 * empty-state sentences shown across the agent panel, attendance, calls, DNC, scripts, campaigns
 * and supervisor pages, so the same wording isn't duplicated or drifting.
 *
 * RULE: never surface internal identifiers (permission codes, role names, GUIDs). Speak plainly about
 * what the user can/can't do; lean on the shared MESSAGES helpers for read-only / permission copy.
 */
export const CALLCENTER_MSG = {
  /** Lowercase noun for the "Couldn't load <x>" error state. */
  attendanceResourceName: "attendance",
  // Search placeholders (kept here so no user-facing copy lives inline).
  scriptsSearchPlaceholder: "Search scripts by name, content, stage, or role…",
  dncSearchPlaceholder: "Search by phone or reason…",
  agentSearchPlaceholder: "Search by agent name…",
  /** What failed to load, for the shared ErrorState ("Couldn't load call history"). */
  callsResourceName: "call history",
  // Shared across call-center pages
  exportReadyTitle: "Export ready",
  rowsDownloaded: (count: number) => `${count} rows downloaded.`,
  saveFailed: "Couldn't save",
  updateFailed: "Couldn't update",
  tryDifferentSearch: "Try a different search.",
  disableNameConfirmTitle: (name: string) => `Disable ${name}?`,
  disableLabel: "Disable",

  // AgentPanelPage
  statusUpdatedTitle: "Status updated",
  statusUpdatedBody: (status: string) => `You're now ${status}.`,
  sessionExpiredTitle: "Session expired",
  sessionExpiredBody: "Your shift was ended remotely. Please clock in again.",
  updateStatusFailed: "Couldn't update status",
  clockedInTitle: "Clocked in",
  clockedInBody: "Welcome back.",
  clockInFailed: "Clock-in failed",
  clockedOutTitle: "Clocked out",
  clockedOutBody: "Have a good one.",
  noActiveShiftTitle: "No active shift",
  noActiveShiftBody: "Your shift was already ended elsewhere.",
  clockOutFailed: "Clock-out failed",
  wrapUpSaved: "Wrap-up saved",
  saveWrapUpFailed: "Couldn't save wrap-up",
  offTheClockTitle: "You're off the clock",
  offTheClockBody: "Clock in to start receiving calls and tracking your shift.",
  noCallsTitle: "No calls yet",
  noCallsBody: "Calls will appear here once you start your shift.",

  // AttendancePage
  noAttendanceTitle: "No attendance in this range",
  noAttendanceBody: "Nobody clocked in during the selected dates. Try a wider range.",
  pickAgencyTitle: "Pick an agency",
  noAgenciesExist: "No agencies exist yet.",
  chooseAgencyToView: "Choose an agency above to view its attendance.",

  // CallsHistoryPage
  noCallsMatchTitle: "No calls match",
  noCallsMatchBody: "Try removing a filter or expanding the date range.",
  callsSearchPlaceholder: "Search by lead, phone, agent or wrap-up…",
  noCallsSearchMatchTitle: "No calls match your search",

  // DncPage
  removeDncConfirmTitle: (count: number) =>
    `Remove ${count} ${count === 1 ? "number" : "numbers"} from DNC?`,
  removeDncConfirmBody:
    "These numbers will be eligible for outbound dialing again. You can always re-add them later.",
  removeLabel: "Remove",
  removedFromDncCount: (count: number) => `Removed ${count} from DNC`,
  removeFailed: "Couldn't remove",
  addedToDncTitle: "Added to DNC",
  addDncFailed: "Couldn't add DNC entry",
  removedFromDnc: "Removed from DNC",
  noMatchingEntriesTitle: "No matching entries",
  noDncEntriesTitle: "No DNC entries",
  noDncEntriesBody: "Numbers added here will be blocked from outbound dialing.",

  // ScriptsPage
  saveScriptFailed: "Couldn't save script",
  scriptUpdated: "Script updated",
  scriptCreated: "Script created",
  disableScriptConfirmTitle: (name: string) => `Disable "${name}"?`,
  disableScriptConfirmBody:
    "Agents will no longer see this script in the dialer until you re-enable it.",
  scriptDisabled: "Script disabled",
  scriptEnabled: "Script enabled",
  noScriptsMatchTitle: "No scripts match",
  noScriptsTitle: "No scripts yet",
  noScriptsBody: "Create call scripts your agents can use during calls.",

  // CampaignsPage
  campaignSavedTitle: "Campaign saved",
  saveCampaignFailed: "Couldn't save campaign",
  disableCampaignConfirmBody:
    "The dialer will stop pulling leads from this campaign until you re-enable it.",
  campaignDisabled: "Campaign disabled",
  campaignEnabled: "Campaign enabled",
  noCampaignsTitle: "No campaigns yet",
  noCampaignsBody: "Create a campaign to group dialer activity and lead sources.",
  campaignSearchPlaceholder: "Search campaigns by code or name…",
  noCampaignsMatchTitle: "No campaigns match",
  leadSourceSavedTitle: "Lead source saved",
  noLeadSourcesTitle: "No lead sources",
  noLeadSourcesBody: "Track how leads enter your pipeline.",
  leadSourceSearchPlaceholder: "Search sources by code, name or campaign…",
  noLeadSourcesMatchTitle: "No lead sources match",
  skillSavedTitle: "Skill saved",
  disableSkillConfirmBody:
    "This skill will stop being used for skill-based call routing until you re-enable it.",
  skillDisabled: "Skill disabled",
  skillEnabled: "Skill enabled",
  noSkillsTitle: "No skills",
  noSkillsBody: "Add skills to enable skill-based routing.",
  skillSearchPlaceholder: "Search skills by code or name…",
  noSkillsMatchTitle: "No skills match",
  wrapUpCodeSavedTitle: "Wrap-up code saved",
  noWrapUpCodesTitle: "No wrap-up codes",
  noWrapUpCodesBody: "Add codes for agents to use after calls.",
  wrapUpSearchPlaceholder: "Search codes by code or label…",
  noWrapUpCodesMatchTitle: "No wrap-up codes match",

  // SupervisorPage
  forceApplied: (label: string) => `${label} applied`,
  agentMovedTo: (status: string) => `Agent moved to ${status}.`,
  forceFailed: (label: string) => `Couldn't ${label.toLowerCase()}`,
  coachStarted: (mode: string) => `${mode[0].toUpperCase() + mode.slice(1)} started`,
  connectedTo: (agentName: string) => `Connected to ${agentName}.`,
  startCoachingFailed: "Couldn't start coaching",
  forceLogoutConfirmTitle: (name: string) => `Log ${name} out?`,
  forceLogoutConfirmBody:
    "This ends their shift and forces them Offline. They'll have to clock in again before they can take calls.",
  forceLogoutLabel: "Force logout",
  noAgentsMatchTitle: "No agents match",
  noAgentsClockedInTitle: "No agents clocked in",
  noAgentsBody: "Live agent activity will appear here as your team starts shifts.",
} as const;
