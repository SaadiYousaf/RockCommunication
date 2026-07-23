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
  | "BadBank" | "Nsf" | "Decline" | "ClientCancelled";

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
  dateOfBirth: string | null;
  stage: WorkflowStage;
  closerStatus: CloserStatusValue;
  application: (Partial<ClosingApplicationInput> & { closerStatus: CloserStatusValue; saleId: string | null }) | null;
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
