export interface UserSummary {
  id: string;
  userName: string;
  email: string;
  agencyId: string;
  /** The user's agency display name (null for SuperAdmin / central users with no agency). */
  agencyName?: string | null;
  roles: string[];
  modules: string[];
  /** True for accounts created with a temporary password — they must change it before using the app. */
  mustChangePassword?: boolean;
  /** True when the onboarding invitation lapsed (never accepted within 2 days) — login is refused
   * until an admin resends it. Distinguishes an "Expired" invite from a still-valid "Pending" one. */
  invitationExpired?: boolean;
  /** False = deactivated account; login + active sessions are blocked. Defaults true on existing rows. */
  isActive?: boolean;
  teamId?: string | null;
  /** Call center the user is pinned to; null = agency-level (sees all call centers). */
  callCenterId?: string | null;
  /** The user's call-center display name (null when agency-level / not pinned to one). */
  callCenterName?: string | null;
  /** True when a privileged user (SuperAdmin/Admin/CEO) must enrol in 2FA before using the app. */
  twoFactorSetupRequired?: boolean;
  /** When the onboarding invitation was accepted (null = still pending). */
  invitationAcceptedAt?: string | null;
  /** Currency the UI formats sale/commission money in ("USD" / "PKR") — an agency setting. */
  displayCurrency?: string;
  /** Units of displayCurrency per 1 USD (1 = show stored USD figures unchanged). */
  exchangeRate?: number;
}

export interface CallCenterDto {
  id: string;
  name: string;
  code: string | null;
  isActive: boolean;
  leadCount: number;
}

