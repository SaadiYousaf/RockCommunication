/**
 * Centralized, user-facing copy for the Admin feature pages: toast titles/descriptions,
 * confirm-dialog copy, and full-sentence empty states. One home so the same wording isn't
 * duplicated across pages.
 *
 * Short UI labels (buttons, headers, placeholders) stay inline in the components. For generic
 * "please try again" fallbacks and read-only / permission copy, import the shared MESSAGES
 * helpers instead — they speak in plain language and never surface internal identifiers
 * (permission codes, raw role names, GUIDs) to end users.
 */
export const ADMIN_MSG = {
  /** Search placeholders across the admin pages (no user-facing copy inline). */
  search: {
    chatOversight: "Search rooms or people…",
    users: "Search by name, email, or role…",
    audit: "Search entity, user, changes, IP…",
    roles: "Search roles…",
  },
  /** Wording repeated across several admin pages — kept in one place. */
  common: {
    exportReady: "Export ready",
    exportReadyDesc: (n: number) => `${n} rows downloaded.`,
    saved: "Saved",
    createFailed: "Couldn't create",
    updateFailed: "Couldn't update",
    saveFailed: "Couldn't save",
    deactivateFailed: "Couldn't deactivate",
    reactivateFailed: "Couldn't reactivate",
    assignFailed: "Couldn't assign",
    resendInviteFailed: "Couldn't resend invitation",
    resetPasswordFailed: "Couldn't reset password",
    passwordReset: "Password reset",
    passwordResetDesc: (name: string) => `New password set for ${name}.`,
    invitationResent: "Invitation resent",
    invitationResentDesc: (name: string) => `A fresh temporary password was emailed to ${name}.`,
    emailedInvitation: "They've been emailed an invitation.",
    canNoLongerSignIn: (subject: string | number) => `${subject} can no longer sign in.`,
    checkFieldsAndTryAgain: "Check the fields and try again.",
  },

  // ── Role management (RolesPage) ──────────────────────────────────────────
  roles: {
    created: "Role created",
    createdDesc: (name: string) => `${name} is ready to assign.`,
    createFailed: "Could not create role",
    modulesSavedDesc: (name: string) => `Module access updated for ${name}.`,
    saveFailed: "Save failed",
    renamed: "Renamed",
    renameFailed: "Rename failed",
    deleted: "Role deleted",
    deleteFailed: "Delete failed",
    permissionsSaved: "Permissions saved",
    permissionsSavedDesc: "Role grants updated.",
    noMatchTitle: "No matching roles",
    noMatchDesc: "Try a different name.",
    emptyTitle: "No roles yet",
    emptyDesc: "Create one to get started.",
    pickRoleTitle: "Pick a role to manage",
    pickRoleDesc: "Select a role on the left to edit its module access and permissions.",
    /** Plain-language explainer for the permissions panel — no permission codes surfaced. */
    permissionsHint:
      "Tick the actions this role can perform. The actions that let a role add, edit, or delete data unlock the matching buttons across the app; without them, the role only has read-only access.",
  },

  // ── User management (UserManagementPage) ─────────────────────────────────
  userMgmt: {
    callCenterUpdated: "Call center updated",
    callCenterUpdateFailed: "Couldn't update call center",
    deactivateConfirmTitle: (n: number) => `Deactivate ${n} ${n === 1 ? "user" : "users"}?`,
    deactivateConfirmDesc: "They'll be blocked from signing in. You can reactivate them later.",
    deactivateConfirmLabel: "Deactivate",
    usersDeactivated: "Users deactivated",
    emptyTitle: "No users yet",
    emptyDesc: "Users will appear here once they're created.",
    userReactivated: "User reactivated",
    userReactivatedDesc: (name: string) => `${name} can sign in again.`,
    rolesUpdated: "Roles updated",
    rolesUpdatedDesc: (name: string, count: number) => `${name} now has ${count} role(s).`,
    rolesUpdateFailed: "Couldn't update roles",
    userDeactivated: "User deactivated",
  },

  // ── Submission agents (SubmissionAgentsPage) ─────────────────────────────
  submissionAgents: {
    deactivateConfirmTitle: (n: number) => `Deactivate ${n} ${n === 1 ? "agent" : "agents"}?`,
    deactivateConfirmDesc:
      "They'll be signed out and can no longer validate or approve sales. You can reactivate them later.",
    deactivateConfirmLabel: "Deactivate",
    agentsDeactivated: "Agents deactivated",
    agentReactivated: "Agent reactivated",
    agentReactivatedDesc: (name: string) => `${name} can sign in and validate again.`,
    noAgentsTitle: "No submission agents",
    noAgentsDesc: "Add one with the button above — they can approve sales for every agency.",
    agentDeactivated: "Agent deactivated",
    agentAdded: "Submission agent added",
    agentAddFailed: "Couldn't add submission agent",
    searchPlaceholder: "Search by name or email…",
    noMatchTitle: "No matching agents",
    noMatchDesc: "No submission agents match your search. Try a different name or email.",
  },

  // ── Agencies (AgenciesPage) ──────────────────────────────────────────────
  agencies: {
    ceoRequiredTitle: "CEO required",
    ceoRequiredDesc: "An Agency CEO name and email are required.",
    created: "Agency created",
    createdDesc: (name: string) => `${name} — the CEO has been emailed an invitation.`,
    updated: "Updated",
    disabled: "Disabled",
    enabled: "Enabled",
    disableConfirmTitle: (name: string) => `Disable ${name}?`,
    disableConfirmDesc:
      "Everyone in this agency is signed out and blocked from signing in until you re-enable it. Its leads, sales, and users are preserved.",
    disableConfirmLabel: "Disable agency",
    ceoAssigned: "CEO assigned",
    promoteFailed: "Couldn't promote",
    moveUserFailed: "Couldn't move user",
    userExistsTitle: "User already exists",
    userExistsDesc:
      "Someone with that username or email is already registered. Try a different one or use 'Pick existing user'.",
    invalidDetailsTitle: "Invalid details",
    invalidDetailsDesc: "Check the username and email format.",
    registerCeoFailed: "Couldn't register CEO",
    emptyTitle: "No agencies yet",
    emptyDesc: "Use the form above to create your first one.",
  },

  // ── Agency detail (AgencyDetailPage) ─────────────────────────────────────
  agencyDetail: {
    ccSaved: "Call centre saved",
    ccDisabledDesc: (name: string) => `${name} disabled — its agents are logged out`,
    ccCreated: "Call centre created",
    ccCreatedDesc: "The Call Center Admin has been emailed an invitation.",
    ccCreateFailed: "Couldn't create call centre",
    agentAdded: "License agent added",
    agentAddFailed: "Couldn't add license agent",
    noAgentsTitle: "No license agents yet",
    noAgentsDesc: "Add one with the button above — they'll be emailed an invitation.",
    noCcTitle: "No call centres yet",
    noCcDesc: "Add one with the button above — its admin is emailed an invitation.",
    noSalesTitle: "No sales",
    noSalesDesc: "This agency has no recorded sales yet.",

    // Customer-email branding (welcome email)
    brandingTitle: "Customer email branding",
    brandingSubtitle: "Shown on the welcome email a customer receives when their policy is approved.",
    senderEmailLabel: "Reply-to email",
    senderEmailHint: "Customer replies go here. The email is sent as your agency's name via our mail server.",
    senderEmailPlaceholder: "e.g. hello@youragency.com",
    logoLabel: "Agency logo",
    logoHint: "Shown at the top of the welcome email. PNG or JPG, under 2 MB.",
    uploadLogo: "Upload logo",
    changeLogo: "Change logo",
    logoUpdated: "Logo updated",
    logoTooLarge: "Please choose an image under 2 MB.",
    chooseImage: "Please choose an image file.",
    logoUploadFailed: "Couldn't upload the logo",
    brandingSaved: "Branding saved",
    brandingSaveFailed: "Couldn't save branding",
    noLogoYet: "No logo uploaded yet",

    // Money display (sales/commissions are stored in USD; payroll is PKR-native and untouched)
    currencyTitle: "Money display",
    currencySubtitle:
      "How sale and commission figures are shown across the app. Salary and payroll are handled separately in PKR and are not affected.",
    currencyLabel: "Show sale amounts in",
    rateLabel: "Exchange rate",
    rateHint: (code: string) => `How many ${code} equal 1 US dollar. Sales are stored in USD and converted for display only.`,
    rateNoConversion: "No conversion — figures are shown exactly as stored.",
    currencyPreview: (sample: string) => `A $400.00 sale will show as ${sample}.`,
    currencySaved: "Money display updated",
    currencySaveFailed: "Couldn't update the money display",
  },

  // ── Call centers list (CallCentersPage) ──────────────────────────────────
  callCenters: {
    adminRequiredTitle: "Admin required",
    adminRequiredDesc: "A Call Center Admin name and email are required.",
    agencyRequiredTitle: "Agency required",
    agencyRequiredDesc: "Choose which agency this call centre belongs to.",
    created: "Call center created",
    createdDesc: (name: string) => `${name} — the admin has been emailed an invitation.`,
    createFailedDesc: "Check the name and try again.",
    emptyTitle: "No call centers yet",
    emptyDesc: "Create one, then assign agents to it from User Management.",
    searchPlaceholder: "Search by name or code…",
    noMatchTitle: "No matching call centers",
    noMatchDesc: "No call centers match your search. Try a different name or code.",
  },

  // ── Call center detail (CallCenterDetailPage) ────────────────────────────
  callCenterDetail: {
    assignmentSaved: "Assignment saved",
    pinnedDesc: "Pinned to the call centre.",
    agencyWideDesc: "Set to agency-wide.",
    noStaffTitle: "No staff here",
    noStaffDescAgencyWide: "Every user is pinned to a call centre.",
    noStaffDesc:
      "No one is assigned to this call centre yet — pick it from a user's dropdown to move them here.",
  },

  // ── Audit log (AuditLogPage) ─────────────────────────────────────────────
  audit: {
    exportedRows: (n: number) => `Exported ${n} rows`,
    emptyTitle: "No audit entries",
    emptyDesc: "Try a different filter or date range.",
  },

  // ── Chat oversight (ChatOversightPage) ───────────────────────────────────
  chatOversight: {
    noAgenciesTitle: "No agencies",
    noAgenciesBody: "No agencies exist yet.",
    noCallCentersTitle: "No call centers with chats",
    noCallCentersBody: (agencyName: string) =>
      `No conversations grouped by call center in ${agencyName}. Use "View all chats" above.`,
    noRoomsMatch: "No rooms match your search.",
    noConversations: "No conversations here.",
    selectConversationTitle: "Select a conversation",
    selectConversationBody: "Pick a room on the left to read its full transcript.",
    noMessages: "This room has no messages yet.",
  },

  // ── Integrations (IntegrationsPage) ──────────────────────────────────────
  integrations: {
    checkOk: (code: string) => `${code} OK`,
    checkUnhealthy: (code: string) => `${code} unhealthy`,
    checkFailed: "Check failed",
    testCallPlaced: "Test call placed",
    testCallPlacedDesc: (provider: string, status: string) => `Provider ${provider} · ${status}`,
    testCallFailed: "Test call failed",
    emptyTitle: "No integrations configured",
    emptyDesc:
      "No external providers are registered yet. Configure them in appsettings.json, then restart the API.",
  },

  // ── System config (AdminPage) ────────────────────────────────────────────
  system: {
    ipAdded: "Entry added",
    ipAddFailed: "Couldn't add",
    ipEmptyTitle: "No entries — all IPs allowed",
    ipEmptyDesc: "Add at least one CIDR/IP to start enforcing allowlisting.",
    ipRemoveConfirmTitle: (cidrOrIp: string) => `Remove ${cidrOrIp} from the allowlist?`,
    ipRemoveConfirmDesc:
      "Anyone on that network is blocked from reaching the app immediately. If this is the last entry, every IP is allowed again.",
    ipRemoveConfirmLabel: "Remove entry",
    ipRemoved: "Entry removed",
    ipRemoveFailed: "Couldn't remove the entry",
    verticalCreated: "Vertical created",
    verticalsEmptyTitle: "No verticals yet",
    verticalsEmptyDesc: "Create the first one to tag leads and teams.",
    verticalEnabled: "Vertical enabled",
    verticalDisabled: "Vertical disabled",
    horizontalCreated: "Horizontal created",
    horizontalsEmptyTitle: "No horizontals yet",
    horizontalsEmptyDesc: "Create the first one to organise teams and campaigns across verticals.",
    horizontalEnabled: "Horizontal enabled",
    horizontalDisabled: "Horizontal disabled",
    ruleSaved: "Rule saved",
    ruleSaveFailed: "Couldn't save rule",
  },
} as const;
