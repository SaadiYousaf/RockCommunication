import { createApi, fetchBaseQuery, type BaseQueryFn, type FetchArgs, type FetchBaseQueryError } from "@reduxjs/toolkit/query/react";
import { setAuth, clearAuth } from "../../app/authSlice";
import type {
  LoginResponse, Lead, UserSummary, TwoFactorSetup,
  CreateLeadInput, LeadTimeline, Sale, CommissionEntry, PayrollRun,
  Callback, MetricCatalogItem, MetricValue, Rubric, ChatRoom, ChatMessage,
  WorkflowStage, LeadDisposition, DashboardSummary,
  AppModuleDto, RoleDto, AgencyDto, CallCenterDto, OrgTreeDto,
  LeadDiagnostics, IntegrationInfo, IntegrationHealthResult,
  DocumentMeta, DocumentNote,
  IntakeLeadInput, IntakeQueueItem, ClosingApplicationView, ClosingApplicationInput,
  ValidatorQueueItem, SetValidatorStatusInput,
} from "./types";

const API_URL = (import.meta as any).env?.VITE_API_URL ?? "http://localhost:5050";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: API_URL,
  prepareHeaders: (headers, { getState }) => {
    const token = (getState() as any).auth?.accessToken as string | null;
    if (token) headers.set("Authorization", `Bearer ${token}`);
    return headers;
  },
});

// Single shared refresh promise so concurrent 401s don't each fire their own
// `/api/auth/refresh` (which previously caused a storm visible in DevTools).
let inFlightRefresh: Promise<{ accessToken: string; refreshToken: string } | null> | null = null;
// Once we've decided the session is dead, stop trying to refresh until the user
// signs in again. Set on a failed refresh, reset by setAuth (login).
let sessionInvalid = false;

const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> = async (args, api, extra) => {
  let result = await rawBaseQuery(args, api, extra);
  if (result.error && result.error.status === 401) {
    // Backend signals "account deactivated" with a structured 401 body
    // (`{ status: 401, detail: "Your account has been deactivated…" }`).
    // Don't bother trying to refresh — the new access token would just be
    // rejected too. Nuke the local session and let the router send the user
    // to /login.
    const errBody = (result.error as any).data as { detail?: string; title?: string } | undefined;
    if (errBody && (
        errBody.title === "Account disabled" ||
        (typeof errBody.detail === "string" && /deactivated/i.test(errBody.detail))
    )) {
      sessionInvalid = true;
      api.dispatch(clearAuth());
      return result;
    }
    const refreshToken = (api.getState() as any).auth?.refreshToken;
    if (!refreshToken || sessionInvalid) {
      api.dispatch(clearAuth());
      return result;
    }

    // Coalesce: if a refresh is already in flight, await it instead of starting another.
    if (!inFlightRefresh) {
      inFlightRefresh = (async () => {
        const refresh = await rawBaseQuery({
          url: "/api/auth/refresh", method: "POST", body: { refreshToken },
        }, api, extra);
        const data = refresh.data as { accessToken: string; refreshToken: string } | undefined;
        return data ?? null;
      })().finally(() => { inFlightRefresh = null; });
    }

    const data = await inFlightRefresh;
    if (data) {
      sessionInvalid = false;
      const current = (api.getState() as any).auth;
      api.dispatch(setAuth({ ...current, accessToken: data.accessToken, refreshToken: data.refreshToken }));
      result = await rawBaseQuery(args, api, extra);
    } else {
      sessionInvalid = true;
      api.dispatch(clearAuth());
    }
  }
  return result;
};

// Reset the kill-switch when a fresh login lands.
export function markSessionRecovered() { sessionInvalid = false; }

