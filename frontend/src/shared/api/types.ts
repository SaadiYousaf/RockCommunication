export interface UserSummary {
  id: string;
  userName: string;
  email: string;
  agencyId: string;
  roles: string[];
  modules: string[];
  /** True for accounts created with a temporary password — they must change it before using the app. */
  mustChangePassword?: boolean;
  /** False = deactivated account; login + active sessions are blocked. Defaults true on existing rows. */
  isActive?: boolean;
  teamId?: string | null;
  /** Call center the user is pinned to; null = agency-level (sees all call centers). */
  callCenterId?: string | null;
}

export interface CallCenterDto {
  id: string;
  name: string;
  code: string | null;
  isActive: boolean;
  leadCount: number;
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
}

export type VerifierStatusValue = "None" | "Verified" | "NotInterested" | "Dnc" | "Busy" | "CallBack" | "DeadAir";
export type CloserStatusValue = "None" | "CompleteAndSold" | "LostOnSocial" | "LostOnAccount" | "DncLead" | "NotInterestedCallback";
export type ValidatorStatusValue =
  | "Completed" | "Approved" | "ActivePaid" | "NoUpdateInCommission"
  | "BadBank" | "Nsf" | "Decline" | "ClientCancelled" | "ErrorInApplicationInformation";

export interface ValidatorQueueItem {
  saleId: string;
  leadId: string;
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

/** Payload for an upsert mutation: any subset of the entity; `id` absent/null means "create". */
export type Upsert<T> = Partial<Omit<T, "id">> & { id?: string | null };