/** A stored insurance / carrier portal login (Confidential vault, Admin/SuperAdmin only). */
export interface PortalCredential {
  id: string;
  portalName: string;
  url?: string | null;
  username: string;
  /** True when a password is stored. The secret itself is fetched one at a time via reveal. */
  hasPassword: boolean;
  notes?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

/** The decrypted secret for a single credential (from the audited reveal endpoint). */
export interface RevealedCredential {
  id: string;
  password: string;
}
export interface PortalCredentialInput {
  portalName: string;
  url?: string | null;
  username: string;
  password: string;
  notes?: string | null;
}

/** HR employee roster row (no sensitive fields). */
export interface EmployeeListItem {
  id: string;
  fullName: string;
  agentCode: string;
  phoneNumber?: string | null;
  officialEmail?: string | null;
  designation: string;
  employmentStatus: string;
  callCenterId?: string | null;
  callCenterName?: string | null;
  dateOfJoining?: string | null;
  dateOfBirth?: string | null;
}

/** Full HR employee profile. */
export interface Employee {
  id: string;
  fullName: string;
  agentCode: string;
  phoneNumber?: string | null;
  officialEmail?: string | null;
  designation: string;
  callCenterId?: string | null;
  callCenterName?: string | null;
  userId?: string | null;
  cnic?: string | null;
  gender: string;
  maritalStatus: string;
  dateOfBirth?: string | null;
  guardianPhone?: string | null;
  guardianRelationship?: string | null;
  currentAddress?: string | null;
  permanentAddress?: string | null;
  dateOfJoining?: string | null;
  employmentStatus: string;
  reportingToEmployeeId?: string | null;
  reportingToName?: string | null;
  workHours?: string | null;
  bankName?: string | null;
  bankAccountTitle?: string | null;
  bankAccountNumber?: string | null;
  bankIban?: string | null;
  photoKey?: string | null;
  idCardFrontKey?: string | null;
  idCardBackKey?: string | null;
  createdAt: string;
}

/** A social-media handling report. */
export interface SocialMediaReport {
  id: string;
  date: string;
  employeeId?: string | null;
  employeeName?: string | null;
  platform?: string | null;
  postsMade: number;
  queriesAnswered: number;
  /** Optional link to the actual post / campaign (proof of the reported posting). */
  link?: string | null;
  notes?: string | null;
  createdAt: string;
}
export interface SocialMediaInput {
  date: string;
  employeeId?: string | null;
  platform?: string | null;
  postsMade: number;
  queriesAnswered: number;
  link?: string | null;
  notes?: string | null;
}

/** One item in the dashboard "Upcoming events" widget. */
export interface UpcomingEvent {
  /** "callback" | "training" | "birthday" — drives the icon/tone. */
  type: string;
  title: string;
  subtitle?: string | null;
  whenUtc: string;
}

/** A candidate with a training scheduled soon. */
export interface UpcomingTraining {
  id: string;
  candidateName: string;
  positionOffered?: string | null;
  trainingScheduledAt: string;
}

/** An employee whose birthday is coming up. */
export interface UpcomingBirthday {
  employeeId: string;
  fullName: string;
  agentCode: string;
  dateOfBirth: string;
  daysUntil: number;
  isToday: boolean;
}

/** One employee's payroll for a month, with computed totals. */
export interface PayrollConfig {
  callCenterId: string;
  callCenterName: string;
  /** Flat amount deducted per late-coming (PKR). */
  lateComingFine: number;
  /** Fraction of a day's pay deducted per half-day / absent / NCNS (a day's pay = basic / working days). */
  halfDayFactor: number;
  absentDayFactor: number;
  ncnsFactor: number;
  /** false = the defaults are shown (this centre hasn't saved its own rules yet). */
  saved: boolean;
}

export interface SavePayrollConfigInput {
  lateComingFine: number;
  halfDayFactor: number;
  absentDayFactor: number;
  ncnsFactor: number;
}

export interface PayrollRow {
  employeeId: string;
  fullName: string;
  agentCode: string;
  callCenterId?: string | null;
  callCenterName?: string | null;
  year: number;
  month: number;
  basicSalary: number;
  punctuality: number;
  dailyBonus: number;
  monthlyCommissions: number;
  transportAllowance: number;
  specialAllowance: number;
  advanceSalary: number;
  docks: number;
  workingDays: number;
  presentDays: number;
  leavesApproved: number;
  lateComing: number;
  halfDays: number;
  absentDays: number;
  ncns: number;
  /** Money withheld against each attendance-driven line (its day count is the "number"). */
  lateComingAmount: number;
  halfDaysAmount: number;
  absentDaysAmount: number;
  ncnsAmount: number;
  grossEarnings: number;
  deductions: number;
  netPay: number;
  notes?: string | null;
  finalized: boolean;
  saved: boolean;
}

/** Writable payroll fields. */
export interface SavePayrollInput {
  basicSalary: number;
  punctuality: number;
  dailyBonus: number;
  monthlyCommissions: number;
  transportAllowance: number;
  specialAllowance: number;
  advanceSalary: number;
  docks: number;
  workingDays: number;
  presentDays: number;
  leavesApproved: number;
  lateComing: number;
  halfDays: number;
  absentDays: number;
  ncns: number;
  lateComingAmount: number;
  halfDaysAmount: number;
  absentDaysAmount: number;
  ncnsAmount: number;
  notes?: string | null;
  finalized: boolean;
}

/** A recruitment / interview record. */
export interface Interview {
  id: string;
  interviewDate?: string | null;
  candidateName: string;
  cnic?: string | null;
  phoneNumber?: string | null;
  positionAppliedFor?: string | null;
  experience?: string | null;
  expectedSalary?: number | null;
  ableToJoinOn?: string | null;
  status: string;
  positionOffered?: string | null;
  salaryOffered?: number | null;
  remarks?: string | null;
  trainingScheduledAt?: string | null;
  createdAt: string;
}
export interface InterviewInput {
  interviewDate?: string | null;
  candidateName: string;
  cnic?: string | null;
  phoneNumber?: string | null;
  positionAppliedFor?: string | null;
  experience?: string | null;
  expectedSalary?: number | null;
  ableToJoinOn?: string | null;
  status: string;
  positionOffered?: string | null;
  salaryOffered?: number | null;
  remarks?: string | null;
  trainingScheduledAt?: string | null;
}

/** One employee's HR attendance mark for a chosen day (status null = unmarked). */
export interface HrAttendanceRow {
  employeeId: string;
  fullName: string;
  agentCode: string;
  designation: string;
  callCenterId?: string | null;
  callCenterName?: string | null;
  status?: string | null;
  notes?: string | null;
}

/** One employee's monthly HR attendance roll-up. */
export interface HrAttendanceSummaryRow {
  employeeId: string;
  fullName: string;
  agentCode: string;
  designation: string;
  callCenterId?: string | null;
  callCenterName?: string | null;
  present: number;
  absent: number;
  late: number;
  halfDay: number;
  leave: number;
  ncns: number;
  marked: number;
}

/** Writable employee fields (create + update). */
export interface EmployeeInput {
  fullName: string;
  agentCode: string;
  phoneNumber?: string | null;
  officialEmail?: string | null;
  designation: string;
  callCenterId?: string | null;
  userId?: string | null;
  cnic?: string | null;
  gender: string;
  maritalStatus: string;
  dateOfBirth?: string | null;
  guardianPhone?: string | null;
  guardianRelationship?: string | null;
  currentAddress?: string | null;
  permanentAddress?: string | null;
  dateOfJoining?: string | null;
  employmentStatus: string;
  reportingToEmployeeId?: string | null;
  workHours?: string | null;
  bankName?: string | null;
  bankAccountTitle?: string | null;
  bankAccountNumber?: string | null;
  bankIban?: string | null;
  photoKey?: string | null;
  idCardFrontKey?: string | null;
  idCardBackKey?: string | null;
}

// ---- Scheduler / meetings ---------------------------------------------------

export type MeetingStatus = "Scheduled" | "Cancelled" | "Completed";
export type AttendeeResponse = "Pending" | "Accepted" | "Declined" | "Tentative";

/** One invitee on a meeting, with their resolved display name and RSVP. */
export interface MeetingAttendee {
  id: string;
  /** The agency user (they can RSVP in-app), or null for an external email guest. */
  userId: string | null;
  email: string;
  userName: string | null;
  response: AttendeeResponse;
}

/** A meeting as seen by the current user. */
export interface Meeting {
  id: string;
  title: string;
  description?: string | null;
  /** UTC ISO instant. */
  startsAt: string;
  /** UTC ISO instant, always after startsAt. */
  endsAt: string;
  location?: string | null;
  onlineUrl?: string | null;
  organizerUserId: string;
  organizerName?: string | null;
  status: MeetingStatus;
  /** True when the current user owns (organizes) the meeting. */
  isOrganizer: boolean;
  /** The current user's own RSVP, or null when they're the organizer / not invited. */
  myResponse?: AttendeeResponse | null;
  attendees: MeetingAttendee[];
  createdAt: string;
}

/** Writable meeting fields (create + update). Times are UTC ISO instants. */
export interface MeetingInput {
  title: string;
  description?: string | null;
  startsAt: string;
  endsAt: string;
  location?: string | null;
  onlineUrl?: string | null;
  /** Agency users invited (they can RSVP in-app). */
  attendeeUserIds: string[];
  /** Free-text external email guests. */
  attendeeEmails: string[];
}

export interface AppModuleDto {
  id: string;
  code: string;
  name: string;
  group: string;
  routePath: string | null;
  icon: string | null;
  sortOrder: number;
  isSystem: boolean;
}

export interface RoleDto {
  id: string;
  name: string;
  isSystem: boolean;
  modules: string[];
  /** Null for system role templates; set for agency-scoped custom roles. */
  agencyId?: string | null;
}

export interface AgencyDto {
  id: string;
  name: string;
  code: string | null;
  isActive: boolean;
  ceoUserId: string | null;
  ceoUserName: string | null;
  userCount: number;
  createdAt: string;
  /** Reply-to address for customer mail (welcome email). Null = not configured. */
  senderEmail: string | null;
  /** True when a logo has been uploaded (fetch from /api/agencies/{id}/logo). */
  hasLogo: boolean;
  /** Currency the UI formats sale money in ("USD" / "PKR"). */
  displayCurrency: string;
  /** Units of displayCurrency per 1 USD (1 = no conversion). */
  exchangeRate: number;
}

// ---- Org tree (Team hierarchy page) -----------------------------------------

export interface OrgPersonDto {
  id: string;
  userName: string;
  email: string;
  displayName: string | null;
  roles: string[];
  isActive: boolean;
}

export interface OrgTeamDto {
  id: string;
  name: string;
  vertical: string | null;
  lead: OrgPersonDto | null;
  members: OrgPersonDto[];
}

export interface OrgTreeDto {
  agencyId: string;
  agencyName: string;
  ceo: OrgPersonDto | null;
  leadership: OrgPersonDto[];
  teams: OrgTeamDto[];
  unassigned: OrgPersonDto[];
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  requiresTwoFactor: boolean;
  twoFactorToken: string | null;
  user: UserSummary | null;
}

export interface TwoFactorSetup {
  secret: string;
  qrCodeUri: string;
}

export type WorkflowStage =
  | "New" | "Fronted" | "Verified" | "JrClosed" | "Closed"
  | "Validated" | "Funded" | "Followup" | "Winback" | "Lost";

export type LeadDisposition =
  | "None" | "Interested" | "NotInterested" | "CallBack" | "DoNotCall"
  | "Sold" | "NotQualified" | "Voicemail" | "NoAnswer" | "WrongNumber";

export interface Lead {
  id: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  email: string | null;
  state: string | null;
  stage: WorkflowStage;
  disposition: LeadDisposition;
  assignedUserId: string | null;
  teamId: string | null;
  jornayaVerified: boolean;
  createdAt: string;
}

export interface CreateLeadInput {
  firstName: string;
  lastName: string;
  phoneNumber: string;
  email?: string;
  address?: string;
  city?: string;
  state?: string;
  postalCode?: string;
  dateOfBirth?: string;
  source?: string;
  jornayaLeadId?: string;
}

export interface TimelineEntry {
  type: "StageChange" | "Call" | "Callback" | "Audit";
  at: string;
  actor: string | null;
  description: string | null;
  detail: Record<string, unknown> | null;
}

export interface LeadTimeline {
  leadId: string;
  name: string;
  stage: WorkflowStage;
  disposition: LeadDisposition;
  entries: TimelineEntry[];
}

export interface Sale {
  id: string;
  leadId: string;
  closerUserId: string;
  validatorUserId: string | null;
  carrier: string;
  policyNumber: string | null;
  monthlyPremium: number;
  annualPremium: number;
  soldAt: string;
  validatedAt: string | null;
  fundedAt: string | null;
  isInternalSale: boolean;
  internalSaleReason: string | null;
  bankingCode: number;
  bankName: string | null;
  bankAccountLast4: string | null;
  lyonsReference: string | null;
}

// ---- Intake pipeline (Fronter → Verifier → Closer) ----
export interface IntakeLeadInput {
  firstName: string;
  lastName: string;
  maritalStatus: string;
  createdDate: string;
  streetAddress: string;
  city: string;
  state: string;
  zipcode: string;
  phoneNumber: string;
  birthDate: string;
  ageYears: number;
  email: string;
  jornayaLeadId?: string;
  notes?: string;
}

export type VerifierStatusValue = "None" | "Verified" | "NotInterested" | "Dnc" | "Busy" | "CallBack" | "DeadAir";
export type CloserStatusValue = "None" | "CompleteAndSold" | "LostOnSocial" | "LostOnAccount" | "DncLead" | "NotInterestedCallback";
export type ValidatorStatusValue =
  | "Completed" | "Approved" | "ActivePaid" | "NoUpdateInCommission"
  | "BadBank" | "Nsf" | "Decline" | "ClientCancelled" | "ErrorInApplicationInformation";

export interface ValidatorQueueItem {
  saleId: string;
  leadId: string;
  agencyId: string;
  agencyName: string;
  leadName: string;
  leadPhone: string;
  state: string | null;
  carrier: string;
  policyNumber: string | null;
  monthlyPremium: number;
  closerUserId: string;
  closerName: string | null;
  status: ValidatorStatusValue;
  carrierApproved: string | null;
  coverageApproved: number | null;
  premiumApproved: number | null;
  planApproved: string | null;
  declineReason: string | null;
  validatorUserId: string | null;
  validatorName: string | null;
  licenseAgentUserId: string | null;
  licenseAgentName: string | null;
  soldAt: string;
  validatedAt: string | null;
}

export interface SetValidatorStatusInput {
  status: ValidatorStatusValue;
  carrierApproved?: string;
  coverageApproved?: number;
  premiumApproved?: number;
  planApproved?: string;
  declineReason?: string;
  licenseAgentUserId?: string | null;
}

/** Agency Panel + cross-agency Submission-Agent support. */
export interface AgencyOption {
  id: string;
  name: string;
  isActive: boolean;
}
export interface LicenseAgent {
  id: string;
  name: string;
  email: string;
  isActive: boolean;
}
export interface QueueCounts {
  myQueue: number;
  callbacks: number;
  verifierQueue: number;
  closerQueue: number;
  submissionQueue: number;
}

export interface AppNotification {
  id: string;
  title: string;
  body: string;
  url: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface SubmissionAgent {
  id: string;
  name: string;
  email: string;
  isActive: boolean;
  /** True until the agent accepts their invite (still on a temp password, not signed in / 2FA-enrolled). */
  pendingInvite: boolean;
}

export interface IntakeQueueItem {
  id: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  email: string | null;
  state: string | null;
  city: string | null;
  maritalStatus: string | null;
  ageYears: number | null;
  stage: WorkflowStage;
  verifierStatus: VerifierStatusValue;
  hasApplication: boolean;
  createdAt: string;
  score: number;
}

export interface ClosingApplicationInput {
  healthConditions?: string;
  gender: string;
  age: number;
  smokerStatus: string;
  name: string;
  dateOfBirth?: string;
  address: string;
  carrier: string;
  plan: string;
  faceAmount: number;
  premium: number;
  email: string;
  beneficiary: string;
  secondBeneficiary?: string;
  initialDraftDate?: string;
  futureDraftDate?: string;
  phoneNumber: string;
  altPhone?: string;
  primaryDoctor: string;
  social: string;
  bornIn: string;
  driversLicense: string;
  height: string;
  weight: string;
  accountType: string;
  bankName: string;
  accountNumber: string;
  routingNumber: string;
  /** Reason the closer is proceeding when Lyons flags the account (banking code 198). */
  banking198Reason?: string;
}

export interface ClosingApplicationView {
  leadId: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  email: string | null;
  address: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  maritalStatus: string | null;
  ageYears: number | null;
  jornayaLeadId: string | null;
  dateOfBirth: string | null;
  createdAt: string;
  stage: WorkflowStage;
  closerStatus: CloserStatusValue;
  application: (Partial<ClosingApplicationInput> & { closerStatus: CloserStatusValue; saleId: string | null }) | null;
}

/** Fields a verifier can edit on a queued lead. */
export interface UpdateIntakeLeadInput {
  firstName: string;
  lastName: string;
  maritalStatus?: string;
  streetAddress?: string;
  city?: string;
  state?: string;
  zipcode?: string;
  phoneNumber: string;
  birthDate?: string;
  ageYears?: number;
  email?: string;
  jornayaLeadId?: string;
}

export interface DocumentMeta {
  id: string;
  name: string;
  originalFileName: string;
  contentType: string;
  size: number;
  kind: "word" | "spreadsheet" | "other";
  uploadedByUserId: string;
  createdAt: string;
}

export interface DocumentNote {
  id: string;
  documentId: string;
  userId: string;
  body: string;
  createdAt: string;
  updatedAt: string | null;
}

export interface CommissionEntry {
  id: string;
  saleId: string;
  agentUserId: string;
  ruleName: string;
  amount: number;
  note: string | null;
  earnedAt: string;
  paid: boolean;
}

export interface PayrollRun {
  id: string;
  periodStart: string;
  periodEnd: string;
  totalAmount: number;
  status: string;
  processedAt: string | null;
}

export interface Callback {
  id: string;
  leadId: string;
  leadName: string;
  leadPhone: string;
  assignedUserId: string;
  scheduledFor: string;
  reason: string | null;
  completed: boolean;
}

export interface MetricCatalogItem {
  key: string;
  label: string;
  group: string | null;
}

export interface MetricValue {
  key: string;
  label: string;
  value: number;
  unit: string | null;
  group: string | null;
}

export interface RubricItem {
  id: string;
  label: string;
  maxScore: number;
  order: number;
}

export interface Rubric {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  items: RubricItem[];
}

export interface ChatRoomMember {
  userId: string;
  lastReadAt: string | null;
}

export interface ChatRoom {
  id: string;
  name: string;
  isDirect: boolean;
  memberUserIds: string[];
  members: ChatRoomMember[];
  /** Preview of the most recent message (or "📎 filename" for an attachment). */
  lastMessagePreview?: string | null;
  lastMessageAt?: string | null;
  lastMessageSenderId?: string | null;
}

/** One emoji reaction and everyone who applied it (client derives count + "mine"). */
export interface ChatReaction {
  emoji: string;
  userIds: string[];
}

export interface ChatMessage {
  id: string;
  roomId: string;
  senderUserId: string;
  body: string;
  sentAt: string;
  attachmentName?: string | null;
  attachmentContentType?: string | null;
  attachmentSize?: number | null;
  editedAt?: string | null;
  reactions?: ChatReaction[] | null;
}

export interface DashboardStageBucket {
  stage: WorkflowStage;
  count: number;
}

export interface DashboardActivityItem {
  leadId: string;
  leadName: string;
  fromStage: WorkflowStage;
  toStage: WorkflowStage;
  notes: string | null;
  disposition: LeadDisposition;
  occurredAt: string;
  userName: string | null;
}

export interface DashboardSummary {
  activeLeads: number;
  openCallbacks: number;
  salesThisWeek: number;
  conversionRate: number;
  leadsLast7Days: number;
  leadsPrior7Days: number;
  salesPrior7Days: number;
  pipeline: DashboardStageBucket[];
  recentActivity: DashboardActivityItem[];
}

/** Live work state for the "Team status" widget (mirrors backend TeamLiveStatus). */
export type TeamLiveStatus = "OnCall" | "Available" | "OnBreak" | "ClockedOut";

/** One employee in the "Team status" dashboard widget: today's attendance + live work state. */
export interface TeamStatusRow {
  employeeId: string;
  fullName: string;
  agentCode: string;
  designation: string;
  callCenterId?: string | null;
  callCenterName?: string | null;
  /** Today's attendance mark, or null when unmarked. */
  attendanceStatus?: string | null;
  liveStatus: TeamLiveStatus;
}

// ---- Lead troubleshooting ----------------------------------------------------

export interface LeadDiagnostics {
  lead: {
    id: string;
    name: string;
    phone: string;
    email: string | null;
    state: string | null;
    stage: WorkflowStage;
    disposition: LeadDisposition;
    score: number;
    createdAt: string;
    ageDays: number;
  };
  compliance: {
    onDnc: boolean;
    dncReason: string | null;
    dncExpiresAt: string | null;
    consentCaptured: boolean;
    tcpaWindowOk: boolean;
    tcpaNote: string | null;
  };
  jornaya: { verified: boolean; verifiedAt: string | null; leadId: string | null };
  assignment: {
    assigned: boolean;
    assignedUserId: string | null;
    assignedUserName: string | null;
    teamId: string | null;
    team: string | null;
    requiredSkill: string | null;
  };
  cadence: {
    activeEnrollments: number;
    enrollments: {
      enrollmentId: string;
      cadenceName: string;
      currentStep: number;
      totalSteps: number;
      nextRunAt: string;
      status: string;
    }[];
  };
  callActivity: {
    totalCalls: number;
    answeredCalls: number;
    unwrappedCalls: number;
    lastCallAt: string | null;
    lastWrapUp: string | null;
    recent: {
      id: string;
      initiatedAt: string;
      direction: string;
      status: string;
      agentName: string | null;
      wrapUpCode: string | null;
    }[];
  };
  workflows: {
    activeRules: { ruleId: string; name: string; eventType: string; active: boolean }[];
    recentExecutions: { startedAt: string; eventType: string; status: string; error: string | null }[];
  };
  issues: { severity: "error" | "warning" | "info"; code: string; message: string }[];
  recommendations: { action: string; why: string }[];
}

// ---- Integrations admin ------------------------------------------------------

export interface IntegrationField {
  label: string;
  value: string | null;
  masked: boolean;
}

export interface IntegrationInfo {
  code: string;
  name: string;
  provider: string;
  active: boolean;
  usingStub: boolean;
  mode: string;
  fields: IntegrationField[];
}

export interface IntegrationHealthResult {
  code: string;
  mode: string;
  healthy: boolean;
  message: string;
  elapsedMs: number;
}

// ---------------------------------------------------------------------------
// Call-center / workflow / QA list-endpoint response DTOs (mirror the backend
// records; enums serialize as string names). Added to replace `any[]` returns.
// ---------------------------------------------------------------------------

export type AgentStatus =
  | "Offline" | "Available" | "OnCall" | "Break" | "Lunch" | "Training" | "Meeting";

export interface CallListItem {
  id: string; leadId: string; leadName: string; leadPhone: string;
  agentUserId: string; agentName: string | null;
  provider: string; providerCallId: string;
  status: string; direction: string;
  initiatedAt: string; answeredAt: string | null; endedAt: string | null;
  talkSeconds: number | null; waitSeconds: number | null;
  recordingUrl: string | null; wrapUpCode: string | null;
}
export interface PagedCallsResult {
  items: CallListItem[]; total: number; skip: number; take: number;
  answeredCount: number; voicemailCount: number; abandonedCount: number; avgTalkSeconds: number;
}
export interface CallSummary {
  id: string; leadId: string; agentUserId: string;
  provider: string; providerCallId: string;
  status: string; direction: string;
  initiatedAt: string; answeredAt: string | null; endedAt: string | null;
  recordingUrl: string | null; wrapUpCode: string | null; notes: string | null;
  leadName: string | null; leadPhone: string | null;
}
export interface WrapUpCode { id: string; code: string; label: string; isSale: boolean; isContact: boolean; isRetry: boolean; isActive: boolean; }
export interface DncEntry { id: string; phoneNormalized: string; reason: string | null; source: string; expiresAt: string | null; }
export interface Campaign { id: string; code: string; name: string; verticalId: string | null; isActive: boolean; startsAt: string | null; endsAt: string | null; }
export interface LeadSource { id: string; code: string; name: string; campaignId: string | null; costPerLead: number; isActive: boolean; }
export interface Skill { id: string; code: string; name: string; isActive: boolean; }
export interface AgentSkill { skillId: string; code: string; name: string; proficiency: number; }
export interface Script { id: string; name: string; stage: WorkflowStage | null; role: string | null; campaignId: string | null; body: string; isActive: boolean; version: number; }
export interface LiveAgent { userId: string; userName: string; status: AgentStatus; reason: string | null; sinceAt: string; duration: string; currentCallId: string | null; currentCallStatus: string | null; }
export interface WorkflowAction { id?: string; actionType: string; parametersJson: string | null; order: number; }
export interface WorkflowRule { id: string; name: string; eventType: string; conditionJson: string | null; priority: number; isActive: boolean; continueOnError: boolean; description: string | null; actions: WorkflowAction[]; }
export interface WorkflowExecution { id: string; ruleId: string; eventType: string; status: string; startedAt: string; completedAt: string | null; error: string | null; }
export interface ImportBatch { id: string; leadListId: string; fileName: string; totalRows: number; imported: number; duplicates: number; dncScrubbed: number; errors: number; status: string; completedAt: string | null; }
export interface CadenceStep { id?: string; order: number; stepKind: string; delayMinutes: number; parametersJson: string | null; stopIfContacted: boolean; }
export interface Cadence { id: string; name: string; campaignId: string | null; isActive: boolean; description: string | null; steps: CadenceStep[]; }
export interface CadenceEnrollment { id: string; cadenceId: string; leadId: string; currentStepOrder: number; enrolledAt: string; nextRunAt: string; status: string; completedAt: string | null; stopReason: string | null; }
export interface VoicemailAsset { id: string; name: string; url: string; durationSeconds: number; campaignId: string | null; isActive: boolean; }
export interface InboundQueue { id: string; name: string; phoneNumber: string | null; requiredSkillCode: string | null; campaignId: string | null; strategy: string; maxWaitSeconds: number; overflowQueueId: string | null; voicemailAssetId: string | null; isActive: boolean; }
export interface AgentLeaderboard { userId: string; userName: string; callsToday: number; salesToday: number; premiumToday: number; leadsTransitionedToday: number; }
export interface KbArticle { id: string; slug: string; title: string; body: string; tags: string | null; category: string | null; isPublished: boolean; viewCount: number; publishedAt: string | null; }
export interface PublicEndpoint { id: string; slug: string; campaignId: string | null; leadSourceId: string | null; cadenceId: string | null; isActive: boolean; leadCount: number; allowedOrigins: string | null; }
export interface PublicEndpointWithSecret extends PublicEndpoint { secret: string; }
export interface QaReviewSummary { id: string; leadId: string; agentUserId: string; reviewerUserId: string; rubricId: string; totalScore: number; maxScore: number; percentage: number; notes: string | null; reviewedAt: string; }
export interface AgentScorecard { agentUserId: string; reviewCount: number; avgPercentage: number; avgScore: number; }
export interface TopAgent { userId: string; userName: string; sales: number; calls: number; premium: number | null; }
export interface WallboardSnapshot {
  agentsClockedIn: number; agentsAvailable: number; agentsOnCall: number; agentsOnBreak: number;
  callsAnsweredToday: number; callsAbandonedToday: number; leadsCreatedToday: number; salesClosedToday: number;
  callsWaitingNow: number; longestWaitSeconds: number; topAgentsToday: TopAgent[];
}

export interface LeadList { id: string; name: string; campaignId: string | null; leadSourceId: string | null; isActive: boolean; leadCount: number; }

/** SuperAdmin chat oversight — a room across any agency + its transcript. */
export interface ChatOversightRoom {
  id: string;
  name: string;
  isDirect: boolean;
  agencyName: string;
  members: string[];
  messageCount: number;
  lastMessageAt: string | null;
}
export interface ChatOversightMessage {
  id: string;
  sender: string;
  body: string;
  attachmentName: string | null;
  sentAt: string;
}
export interface OversightAgency { id: string; name: string; roomCount: number; }
export interface OversightCallCenter { id: string; name: string; roomCount: number; }

export interface AttendanceSegment {
  status: string; reason: string | null; fromAt: string; untilAt: string | null; minutes: number;
}
export interface AttendanceSession {
  id: string; clockInAt: string; clockOutAt: string | null;
  clockedMinutes: number; availableMinutes: number; onCallMinutes: number; breakMinutes: number;
  segments: AttendanceSegment[];
}
export interface AttendanceRow {
  userId: string; userName: string;
  clockedIn: boolean; currentStatus: string;
  sessionCount: number; firstClockIn: string | null; lastActivity: string | null;
  totalClockedMinutes: number; totalAvailableMinutes: number; totalOnCallMinutes: number; totalBreakMinutes: number;
  sessions: AttendanceSession[];
}

/** Payload for an upsert mutation: any subset of the entity; `id` absent/null means "create". */
export type Upsert<T> = Partial<Omit<T, "id">> & { id?: string | null };

// ── Pulse feed (team social timeline) ─────────────────────────────────────────
export interface FeedReactionSummary { emoji: string; count: number; mine: boolean; }
export interface FeedComment {
  id: string;
  authorUserId: string;
  authorName: string;
  body: string;
  createdAt: string;
  canDelete: boolean;
}
// ── Bug reports (in-app issue tracker) ────────────────────────────────────────
export interface BugReport {
  id: string;
  title: string;
  description: string;
  severity: string;   // BugSeverity name
  status: string;     // BugStatus name
  reporterUserId: string;
  reporterName: string;
  assignedToUserId: string | null;
  assignedToName: string | null;
  pageUrl: string | null;
  resolution: string | null;
  createdAt: string;
  updatedAt: string | null;
  /** True when the current user may triage (change status / assign). */
  canManage: boolean;
}
export interface BugActivity {
  id: string;
  userId: string;
  userName: string;
  fromStatus: string | null;
  toStatus: string | null;
  comment: string | null;
  createdAt: string;
}
export interface BugReportDetail {
  bug: BugReport;
  activity: BugActivity[];
}

export type FeedPostKind = "Update" | "Announcement" | "Praise" | "Question" | "Poll";
export interface FeedPollOption { id: string; text: string; votes: number; }
export interface FeedPoll {
  options: FeedPollOption[];
  totalVotes: number;
  /** The option the current user voted for, if any. */
  myOptionId: string | null;
}
export interface FeedPost {
  id: string;
  authorUserId: string;
  authorName: string;
  body: string;
  /** MS-Teams-style post kind. */
  kind: FeedPostKind;
  /** True when the post has an attached image (fetch it from /api/feed/{id}/image). */
  hasImage: boolean;
  createdAt: string;
  reactions: FeedReactionSummary[];
  comments: FeedComment[];
  /** Present only when kind === "Poll". */
  poll: FeedPoll | null;
  canDelete: boolean;
}

/** A policy sitting in the Retention worklist — a sale that went bad post-submission. */
export interface RetentionPolicy {
  saleId: string;
  saleNumber: number;
  leadId: string;
  leadName: string;
  leadPhone: string;
  state?: string | null;
  carrier: string;
  policyNumber?: string | null;
  monthlyPremium: number;
  closerUserId: string;
  closerName?: string | null;
  /** ValidatorStatus name, e.g. "BadBank" | "Nsf" | "ClientCancelled" | "Decline" | "ErrorInApplicationInformation". */
  status: string;
  declineReason?: string | null;
  soldAt: string;
  validatedAt?: string | null;
}

export interface RetentionResolveResult {
  saleId: string;
  status: string;
  leadStage: string;
}

// ---- Commission Desk (cross-agency financial role) --------------------------

/** One money line on a sale — editable once the sale is charged back (unpaid lines only). */
export interface CommissionAmount {
  id: string;
  ruleName: string;
  agentUserId: string;
  agentName: string | null;
  amount: number;
  paid: boolean;
  note: string | null;
}

/** A sale as shown on the cross-agency commission desk. */
export interface CommissionSale {
  saleId: string;
  saleNumber: number;
  leadId: string;
  customerName: string;
  phoneNumber: string;
  agencyId: string;
  agencyName: string;
  callCenterId: string | null;
  callCenterName: string | null;
  carrier: string;
  carrierApproved: string | null;
  policyNumber: string | null;
  monthlyPremium: number;
  coverageApproved: number | null;
  premiumApproved: number | null;
  planApproved: string | null;
  status: string;
  /** Sum of the sale's commission entries — negative once charged back. */
  fundedAmount: number;
  amounts: CommissionAmount[];
  /** Joined read-only from the global carrier advancing rules. */
  advanceRate: number | null;
  advancedMonths: number | null;
  expectedAdvance: number | null;
  soldAt: string;
  validatedAt: string | null;
  fundedAt: string | null;
  chargedBackAt: string | null;
}

export interface CommissionDeskResult {
  items: CommissionSale[];
  total: number;
  totalFunded: number;
  totalPremium: number;
}

/** A carrier's advancing terms (global — one rule per carrier name). */
export interface CarrierRule {
  id: string;
  carrier: string;
  commissionRate: number;
  advancedMonths: number;
  notes: string | null;
  isActive: boolean;
}

export interface CommissionDeskBreakdownRow {
  id: string;
  name: string;
  saleCount: number;
  premium: number;
  funded: number;
  expectedAdvance: number;
  chargedBackCount: number;
  chargedBackAmount: number;
}

export interface CommissionDeskDashboard {
  year: number;
  month: number;
  totalSales: number;
  totalPremium: number;
  totalFunded: number;
  totalExpectedAdvance: number;
  chargedBackCount: number;
  chargedBackAmount: number;
  byAgency: CommissionDeskBreakdownRow[];
  byCallCenter: CommissionDeskBreakdownRow[];
}