export const baseApi = createApi({
  reducerPath: "api",
  baseQuery,
  tagTypes: ["Leads", "Lead", "Users", "Me", "Sales", "Commissions", "Callbacks", "Metrics", "Rubrics", "Rooms", "Messages", "Ip", "Verticals", "CommissionConfig", "Session", "WrapUpCodes", "Dnc", "Campaigns", "LeadSources", "Skills", "Scripts", "LiveAgents", "Calls", "Workflows", "WorkflowExecutions", "AiScore", "AiRecs", "Roles", "Modules", "LeadLists", "ImportBatches", "Cadences", "CadenceEnrollments", "Voicemails", "Queues", "Ivr", "KbArticles", "PublicEndpoints", "Wallboard", "Leaderboard", "Agencies", "Permissions", "RolePermissions", "Documents", "Horizontals", "VerifierQueue", "CloserQueue", "ClosingApp", "ValidatorQueue", "CallCenters"],
  endpoints: (b) => ({
    login: b.mutation<LoginResponse, { userNameOrEmail: string; password: string }>({
      query: (body) => ({ url: "/api/auth/login", method: "POST", body }),
    }),
    verify2Fa: b.mutation<LoginResponse, { twoFactorToken: string; code: string }>({
      query: (body) => ({ url: "/api/auth/2fa/verify", method: "POST", body }),
    }),
    setup2Fa: b.mutation<TwoFactorSetup, void>({
      query: () => ({ url: "/api/auth/2fa/setup", method: "POST" }),
    }),
    enable2Fa: b.mutation<void, { code: string }>({
      query: (body) => ({ url: "/api/auth/2fa/enable", method: "POST", body }),
    }),
    disable2Fa: b.mutation<void, void>({
      query: () => ({ url: "/api/auth/2fa", method: "DELETE" }),
    }),
    get2FaStatus: b.query<{ enabled: boolean; method: string | null }, void>({
      query: () => ({ url: "/api/auth/2fa/status" }),
    }),
    setTwoFactorMethod: b.mutation<void, { method: string }>({
      query: (body) => ({ url: "/api/auth/2fa/method", method: "PUT", body }),
    }),
    sendEmailOtp: b.mutation<void, void>({
      query: () => ({ url: "/api/auth/2fa/email/send-otp", method: "POST" }),
    }),
    forgotPassword: b.mutation<void, { email: string }>({
      query: (body) => ({ url: "/api/auth/forgot-password", method: "POST", body }),
    }),
    resetPassword: b.mutation<void, { email: string; token: string; newPassword: string }>({
      query: (body) => ({ url: "/api/auth/reset-password", method: "POST", body }),
    }),
    confirmEmail: b.mutation<void, { userId: string; token: string }>({
      query: (body) => ({ url: "/api/auth/email/confirm", method: "POST", body }),
    }),
    resendEmailConfirmation: b.mutation<void, { email: string }>({
      query: (body) => ({ url: "/api/auth/email/resend-confirmation", method: "POST", body }),
    }),
    me: b.query<UserSummary, void>({
      query: () => "/api/auth/me",
      providesTags: ["Me"],
    }),
    // `agencyId` is optional — admins/super-admins pass it to scope the list
    // to a specific tenant (e.g. the "Assign CEO" modal). Regular users have
    // their agency enforced server-side regardless of what they send.
    listUsers: b.query<UserSummary[], { agencyId?: string } | void>({
      query: (args) => ({
        url: "/api/users",
        params: args && "agencyId" in args && args.agencyId ? { agencyId: args.agencyId } : undefined,
      }),
      providesTags: ["Users"],
    }),
    userDirectory: b.query<{ id: string; userName: string }[], void>({
      query: () => "/api/users/directory",
      providesTags: ["Users"],
    }),

    listLeads: b.query<PagedLeadsResult, ListLeadsParams | void>({
      query: (params) => ({ url: "/api/leads", params: params ?? undefined }),
      providesTags: ["Leads"],
    }),
    bulkAssignLeads: b.mutation<BulkResult, { leadIds: string[]; assigneeUserId: string }>({
      query: (body) => ({ url: "/api/leads/bulk/assign", method: "POST", body }),
      invalidatesTags: ["Leads"],
    }),
    bulkSetStage: b.mutation<BulkResult, { leadIds: string[]; toStage: string; disposition: string; notes?: string }>({
      query: (body) => ({ url: "/api/leads/bulk/stage", method: "POST", body }),
      invalidatesTags: ["Leads"],
    }),
    bulkEnrollCadence: b.mutation<BulkResult, { leadIds: string[]; cadenceId: string }>({
      query: (body) => ({ url: "/api/leads/bulk/enroll-cadence", method: "POST", body }),
      invalidatesTags: ["Leads"],
    }),
    myLeads: b.query<Lead[], { stage?: WorkflowStage; take?: number } | void>({
      query: (params) => ({ url: "/api/leads/mine", params: params ?? undefined }),
      providesTags: ["Leads"],
    }),
    leadTimeline: b.query<LeadTimeline, string>({
      query: (id) => `/api/leads/${id}/timeline`,
      providesTags: (_r, _e, id) => [{ type: "Lead", id }],
    }),
    createLead: b.mutation<Lead, CreateLeadInput>({
      query: (body) => ({ url: "/api/leads", method: "POST", body }),
      invalidatesTags: ["Leads"],
    }),
    transitionLead: b.mutation<Lead, { id: string; toStage: WorkflowStage; disposition: LeadDisposition; notes?: string }>({
      query: ({ id, ...body }) => ({ url: `/api/leads/${id}/transition`, method: "POST", body }),
      invalidatesTags: (_r, _e, arg) => ["Leads", { type: "Lead", id: arg.id }],
    }),
    assignLead: b.mutation<Lead, { id: string; targetRole: string; strategy?: string; userId?: string }>({
      query: ({ id, ...body }) => ({ url: `/api/leads/${id}/assign`, method: "POST", body }),
      invalidatesTags: ["Leads"],
    }),
    verifyJornaya: b.mutation<Lead, string>({
      query: (id) => ({ url: `/api/integrations/jornaya/verify/${id}`, method: "POST" }),
      invalidatesTags: (_r, _e, id) => ["Leads", { type: "Lead", id }],
    }),
    dial: b.mutation<{ callId: string; status: string }, { leadId: string }>({
      query: (body) => ({ url: "/api/integrations/dialer/dial", method: "POST", body }),
    }),
    carriers: b.query<string[], void>({
      query: () => "/api/integrations/carriers",
    }),

    recordSale: b.mutation<Sale, { leadId: string; carrier: string; policyNumber?: string; monthlyPremium: number; routingNumber: string; accountNumber: string; accountType?: string; recordingKey?: string | null }>({
      query: (body) => ({ url: "/api/sales", method: "POST", body }),
      invalidatesTags: ["Leads", "Sales"],
    }),
    uploadSaleRecording: b.mutation<{ key: string; fileName: string; size: number }, File>({
      query: (file) => {
        const form = new FormData();
        form.append("file", file);
        return { url: "/api/sales/recording-upload", method: "POST", body: form };
      },
    }),
    validateSale: b.mutation<Sale, { id: string; approve: boolean; notes?: string }>({
      query: ({ id, ...body }) => ({ url: `/api/sales/${id}/validate`, method: "POST", body }),
      invalidatesTags: ["Leads", "Sales"],
    }),
    fundSale: b.mutation<Sale, string>({
      query: (id) => ({ url: `/api/sales/${id}/fund`, method: "POST" }),
      invalidatesTags: ["Leads", "Sales"],
    }),
    myCommissions: b.query<CommissionEntry[], { from?: string; to?: string; paid?: boolean } | void>({
      query: (params) => ({ url: "/api/sales/commissions", params: params ?? undefined }),
      providesTags: ["Commissions"],
    }),
    payrollRuns: b.query<PayrollRun[], void>({
      query: () => "/api/sales/payroll-runs",
    }),
    createPayrollRun: b.mutation<PayrollRun, { periodStart: string; periodEnd: string }>({
      query: (body) => ({ url: "/api/sales/payroll-runs", method: "POST", body }),
      invalidatesTags: ["Commissions"],
    }),

    myCallbacks: b.query<Callback[], { includeCompleted?: boolean } | void>({
      query: (params) => ({ url: "/api/callbacks/mine", params: params ?? undefined }),
      providesTags: ["Callbacks"],
    }),
    scheduleCallback: b.mutation<Callback, { leadId: string; scheduledFor: string; reason?: string }>({
      query: (body) => ({ url: "/api/callbacks", method: "POST", body }),
      invalidatesTags: ["Callbacks"],
    }),
    completeCallback: b.mutation<Callback, string>({
      query: (id) => ({ url: `/api/callbacks/${id}/complete`, method: "POST" }),
      invalidatesTags: ["Callbacks"],
    }),

    metricCatalog: b.query<MetricCatalogItem[], void>({
      query: () => "/api/dashboard/metrics",
    }),
    dashboard: b.query<MetricValue[], { from?: string; to?: string; metrics?: string[]; userId?: string } | void>({
      query: (params) => ({ url: "/api/dashboard", params: params ?? undefined }),
      providesTags: ["Metrics"],
    }),
    dashboardSummary: b.query<DashboardSummary, void>({
      query: () => "/api/dashboard/summary",
      providesTags: ["Metrics"],
    }),

    rubrics: b.query<Rubric[], void>({
      query: () => "/api/qa/rubrics",
      providesTags: ["Rubrics"],
    }),
    createRubric: b.mutation<Rubric, { name: string; description?: string; items: { label: string; maxScore: number; order: number }[] }>({
      query: (body) => ({ url: "/api/qa/rubrics", method: "POST", body }),
      invalidatesTags: ["Rubrics"],
    }),

    chatRooms: b.query<ChatRoom[], void>({
      query: () => "/api/chat/rooms",
      providesTags: ["Rooms"],
    }),
    createRoom: b.mutation<ChatRoom, { name: string; isDirect: boolean; memberUserIds: string[] }>({
      query: (body) => ({ url: "/api/chat/rooms", method: "POST", body }),
      invalidatesTags: ["Rooms"],
    }),
    startDirectMessage: b.mutation<ChatRoom, string>({
      query: (userId) => ({ url: `/api/chat/direct/${userId}`, method: "POST" }),
      invalidatesTags: ["Rooms"],
    }),

    // ===== Documents (protected viewer) =====
    listDocuments: b.query<DocumentMeta[], void>({
      query: () => "/api/documents",
      providesTags: ["Documents"],
    }),
    uploadDocument: b.mutation<DocumentMeta, { name: string; file: File }>({
      query: ({ name, file }) => {
        const form = new FormData();
        if (name) form.append("name", name);
        form.append("file", file);
        return { url: "/api/documents/upload", method: "POST", body: form };
      },
      invalidatesTags: ["Documents"],
    }),
    deleteDocument: b.mutation<void, string>({
      query: (id) => ({ url: `/api/documents/${id}`, method: "DELETE" }),
      invalidatesTags: ["Documents"],
    }),
    documentNotes: b.query<DocumentNote[], string>({
      query: (id) => `/api/documents/${id}/notes`,
      providesTags: (_r, _e, id) => [{ type: "Documents", id: `notes-${id}` }],
    }),
    addDocumentNote: b.mutation<DocumentNote, { id: string; body: string }>({
      query: ({ id, body }) => ({ url: `/api/documents/${id}/notes`, method: "POST", body: { body } }),
      invalidatesTags: (_r, _e, arg) => [{ type: "Documents", id: `notes-${arg.id}` }],
    }),
    roomMessages: b.query<ChatMessage[], { roomId: string; take?: number }>({
      query: ({ roomId, take = 50 }) => ({ url: `/api/chat/rooms/${roomId}/messages`, params: { take } }),
      providesTags: (_r, _e, arg) => [{ type: "Messages", id: arg.roomId }],
    }),
    sendMessage: b.mutation<ChatMessage, { roomId: string; body: string }>({
      query: ({ roomId, body }) => ({ url: `/api/chat/rooms/${roomId}/messages`, method: "POST", body: { body } }),
      invalidatesTags: (_r, _e, arg) => [{ type: "Messages", id: arg.roomId }],
    }),
    sendAttachment: b.mutation<ChatMessage, { roomId: string; body?: string; file: File }>({
      query: ({ roomId, body, file }) => {
        const form = new FormData();
        if (body) form.append("body", body);
        form.append("file", file);
        return { url: `/api/chat/rooms/${roomId}/messages/upload`, method: "POST", body: form };
      },
      invalidatesTags: (_r, _e, arg) => [{ type: "Messages", id: arg.roomId }],
    }),
    chatUnread: b.query<{ roomId: string; unreadCount: number; lastReadAt: string | null }[], void>({
      query: () => "/api/chat/unread",
      providesTags: ["Rooms"],
    }),
    markRoomRead: b.mutation<void, string>({
      query: (id) => ({ url: `/api/chat/rooms/${id}/read`, method: "POST" }),
      invalidatesTags: ["Rooms"],
    }),

    // Admin
    listIpAllowlist: b.query<{ id: string; cidrOrIp: string; note: string | null }[], void>({
      query: () => "/api/admin/ip-allowlist",
      providesTags: ["Ip"],
    }),
    addIpAllowlist: b.mutation<{ id: string; cidrOrIp: string; note: string | null }, { cidrOrIp: string; note?: string }>({
      query: (body) => ({ url: "/api/admin/ip-allowlist", method: "POST", body }),
      invalidatesTags: ["Ip"],
    }),
    removeIpAllowlist: b.mutation<void, string>({
      query: (id) => ({ url: `/api/admin/ip-allowlist/${id}`, method: "DELETE" }),
      invalidatesTags: ["Ip"],
    }),
    listVerticals: b.query<{ id: string; name: string; description: string | null; isActive: boolean }[], void>({
      query: () => "/api/admin/verticals",
      providesTags: ["Verticals"],
    }),
    createVertical: b.mutation<any, { name: string; description?: string }>({
      query: (body) => ({ url: "/api/admin/verticals", method: "POST", body }),
      invalidatesTags: ["Verticals"],
    }),
    updateVertical: b.mutation<any, { id: string; name: string; description?: string; isActive: boolean }>({
      query: ({ id, ...body }) => ({ url: `/api/admin/verticals/${id}`, method: "PUT", body }),
      invalidatesTags: ["Verticals"],
    }),
    listHorizontals: b.query<{ id: string; name: string; description: string | null; isActive: boolean }[], void>({
      query: () => "/api/admin/horizontals",
      providesTags: ["Horizontals"],
    }),
    createHorizontal: b.mutation<any, { name: string; description?: string }>({
      query: (body) => ({ url: "/api/admin/horizontals", method: "POST", body }),
      invalidatesTags: ["Horizontals"],
    }),
    updateHorizontal: b.mutation<any, { id: string; name: string; description?: string; isActive: boolean }>({
      query: ({ id, ...body }) => ({ url: `/api/admin/horizontals/${id}`, method: "PUT", body }),
      invalidatesTags: ["Horizontals"],
    }),

    // ---- Call centers (sub-agency data-isolation unit) ----
    listCallCenters: b.query<CallCenterDto[], void>({
      query: () => "/api/admin/call-centers",
      providesTags: ["CallCenters"],
    }),
    createCallCenter: b.mutation<CallCenterDto, { name: string; code?: string | null }>({
      query: (body) => ({ url: "/api/admin/call-centers", method: "POST", body }),
      invalidatesTags: ["CallCenters"],
    }),
    updateCallCenter: b.mutation<CallCenterDto, { id: string; name: string; code?: string | null; isActive: boolean }>({
      query: ({ id, ...body }) => ({ url: `/api/admin/call-centers/${id}`, method: "PUT", body }),
      invalidatesTags: ["CallCenters"],
    }),
    setUserCallCenter: b.mutation<UserSummary, { userId: string; callCenterId: string | null }>({
      query: ({ userId, callCenterId }) => ({ url: `/api/admin/users/${userId}/call-center`, method: "PUT", body: { callCenterId } }),
      invalidatesTags: ["Users"],
    }),

    // ---- Intake pipeline (Fronter → Verifier → Closer) ----
    captureIntakeLead: b.mutation<{ leadId: string; firstName: string; lastName: string; stage: string }, IntakeLeadInput>({
      query: (body) => ({ url: "/api/intake/leads", method: "POST", body }),
      invalidatesTags: ["VerifierQueue", "Leads"],
    }),
    verifierQueue: b.query<IntakeQueueItem[], void>({
      query: () => "/api/intake/verify/queue",
      providesTags: ["VerifierQueue"],
    }),
    setVerifierStatus: b.mutation<{ leadId: string; status: string; stage: string }, { leadId: string; status: string; notes?: string; callbackAt?: string }>({
      query: ({ leadId, ...body }) => ({ url: `/api/intake/verify/${leadId}/status`, method: "POST", body }),
      invalidatesTags: ["VerifierQueue", "CloserQueue"],
    }),
    closerQueue: b.query<IntakeQueueItem[], void>({
      query: () => "/api/intake/close/queue",
      providesTags: ["CloserQueue"],
    }),
    getClosingApplication: b.query<ClosingApplicationView, string>({
      query: (leadId) => `/api/intake/close/${leadId}`,
      providesTags: (_r, _e, id) => [{ type: "ClosingApp", id }],
    }),
    submitClosingApplication: b.mutation<{ leadId: string; status: string; stage: string; saleId: string | null }, { leadId: string; status: string; application: ClosingApplicationInput }>({
      query: ({ leadId, ...body }) => ({ url: `/api/intake/close/${leadId}`, method: "POST", body }),
      invalidatesTags: (_r, _e, arg) => ["CloserQueue", "Sales", "Leads", "Commissions", "ValidatorQueue", { type: "ClosingApp", id: arg.leadId }],
    }),
    // Closer adds a lead straight into the Closer queue (skips fronter/verifier).
    captureCloserLead: b.mutation<{ leadId: string; firstName: string; lastName: string; stage: string }, IntakeLeadInput>({
      query: (body) => ({ url: "/api/intake/close/leads", method: "POST", body }),
      invalidatesTags: ["CloserQueue", "Leads"],
    }),
    // ---- Validator queue ----
    validatorQueue: b.query<ValidatorQueueItem[], void>({
      query: () => "/api/intake/validate/queue",
      providesTags: ["ValidatorQueue"],
    }),
    setValidatorStatus: b.mutation<{ saleId: string; status: string; leadStage: string }, { saleId: string } & SetValidatorStatusInput>({
      query: ({ saleId, ...body }) => ({ url: `/api/intake/validate/${saleId}/status`, method: "POST", body }),
      invalidatesTags: ["ValidatorQueue", "Sales", "Leads"],
    }),
    listCommissionConfig: b.query<{ ruleName: string; amount: number | null; threshold: number | null; enabled: boolean }[], void>({
      query: () => "/api/admin/commission-config",
      providesTags: ["CommissionConfig"],
    }),
    upsertCommissionConfig: b.mutation<any, { ruleName: string; amount: number | null; threshold: number | null; enabled: boolean }>({
      query: (body) => ({ url: "/api/admin/commission-config", method: "PUT", body }),
      invalidatesTags: ["CommissionConfig"],
    }),
    updateUserRoles: b.mutation<UserSummary, { id: string; roles: string[] }>({
      query: ({ id, roles }) => ({ url: `/api/admin/users/${id}/roles`, method: "PUT", body: { roles } }),
      invalidatesTags: ["Users"],
    }),
    setUserActive: b.mutation<UserSummary, { id: string; isActive: boolean }>({
      query: ({ id, isActive }) => ({ url: `/api/admin/users/${id}/active`, method: "PUT", body: { isActive } }),
      invalidatesTags: ["Users"],
    }),
    resetUserPassword: b.mutation<void, { id: string; newPassword: string }>({
      query: ({ id, newPassword }) => ({ url: `/api/admin/users/${id}/password`, method: "PUT", body: { newPassword } }),
    }),

    // Lead search/dedup
    searchLeads: b.query<Lead[], { phone?: string; email?: string; name?: string; take?: number }>({
      query: (params) => ({ url: "/api/leads/search", params }),
    }),
    duplicateLeads: b.query<{ key: string; leads: Lead[] }[], void>({
      query: () => "/api/leads/duplicates",
    }),

    // Self-validate
    selfValidateSale: b.mutation<Sale, { id: string; notes?: string }>({
      query: ({ id, notes }) => ({ url: `/api/sales/${id}/self-validate`, method: "POST", body: { notes } }),
      invalidatesTags: ["Sales", "Leads"],
    }),

    // QA browser
    listQaReviews: b.query<any[], { agentUserId?: string; from?: string; to?: string }>({
      query: (params) => ({ url: "/api/qa/reviews", params }),
    }),
    qaScorecards: b.query<any[], { from?: string; to?: string }>({
      query: (params) => ({ url: "/api/qa/scorecards", params }),
    }),

    // ===== Call center: agent session =====
    mySession: b.query<any, void>({
      query: () => "/api/cc/session",
      providesTags: ["Session"],
    }),
    clockIn: b.mutation<any, void>({
      query: () => ({ url: "/api/cc/clock-in", method: "POST" }),
      invalidatesTags: ["Session"],
    }),
    clockOut: b.mutation<any, void>({
      query: () => ({ url: "/api/cc/clock-out", method: "POST" }),
      invalidatesTags: ["Session"],
    }),
    setAgentStatus: b.mutation<any, { status: string; reason?: string }>({
      query: (body) => ({ url: "/api/cc/status", method: "POST", body }),
      invalidatesTags: ["Session", "LiveAgents"],
    }),
    wrapUpCall: b.mutation<void, { callId: string; wrapUpCode: string; notes?: string }>({
      query: ({ callId, ...body }) => ({ url: `/api/cc/calls/${callId}/wrap-up`, method: "POST", body }),
      invalidatesTags: ["Session", "Calls"],
    }),
    myRecentCalls: b.query<any[], number | void>({
      query: (take) => ({ url: "/api/cc/calls/recent", params: take ? { take } : undefined }),
      providesTags: ["Calls"],
    }),

    // ===== Wrap-up codes =====
    listWrapUpCodes: b.query<any[], void>({
      query: () => "/api/cc/wrap-up-codes",
      providesTags: ["WrapUpCodes"],
    }),
    upsertWrapUpCode: b.mutation<any, { id?: string; code: string; label: string; isSale: boolean; isContact: boolean; isRetry: boolean; isActive: boolean }>({
      query: (body) => ({ url: "/api/cc/wrap-up-codes", method: "PUT", body }),
      invalidatesTags: ["WrapUpCodes"],
    }),

    // ===== DNC =====
    listDnc: b.query<any[], { skip?: number; take?: number } | void>({
      query: (params) => ({ url: "/api/cc/dnc", params: params ?? undefined }),
      providesTags: ["Dnc"],
    }),
    addDnc: b.mutation<any, { phone: string; reason?: string; source?: string; expiresAt?: string }>({
      query: (body) => ({ url: "/api/cc/dnc", method: "POST", body }),
      invalidatesTags: ["Dnc"],
    }),
    removeDnc: b.mutation<void, string>({
      query: (id) => ({ url: `/api/cc/dnc/${id}`, method: "DELETE" }),
      invalidatesTags: ["Dnc"],
    }),

    // ===== Compliance pre-flight =====
    checkCompliance: b.mutation<{ allowed: boolean; blockReason: string | null; warnings: string[] }, { phone: string; state?: string }>({
      query: (body) => ({ url: "/api/cc/compliance/check", method: "POST", body }),
    }),

    // ===== Campaigns / LeadSources / Skills / Scripts =====
    listCampaigns: b.query<any[], void>({
      query: () => "/api/cc/campaigns",
      providesTags: ["Campaigns"],
    }),
    upsertCampaign: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/campaigns", method: "PUT", body }),
      invalidatesTags: ["Campaigns"],
    }),
    listLeadSources: b.query<any[], void>({
      query: () => "/api/cc/lead-sources",
      providesTags: ["LeadSources"],
    }),
    upsertLeadSource: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/lead-sources", method: "PUT", body }),
      invalidatesTags: ["LeadSources"],
    }),
    listSkills: b.query<any[], void>({
      query: () => "/api/cc/skills",
      providesTags: ["Skills"],
    }),
    upsertSkill: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/skills", method: "PUT", body }),
      invalidatesTags: ["Skills"],
    }),
    assignAgentSkill: b.mutation<void, { userId: string; skillId: string; proficiency: number }>({
      query: (body) => ({ url: "/api/cc/skills/assign", method: "POST", body }),
      invalidatesTags: ["Skills"],
    }),
    removeAgentSkill: b.mutation<void, { userId: string; skillId: string }>({
      query: ({ userId, skillId }) => ({ url: "/api/cc/skills/assign", method: "DELETE", params: { userId, skillId } }),
      invalidatesTags: ["Skills"],
    }),
    agentSkills: b.query<any[], string>({
      query: (id) => `/api/cc/agents/${id}/skills`,
      providesTags: ["Skills"],
    }),
    listScripts: b.query<any[], { stage?: string; role?: string; campaignId?: string } | void>({
      query: (params) => ({ url: "/api/cc/scripts", params: params ?? undefined }),
      providesTags: ["Scripts"],
    }),
    upsertScript: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/scripts", method: "PUT", body }),
      invalidatesTags: ["Scripts"],
    }),

    // ===== Supervisor =====
    liveAgents: b.query<any[], void>({
      query: () => "/api/cc/supervisor/live",
      providesTags: ["LiveAgents"],
    }),
    forceAgentStatus: b.mutation<void, { id: string; status: string; reason?: string }>({
      query: ({ id, ...body }) => ({ url: `/api/cc/supervisor/agents/${id}/force-status`, method: "POST", body }),
      invalidatesTags: ["LiveAgents"],
    }),
    coachAgent: b.mutation<void, { id: string; mode: string }>({
      query: ({ id, mode }) => ({ url: `/api/cc/supervisor/agents/${id}/coach`, method: "POST", body: { mode } }),
    }),

    // ===== Workflows =====
    listWorkflowRules: b.query<any[], string | void>({
      query: (eventType) => ({ url: "/api/workflows/rules", params: eventType ? { eventType } : undefined }),
      providesTags: ["Workflows"],
    }),
    upsertWorkflowRule: b.mutation<any, any>({
      query: (body) => ({ url: "/api/workflows/rules", method: "PUT", body }),
      invalidatesTags: ["Workflows"],
    }),
    deleteWorkflowRule: b.mutation<void, string>({
      query: (id) => ({ url: `/api/workflows/rules/${id}`, method: "DELETE" }),
      invalidatesTags: ["Workflows"],
    }),
    workflowEventTypes: b.query<string[], void>({ query: () => "/api/workflows/event-types" }),
    workflowActionTypes: b.query<string[], void>({ query: () => "/api/workflows/action-types" }),
    workflowExecutions: b.query<any[], { ruleId?: string; take?: number } | void>({
      query: (params) => ({ url: "/api/workflows/executions", params: params ?? undefined }),
      providesTags: ["WorkflowExecutions"],
    }),

    // ===== Lead scoring =====
    rescoreLead: b.mutation<any, string>({
      query: (id) => ({ url: `/api/leads/${id}/rescore`, method: "POST" }),
      invalidatesTags: (_r, _e, id) => [{ type: "Lead", id }],
    }),

    // ===== AI =====
    aiRecommendations: b.query<{ items: { action: string; reason: string; confidence: number }[] }, string>({
      query: (id) => `/api/ai/leads/${id}/recommendations`,
      providesTags: (_r, _e, id) => [{ type: "AiRecs", id }],
    }),
    aiLeadScore: b.query<{ score: number; reasoning: string; riskFactors: string[] }, string>({
      query: (id) => `/api/ai/leads/${id}/score`,
      providesTags: (_r, _e, id) => [{ type: "AiScore", id }],
    }),
    aiCallSummary: b.mutation<{ summary: string; keyMoments: string[]; recommendedDisposition: string | null }, string>({
      query: (callId) => ({ url: `/api/ai/calls/${callId}/summary`, method: "POST" }),
    }),

    // ===== Lead troubleshooting =====
    leadDiagnostics: b.query<LeadDiagnostics, string>({
      query: (id) => `/api/leads/${id}/diagnostics`,
      providesTags: (_r, _e, id) => [{ type: "Lead", id }],
    }),

    // ===== Integrations admin =====
    listIntegrations: b.query<IntegrationInfo[], void>({
      query: () => "/api/admin/integrations",
    }),
    checkIntegration: b.mutation<IntegrationHealthResult, string>({
      query: (code) => ({ url: `/api/admin/integrations/${code}/check`, method: "POST" }),
    }),

    // ===== RBAC: Roles & Modules =====
    listRoles: b.query<RoleDto[], void>({
      query: () => "/api/roles",
      providesTags: ["Roles"],
    }),
    getRole: b.query<RoleDto, string>({
      query: (id) => `/api/roles/${id}`,
      providesTags: (_r, _e, id) => [{ type: "Roles", id }],
    }),
    createRole: b.mutation<RoleDto, { name: string; moduleCodes: string[] }>({
      query: (body) => ({ url: "/api/roles", method: "POST", body }),
      invalidatesTags: ["Roles"],
    }),
    renameRole: b.mutation<RoleDto, { id: string; name: string }>({
      query: ({ id, name }) => ({ url: `/api/roles/${id}/name`, method: "PUT", body: { name } }),
      invalidatesTags: ["Roles"],
    }),
    setRoleModules: b.mutation<RoleDto, { id: string; moduleCodes: string[] }>({
      query: ({ id, moduleCodes }) => ({ url: `/api/roles/${id}/modules`, method: "PUT", body: { moduleCodes } }),
      invalidatesTags: ["Roles", "Me"],
    }),
    deleteRole: b.mutation<void, string>({
      query: (id) => ({ url: `/api/roles/${id}`, method: "DELETE" }),
      invalidatesTags: ["Roles"],
    }),
    listModules: b.query<AppModuleDto[], void>({
      query: () => "/api/modules",
      providesTags: ["Modules"],
    }),
    myModules: b.query<string[], void>({
      query: () => "/api/modules/mine",
      providesTags: ["Me"],
    }),

    // ===== Agencies (Call Centers) — SuperAdmin only =====
    listAgencies: b.query<AgencyDto[], { includeInactive?: boolean } | void>({
      query: (p) => ({ url: "/api/agencies", params: p ?? undefined }),
      providesTags: ["Agencies"],
    }),
    getAgency: b.query<AgencyDto, string>({
      query: (id) => `/api/agencies/${id}`,
      providesTags: (_r, _e, id) => [{ type: "Agencies", id }],
    }),
    createAgency: b.mutation<AgencyDto, { name: string; code?: string | null }>({
      query: (body) => ({ url: "/api/agencies", method: "POST", body }),
      invalidatesTags: ["Agencies"],
    }),
    updateAgency: b.mutation<AgencyDto, { id: string; name: string; code?: string | null; isActive: boolean }>({
      query: ({ id, ...body }) => ({ url: `/api/agencies/${id}`, method: "PUT", body }),
      invalidatesTags: ["Agencies"],
    }),
    assignAgencyCeo: b.mutation<AgencyDto, { id: string; userId: string }>({
      query: ({ id, userId }) => ({ url: `/api/agencies/${id}/assign-ceo`, method: "POST", body: { userId } }),
      invalidatesTags: ["Agencies", "Users"],
    }),

    // ===== Org tree (Team hierarchy page) =====
    orgTree: b.query<OrgTreeDto, { agencyId?: string } | void>({
      query: (p) => ({ url: "/api/org/tree", params: p ?? undefined }),
      providesTags: ["Users", "Agencies"],
    }),
    setUserTeam: b.mutation<UserSummary, { userId: string; teamId: string | null }>({
      query: ({ userId, teamId }) => ({
        url: `/api/org/users/${userId}/team`,
        method: "PUT",
        body: { teamId },
      }),
      invalidatesTags: ["Users"],
    }),
    setTeamLead: b.mutation<void, { teamId: string; userId: string | null }>({
      query: ({ teamId, userId }) => ({
        url: `/api/org/teams/${teamId}/lead`,
        method: "PUT",
        body: { userId },
      }),
      invalidatesTags: ["Users"],
    }),

    // ===== Register (admin-protected on the server) =====
    register: b.mutation<UserSummary, { email: string; userName: string; password?: string | null; agencyId: string; roles: string[] }>({
      query: (body) => ({ url: "/api/auth/register", method: "POST", body }),
      invalidatesTags: ["Users"],
    }),

    // ===== Change own password (used by force-change flow + Settings) =====
    changePassword: b.mutation<void, { currentPassword: string; newPassword: string }>({
      query: (body) => ({ url: "/api/auth/change-password", method: "POST", body }),
      invalidatesTags: ["Me"],
    }),

    // ===== Lead Lists + CSV import =====
    leadLists: b.query<{ id: string; name: string; campaignId: string | null; leadSourceId: string | null; isActive: boolean; leadCount: number }[], void>({
      query: () => "/api/cc/lists",
      providesTags: ["LeadLists"],
    }),
    upsertLeadList: b.mutation<any, { id?: string | null; name: string; campaignId?: string | null; leadSourceId?: string | null; isActive: boolean }>({
      query: (body) => ({ url: "/api/cc/lists", method: "PUT", body }),
      invalidatesTags: ["LeadLists"],
    }),
    importLeadsCsv: b.mutation<any, { listId: string; file: File }>({
      query: ({ listId, file }) => {
        const fd = new FormData(); fd.append("file", file);
        return { url: `/api/cc/lists/${listId}/import`, method: "POST", body: fd };
      },
      invalidatesTags: ["LeadLists", "ImportBatches", "Leads"],
    }),
    listImportBatches: b.query<any[], string>({
      query: (id) => `/api/cc/lists/${id}/imports`,
      providesTags: ["ImportBatches"],
    }),

    // ===== Cadences =====
    listCadences: b.query<any[], void>({
      query: () => "/api/cc/cadences",
      providesTags: ["Cadences"],
    }),
    upsertCadence: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/cadences", method: "PUT", body }),
      invalidatesTags: ["Cadences"],
    }),
    enrollCadence: b.mutation<void, { cadenceId: string; leadId: string }>({
      query: (body) => ({ url: "/api/cc/cadences/enroll", method: "POST", body }),
      invalidatesTags: ["CadenceEnrollments"],
    }),
    cadenceEnrollments: b.query<any[], { cadenceId?: string; status?: string } | void>({
      query: (params) => ({ url: "/api/cc/cadences/enrollments", params: params ?? undefined }),
      providesTags: ["CadenceEnrollments"],
    }),

    // ===== Voicemails =====
    listVoicemails: b.query<any[], void>({
      query: () => "/api/cc/voicemails",
      providesTags: ["Voicemails"],
    }),
    upsertVoicemail: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/voicemails", method: "PUT", body }),
      invalidatesTags: ["Voicemails"],
    }),
    dropVoicemail: b.mutation<void, { leadId: string; voicemailAssetId: string; callRecordId?: string }>({
      query: (body) => ({ url: "/api/cc/voicemails/drop", method: "POST", body }),
    }),

    // ===== Inbound queues + IVR =====
    listQueues: b.query<any[], void>({
      query: () => "/api/cc/queues",
      providesTags: ["Queues"],
    }),
    upsertQueue: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/queues", method: "PUT", body }),
      invalidatesTags: ["Queues"],
    }),
    getIvr: b.query<any, string>({
      query: (id) => `/api/cc/queues/${id}/ivr`,
      providesTags: ["Ivr"],
    }),
    upsertIvr: b.mutation<any, any>({
      query: (body) => ({ url: "/api/cc/queues/ivr", method: "PUT", body }),
      invalidatesTags: ["Ivr"],
    }),

    // ===== Call transfer + dial mode =====
    transferCall: b.mutation<void, { callId: string; targetAgentUserId: string; transferType: string; note?: string }>({
      query: ({ callId, ...body }) => ({ url: `/api/cc/calls/${callId}/transfer`, method: "POST", body }),
    }),
    dialMode: b.query<{ mode: string }, string | void>({
      query: (campaignId) => ({ url: "/api/cc/dial-mode", params: campaignId ? { campaignId } : undefined }),
    }),

    // ===== Wallboard + Leaderboard =====
    wallboard: b.query<any, void>({
      query: () => "/api/cc/wallboard",
      providesTags: ["Wallboard"],
    }),
    leaderboard: b.query<any[], string | void>({
      query: (period) => ({ url: "/api/cc/leaderboard", params: period ? { period } : undefined }),
      providesTags: ["Leaderboard"],
    }),

    // ===== Knowledge base =====
    searchKb: b.query<any[], { q?: string; category?: string; publishedOnly?: boolean } | void>({
      query: (params) => ({ url: "/api/kb/articles", params: params ?? undefined }),
      providesTags: ["KbArticles"],
    }),
    getKbArticle: b.query<any, string>({
      query: (slug) => `/api/kb/articles/${slug}`,
    }),
    upsertKbArticle: b.mutation<any, any>({
      query: (body) => ({ url: "/api/kb/articles", method: "PUT", body }),
      invalidatesTags: ["KbArticles"],
    }),

    // ===== Public lead-capture endpoints =====
    listPublicEndpoints: b.query<any[], void>({
      query: () => "/api/admin/public-endpoints",
      providesTags: ["PublicEndpoints"],
    }),
    createPublicEndpoint: b.mutation<{ id: string; slug: string; secret: string }, any>({
      query: (body) => ({ url: "/api/admin/public-endpoints", method: "POST", body }),
      invalidatesTags: ["PublicEndpoints"],
    }),

    // ===== Call control (softphone) =====
    activeCall: b.query<ActiveCall | null, void>({
      query: () => "/api/cc/calls/active",
      transformResponse: (r) => (r as any) || null,
    }),
    dialLead: b.mutation<ActiveCall, { leadId: string }>({
      query: (body) => ({ url: "/api/cc/calls/dial", method: "POST", body }),
    }),
    answerCall: b.mutation<ActiveCall, string>({
      query: (id) => ({ url: `/api/cc/calls/${id}/answer`, method: "POST" }),
    }),
    hangupCall: b.mutation<ActiveCall, string>({
      query: (id) => ({ url: `/api/cc/calls/${id}/hangup`, method: "POST" }),
    }),
    holdCall: b.mutation<ActiveCall, { id: string; hold: boolean }>({
      query: ({ id, hold }) => ({ url: `/api/cc/calls/${id}/hold`, method: "POST", body: { hold } }),
    }),
    muteCall: b.mutation<ActiveCall, { id: string; mute: boolean }>({
      query: ({ id, mute }) => ({ url: `/api/cc/calls/${id}/mute`, method: "POST", body: { mute } }),
    }),
    sendDtmf: b.mutation<void, { id: string; digits: string }>({
      query: ({ id, digits }) => ({ url: `/api/cc/calls/${id}/dtmf`, method: "POST", body: { digits } }),
    }),
    sendQuickSms: b.mutation<void, { leadId: string; body: string }>({
      query: (body) => ({ url: "/api/cc/calls/sms", method: "POST", body }),
    }),

    // ===== Lead full detail =====
    leadDetail: b.query<LeadDetail, string>({
      query: (id) => `/api/leads/${id}`,
      providesTags: (_r, _e, id) => [{ type: "Lead", id }],
    }),
    updateLeadNotes: b.mutation<void, { id: string; notes: string }>({
      query: ({ id, notes }) => ({ url: `/api/leads/${id}/notes`, method: "PUT", body: { notes } }),
      invalidatesTags: (_r, _e, arg) => [{ type: "Lead", id: arg.id }],
    }),

    // ===== Audit log =====
    listAudit: b.query<PagedAuditResult, AuditQuery | void>({
      query: (params) => ({ url: "/api/admin/audit", params: params ?? undefined }),
    }),
    auditFilters: b.query<{ entityNames: string[]; actions: string[]; users: string[] }, void>({
      query: () => "/api/admin/audit/filters",
    }),

    // ===== Sales list =====
    listSales: b.query<PagedSalesResult, SalesQuery | void>({
      query: (params) => ({ url: "/api/sales", params: params ?? undefined }),
      providesTags: ["Sales"],
    }),

    // ===== Calls list =====
    listCalls: b.query<PagedCallsResult, CallsQuery | void>({
      query: (params) => ({ url: "/api/cc/calls", params: params ?? undefined }),
      providesTags: ["Calls"],
    }),

    // ===== Permissions (RBAC) =====
    myPermissions: b.query<string[], void>({
      query: () => "/api/permissions/mine",
      providesTags: ["Permissions"],
    }),
    listPermissions: b.query<{ code: string; group: string }[], void>({
      query: () => "/api/permissions",
      providesTags: ["Permissions"],
    }),
    rolePermissions: b.query<string[], string>({
      query: (roleId) => `/api/permissions/role/${roleId}`,
      providesTags: (_r, _e, id) => [{ type: "RolePermissions", id }],
    }),
    setRolePermissions: b.mutation<void, { roleId: string; permissionCodes: string[] }>({
      query: ({ roleId, permissionCodes }) => ({
        url: `/api/permissions/role/${roleId}`,
        method: "PUT",
        body: { permissionCodes },
      }),
      invalidatesTags: (_r, _e, arg) => [{ type: "RolePermissions", id: arg.roleId }, "Permissions"],
    }),
  }),
});

