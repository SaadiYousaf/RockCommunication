/**
 * Centralized, user-facing copy for the Team (org-chart) page: toast titles/descriptions,
 * confirm-dialog copy, and full-sentence empty/notice states. Short UI labels stay inline.
 * The read-only banner uses the shared MESSAGES.readOnly helper so no permission code leaks.
 */
export const TEAM_MSG = {
  movedTo: (label: string) => `Moved to ${label}`,
  removedFromTeam: "Removed from team",
  moveFailed: "Couldn't move user",
  leadAssigned: "Lead assigned",
  leadCleared: "Lead cleared",
  leadUpdateFailed: "Couldn't update lead",
  removeLeadTitle: "Remove team lead?",
  removeLeadDesc: (teamName: string) =>
    `Clear the team lead for ${teamName}? You can assign a new one at any time.`,
  removeLeadConfirm: "Remove",
  pickAgencyTitle: "Pick an agency",
  pickAgencyNoAgencies: "No agencies exist yet. Create one from the Agencies page to see its org chart.",
  pickAgencyBody: "Choose an agency from the selector above to view its organization chart.",
  loadFailedTitle: "Couldn't load the team",
  loadFailedBody: "Something went wrong fetching the organization chart. It may be a temporary issue.",
  noPeopleTitle: "No people yet",
  noPeopleBody: "This agency has no team members. Invite people from User Management and assign them to teams.",
} as const;