export interface AuditEntry {
  id: string; entityName: string; entityId: string; action: string;
  userId: string | null; userName: string | null;
  changes: string | null; ipAddress: string | null; occurredAt: string;
}
export interface PagedAuditResult { items: AuditEntry[]; total: number; skip: number; take: number; }
export interface AuditQuery {
  entityName?: string; entityId?: string; action?: string; userId?: string;
  after?: string; before?: string; search?: string; skip?: number; take?: number;
}

export interface SaleListItem {
  id: string; leadId: string; leadName: string; leadPhone: string;
  closerUserId: string; closerName: string | null;
  carrier: string; policyNumber: string | null;
  monthlyPremium: number; annualPremium: number;
  soldAt: string; validatedAt: string | null; fundedAt: string | null;
  isInternalSale: boolean; status: string;
}
export interface PagedSalesResult {
  items: SaleListItem[]; total: number; skip: number; take: number;
  totalPremium: number; fundedCount: number; validatedCount: number; pendingCount: number; internalCount: number;
}
export interface SalesQuery {
  closerUserId?: string; carrier?: string; status?: string;
  from?: string; to?: string; sort?: string; skip?: number; take?: number;
}

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
export interface CallsQuery {
  agentUserId?: string; direction?: string; status?: string;
  from?: string; to?: string; sort?: string; skip?: number; take?: number;
}

export interface LeadDetail {
  id: string;
  firstName: string; lastName: string; fullName: string;
  phoneNumber: string; email: string | null;
  address: string | null; city: string | null; state: string | null; postalCode: string | null;
  dateOfBirth: string | null; age: number | null;
  stage: string; disposition: string;
  source: string | null; jornayaLeadId: string | null; jornayaVerified: boolean;
  assignedUserId: string | null; assignedUserName: string | null;
  teamId: string | null; campaignId: string | null; leadSourceId: string | null; verticalId: string | null;
  requiredSkillCode: string | null; consentCaptured: boolean;
  score: number;
  scoreBreakdown: { rule: string; points: number; note: string | null }[];
  notes: string | null;
  createdAt: string; updatedAt: string | null;
  sale: { saleId: string; carrier: string; policyNumber: string | null; monthlyPremium: number; annualPremium: number; soldAt: string; validatedAt: string | null; fundedAt: string | null; isInternalSale: boolean } | null;
  callCount: number; openCallbackCount: number;
  recentCalls: { id: string; direction: string; status: string; initiatedAt: string; answeredAt: string | null; endedAt: string | null; recordingUrl: string | null; wrapUpCode: string | null; notes: string | null }[];
  callbacks: { id: string; scheduledFor: string; reason: string | null; assignedUserId: string; assignedUserName: string | null; completed: boolean }[];
}

export interface PagedLeadsResult { items: Lead[]; total: number; skip: number; take: number; }

export interface ListLeadsParams {
  stage?: WorkflowStage;
  assignedUserId?: string;
  disposition?: LeadDisposition;
  state?: string;
  campaignId?: string;
  leadSourceId?: string;
  minScore?: number;
  createdAfter?: string;
  createdBefore?: string;
  sort?: string;
  skip?: number;
  take?: number;
}

export interface BulkResult { updated: number; skipped: number; errors: string[]; }

export interface ActiveCall {
  id: string;
  leadId: string;
  leadName: string;
  phone: string;
  direction: "Inbound" | "Outbound";
  status: string;
  initiatedAt: string;
  answeredAt: string | null;
  endedAt: string | null;
  isHeld: boolean;
  isMuted: boolean;
  recordingUrl: string | null;
}

export const {
  useLoginMutation, useVerify2FaMutation, useSetup2FaMutation, useEnable2FaMutation,
  useDisable2FaMutation, useGet2FaStatusQuery,
  useMeQuery, useListUsersQuery, useUserDirectoryQuery,
  useListLeadsQuery, useMyLeadsQuery, useLeadTimelineQuery,
  useCreateLeadMutation, useTransitionLeadMutation, useAssignLeadMutation,
  useVerifyJornayaMutation, useDialMutation, useCarriersQuery,
  useRecordSaleMutation, useValidateSaleMutation, useFundSaleMutation, useUploadSaleRecordingMutation,
  useListDocumentsQuery, useUploadDocumentMutation, useDeleteDocumentMutation,
  useDocumentNotesQuery, useAddDocumentNoteMutation,
  useStartDirectMessageMutation,
  useMyCommissionsQuery, usePayrollRunsQuery, useCreatePayrollRunMutation,
  useMyCallbacksQuery, useScheduleCallbackMutation, useCompleteCallbackMutation,
  useMetricCatalogQuery, useDashboardQuery, useDashboardSummaryQuery,
  useLeadDiagnosticsQuery,
  useListIntegrationsQuery, useCheckIntegrationMutation,
  useRubricsQuery, useCreateRubricMutation,
  useChatRoomsQuery, useCreateRoomMutation, useRoomMessagesQuery, useSendMessageMutation, useSendAttachmentMutation,
  useChatUnreadQuery, useMarkRoomReadMutation,
  useListIpAllowlistQuery, useAddIpAllowlistMutation, useRemoveIpAllowlistMutation,
  useListVerticalsQuery, useCreateVerticalMutation, useUpdateVerticalMutation,
  useListHorizontalsQuery, useCreateHorizontalMutation, useUpdateHorizontalMutation,
  useListCallCentersQuery, useCreateCallCenterMutation, useUpdateCallCenterMutation, useSetUserCallCenterMutation,
  useCaptureIntakeLeadMutation, useVerifierQueueQuery, useSetVerifierStatusMutation,
  useCloserQueueQuery, useGetClosingApplicationQuery, useSubmitClosingApplicationMutation,
  useCaptureCloserLeadMutation, useValidatorQueueQuery, useSetValidatorStatusMutation,
  useListCommissionConfigQuery, useUpsertCommissionConfigMutation,
  useUpdateUserRolesMutation, useSetUserActiveMutation, useResetUserPasswordMutation,
  useSearchLeadsQuery, useDuplicateLeadsQuery,
  useSelfValidateSaleMutation,
  useListQaReviewsQuery, useQaScorecardsQuery,
  useMySessionQuery, useClockInMutation, useClockOutMutation, useSetAgentStatusMutation,
  useWrapUpCallMutation, useMyRecentCallsQuery,
  useListWrapUpCodesQuery, useUpsertWrapUpCodeMutation,
  useListDncQuery, useAddDncMutation, useRemoveDncMutation,
  useCheckComplianceMutation,
  useListCampaignsQuery, useUpsertCampaignMutation,
  useListLeadSourcesQuery, useUpsertLeadSourceMutation,
  useListSkillsQuery, useUpsertSkillMutation, useAssignAgentSkillMutation, useRemoveAgentSkillMutation, useAgentSkillsQuery,
  useListScriptsQuery, useUpsertScriptMutation,
  useLiveAgentsQuery, useForceAgentStatusMutation, useCoachAgentMutation,
  useListWorkflowRulesQuery, useUpsertWorkflowRuleMutation, useDeleteWorkflowRuleMutation,
  useWorkflowEventTypesQuery, useWorkflowActionTypesQuery, useWorkflowExecutionsQuery,
  useRescoreLeadMutation,
  useAiRecommendationsQuery, useAiLeadScoreQuery, useAiCallSummaryMutation,
  useListRolesQuery, useGetRoleQuery, useCreateRoleMutation, useRenameRoleMutation,
  useSetRoleModulesMutation, useDeleteRoleMutation,
  useListModulesQuery, useMyModulesQuery,
  useListAgenciesQuery, useGetAgencyQuery, useCreateAgencyMutation,
  useUpdateAgencyMutation, useAssignAgencyCeoMutation,
  useOrgTreeQuery, useSetUserTeamMutation, useSetTeamLeadMutation,
  useRegisterMutation,
  useChangePasswordMutation,
  useLeadListsQuery, useUpsertLeadListMutation, useImportLeadsCsvMutation, useListImportBatchesQuery,
  useListCadencesQuery, useUpsertCadenceMutation, useEnrollCadenceMutation, useCadenceEnrollmentsQuery,
  useListVoicemailsQuery, useUpsertVoicemailMutation, useDropVoicemailMutation,
  useListQueuesQuery, useUpsertQueueMutation, useGetIvrQuery, useUpsertIvrMutation,
  useTransferCallMutation, useDialModeQuery,
  useWallboardQuery, useLeaderboardQuery,
  useSearchKbQuery, useGetKbArticleQuery, useUpsertKbArticleMutation,
  useListPublicEndpointsQuery, useCreatePublicEndpointMutation,
  useActiveCallQuery, useDialLeadMutation, useAnswerCallMutation, useHangupCallMutation,
  useHoldCallMutation, useMuteCallMutation, useSendDtmfMutation, useSendQuickSmsMutation,
  useLeadDetailQuery, useUpdateLeadNotesMutation,
  useSetTwoFactorMethodMutation, useSendEmailOtpMutation,
  useBulkAssignLeadsMutation, useBulkSetStageMutation, useBulkEnrollCadenceMutation,
  useListAuditQuery, useAuditFiltersQuery,
  useListSalesQuery, useListCallsQuery,
  useForgotPasswordMutation, useResetPasswordMutation,
  useConfirmEmailMutation, useResendEmailConfirmationMutation,
  useMyPermissionsQuery, useListPermissionsQuery,
  useRolePermissionsQuery, useSetRolePermissionsMutation,
} = baseApi;
