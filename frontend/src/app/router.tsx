import { lazy, type ComponentType } from "react";
import { createBrowserRouter, Navigate, RouterProvider } from "react-router-dom";
// Small / always-hit pages stay eager so they're part of the initial bundle.
import { LoginPage } from "../features/auth/LoginPage";
import { ForgotPasswordPage } from "../features/auth/ForgotPasswordPage";
import { ResetPasswordPage } from "../features/auth/ResetPasswordPage";
import { ConfirmEmailPage } from "../features/auth/ConfirmEmailPage";
import { LeadsPage } from "../features/leads/LeadsPage";
import { MyQueuePage } from "../features/leads/MyQueuePage";
import { IntakeFormPage } from "../features/intake/IntakeFormPage";
import { VerifyQueuePage } from "../features/intake/VerifyQueuePage";
import { CloseQueuePage } from "../features/intake/CloseQueuePage";
import { ClosingApplicationPage } from "../features/intake/ClosingApplicationPage";
import { ValidateQueuePage } from "../features/intake/ValidateQueuePage";
import { LeadDetailPage } from "../features/leads/LeadDetailPage";
import { UsersPage } from "../features/users/UsersPage";
import { SalesPage } from "../features/sales/SalesPage";
import { CallbacksPage } from "../features/callbacks/CallbacksPage";
import { ChatPage } from "../features/chat/ChatPage";
import { AdminPage } from "../features/admin/AdminPage";
import { AgenciesPage } from "../features/admin/AgenciesPage";
import { CallCentersPage } from "../features/admin/CallCentersPage";
import { TeamPage } from "../features/team/TeamPage";
import { ProfilePage } from "../features/profile/ProfilePage";
import { UserManagementPage } from "../features/admin/UserManagementPage";
import { RegisterPage } from "../features/auth/RegisterPage";
import { ChangePasswordPage } from "../features/auth/ChangePasswordPage";
import { AgentPanelPage } from "../features/callcenter/AgentPanelPage";
import { Dashboard } from "../pages/Dashboard";
import { Layout } from "../shared/components/Layout";
import { ProtectedRoute } from "../shared/components/ProtectedRoute";
import { ForbiddenPage } from "../shared/components/ForbiddenPage";

/**
 * React.lazy that survives deploys. When a new build ships, this tab's `index.html`
 * still points at the OLD chunk filenames — which are gone from the server — so the
 * dynamic import() 404s and the page can't render. Instead of crashing, we reload
 * ONCE to pull the fresh index.html + current chunks. A sessionStorage guard prevents
 * an infinite reload loop if the chunk is genuinely broken (not just stale); it's
 * cleared on the first successful load so future deploys can self-heal again.
 */
function lazyWithReload<T extends ComponentType<unknown>>(factory: () => Promise<{ default: T }>) {
  const KEY = "lazy-chunk-reloaded";
  return lazy(async () => {
    try {
      const mod = await factory();
      sessionStorage.removeItem(KEY);
      return mod;
    } catch (err) {
      if (sessionStorage.getItem(KEY)) throw err;       // already reloaded once → surface the error
      sessionStorage.setItem(KEY, "1");
      window.location.reload();
      return await new Promise<{ default: T }>(() => {}); // hang (Suspense spinner) until reload
    }
  });
}

// Heavy / rarely-hit pages are route-split via React.lazy so each lands in its own
// chunk instead of the 1.3 MB initial bundle. Every one of these routes renders
// inside <Layout>, whose <Outlet> is wrapped in a <Suspense> boundary (see
// Layout.tsx) that shows a spinner while the chunk downloads. These are NAMED
// exports, so we map the export onto `default` for React.lazy.
const TwoFactorEnrollPage = lazyWithReload(() => import("../features/auth/TwoFactorEnrollPage").then(m => ({ default: m.TwoFactorEnrollPage })));
const LeadSearchPage = lazyWithReload(() => import("../features/leads/LeadSearchPage").then(m => ({ default: m.LeadSearchPage })));
const LeadTroubleshootPage = lazyWithReload(() => import("../features/leads/LeadTroubleshootPage").then(m => ({ default: m.LeadTroubleshootPage })));
const CommissionsPage = lazyWithReload(() => import("../features/sales/CommissionsPage").then(m => ({ default: m.CommissionsPage })));
const SaleDetailPage = lazyWithReload(() => import("../features/sales/SaleDetailPage").then(m => ({ default: m.SaleDetailPage })));
const KpiDashboardPage = lazyWithReload(() => import("../features/dashboard/KpiDashboardPage").then(m => ({ default: m.KpiDashboardPage })));
const QaPage = lazyWithReload(() => import("../features/qa/QaPage").then(m => ({ default: m.QaPage })));
const QaBrowserPage = lazyWithReload(() => import("../features/qa/QaBrowserPage").then(m => ({ default: m.QaBrowserPage })));
const AgencyDetailPage = lazyWithReload(() => import("../features/admin/AgencyDetailPage").then(m => ({ default: m.AgencyDetailPage })));
const CallCenterDetailPage = lazyWithReload(() => import("../features/admin/CallCenterDetailPage").then(m => ({ default: m.CallCenterDetailPage })));
const SubmissionAgentsPage = lazyWithReload(() => import("../features/admin/SubmissionAgentsPage").then(m => ({ default: m.SubmissionAgentsPage })));
const ChatOversightPage = lazyWithReload(() => import("../features/admin/ChatOversightPage").then(m => ({ default: m.ChatOversightPage })));
const SecurityCenterPage = lazyWithReload(() => import("../features/admin/SecurityCenterPage").then(m => ({ default: m.SecurityCenterPage })));
const AuditLogPage = lazyWithReload(() => import("../features/admin/AuditLogPage").then(m => ({ default: m.AuditLogPage })));
const ConfidentialPage = lazyWithReload(() => import("../features/confidential/ConfidentialPage").then(m => ({ default: m.ConfidentialPage })));
const EmployeesPage = lazyWithReload(() => import("../features/hr/EmployeesPage").then(m => ({ default: m.EmployeesPage })));
const HrAttendancePage = lazyWithReload(() => import("../features/hr/HrAttendancePage").then(m => ({ default: m.HrAttendancePage })));
const InterviewsPage = lazyWithReload(() => import("../features/hr/InterviewsPage").then(m => ({ default: m.InterviewsPage })));
const PayrollPage = lazyWithReload(() => import("../features/hr/PayrollPage").then(m => ({ default: m.PayrollPage })));
const SocialReportsPage = lazyWithReload(() => import("../features/hr/SocialReportsPage").then(m => ({ default: m.SocialReportsPage })));
const CallsHistoryPage = lazyWithReload(() => import("../features/callcenter/CallsHistoryPage").then(m => ({ default: m.CallsHistoryPage })));
const RolesPage = lazyWithReload(() => import("../features/admin/RolesPage").then(m => ({ default: m.RolesPage })));
const GlobalSearchPage = lazyWithReload(() => import("../features/search/GlobalSearchPage").then(m => ({ default: m.GlobalSearchPage })));
const SupervisorPage = lazyWithReload(() => import("../features/callcenter/SupervisorPage").then(m => ({ default: m.SupervisorPage })));
const AttendancePage = lazyWithReload(() => import("../features/callcenter/AttendancePage").then(m => ({ default: m.AttendancePage })));
const DncPage = lazyWithReload(() => import("../features/callcenter/DncPage").then(m => ({ default: m.DncPage })));
const CampaignsPage = lazyWithReload(() => import("../features/callcenter/CampaignsPage").then(m => ({ default: m.CampaignsPage })));
const ScriptsPage = lazyWithReload(() => import("../features/callcenter/ScriptsPage").then(m => ({ default: m.ScriptsPage })));
const WorkflowsPage = lazyWithReload(() => import("../features/workflows/WorkflowsPage").then(m => ({ default: m.WorkflowsPage })));
const LeadListsPage = lazyWithReload(() => import("../features/lists/LeadListsPage").then(m => ({ default: m.LeadListsPage })));
const CadencesPage = lazyWithReload(() => import("../features/cadences/CadencesPage").then(m => ({ default: m.CadencesPage })));
const WallboardPage = lazyWithReload(() => import("../features/wallboard/WallboardPage").then(m => ({ default: m.WallboardPage })));
const KnowledgeBasePage = lazyWithReload(() => import("../features/kb/KnowledgeBasePage").then(m => ({ default: m.KnowledgeBasePage })));
const DocumentsPage = lazyWithReload(() => import("../features/documents/DocumentsPage").then(m => ({ default: m.DocumentsPage })));
const QueuesPage = lazyWithReload(() => import("../features/queues/QueuesPage").then(m => ({ default: m.QueuesPage })));
const IntegrationsPage = lazyWithReload(() => import("../features/admin/IntegrationsPage").then(m => ({ default: m.IntegrationsPage })));
const GuidePage = lazyWithReload(() => import("../features/guide/GuidePage").then(m => ({ default: m.GuidePage })));
const NotificationsPage = lazyWithReload(() => import("../features/notifications/NotificationsPage").then(m => ({ default: m.NotificationsPage })));
const CalendarPage = lazyWithReload(() => import("../features/scheduler/CalendarPage").then(m => ({ default: m.CalendarPage })));
const PulsePage = lazyWithReload(() => import("../features/pulse/PulsePage").then(m => ({ default: m.PulsePage })));
const BugsPage = lazyWithReload(() => import("../features/bugs/BugsPage").then(m => ({ default: m.BugsPage })));
const LicenseAgentQueuePage = lazyWithReload(() => import("../features/sales/LicenseAgentQueuePage").then(m => ({ default: m.LicenseAgentQueuePage })));

const adminRoles = ["Admin", "ProgramManager"];

/**
 * Module codes (must match backend ModuleCatalog) used to gate each route.
 * Admin role always bypasses these checks (see ProtectedRoute).
 */
const M = {
  Leads: "leads",
  LeadsSearch: "leads.search",
  Sales: "sales",
  Callbacks: "callbacks",
  Commissions: "commissions",
  Reports: "reports",
  Qa: "qa",
  Chat: "chat",
  AgentPanel: "agent",
  MyQueue: "queue",
  CallCenter: "callcenter",
  Supervisor: "supervisor",
  Attendance: "attendance",
  Knowledge: "knowledge",
  Documents: "documents",
  Dnc: "dnc",
  Campaigns: "campaigns",
  Scripts: "scripts",
  Workflows: "workflows",
  UsersManagement: "users.manage",
  RolesManagement: "roles.manage",
  Admin: "admin",
} as const;

const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  { path: "/reset-password", element: <ResetPasswordPage /> },
  { path: "/confirm-email", element: <ConfirmEmailPage /> },
  { path: "/forbidden", element: <ForbiddenPage /> },
  // Forced-password-change flow: authenticated, but no Layout chrome until they set a password.
  {
    element: <ProtectedRoute />,
    children: [
      { path: "/change-password", element: <ChangePasswordPage /> },
    ],
  },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <Layout />,
        children: [
          // Always available to any authenticated user
          { path: "/dashboard", element: <Dashboard /> },
          { path: "/guide",     element: <GuidePage /> },
          { path: "/notifications", element: <NotificationsPage /> },
          { path: "/search",    element: <GlobalSearchPage /> },
          { path: "/2fa",       element: <TwoFactorEnrollPage /> },
          { path: "/team",      element: <TeamPage /> },
          { path: "/calendar",  element: <CalendarPage /> },
          { path: "/pulse",     element: <PulsePage /> },
          { path: "/bugs",      element: <BugsPage /> },
          // Self-service profile — /profile is the signed-in user (editable);
          // /profile/:userId views another (same-agency) user, read-only.
          { path: "/profile",          element: <ProfilePage /> },
          { path: "/profile/:userId",  element: <ProfilePage /> },

          // Workspace
          {
            element: <ProtectedRoute modules={[M.AgentPanel]} />,
            children: [{ path: "/agent", element: <AgentPanelPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.MyQueue]} />,
            children: [{ path: "/queue", element: <MyQueuePage /> }],
          },

          // Intake pipeline — role-gated (Admin/SuperAdmin bypass in ProtectedRoute)
          {
            element: <ProtectedRoute roles={["Fronter"]} />,
            children: [{ path: "/intake", element: <IntakeFormPage /> }],
          },
          {
            element: <ProtectedRoute roles={["Verifier"]} />,
            children: [{ path: "/verify-queue", element: <VerifyQueuePage /> }],
          },
          {
            element: <ProtectedRoute roles={["Closer"]} />,
            children: [
              { path: "/close-queue", element: <CloseQueuePage /> },
              { path: "/close-queue/:id", element: <ClosingApplicationPage /> },
            ],
          },
          {
            element: <ProtectedRoute roles={["Validator"]} />,
            children: [{ path: "/validate-queue", element: <ValidateQueuePage /> }],
          },
          {
            element: <ProtectedRoute roles={["LicenseAgent"]} />,
            children: [{ path: "/my-sales", element: <LicenseAgentQueuePage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Callbacks]} />,
            children: [{ path: "/callbacks", element: <CallbacksPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Chat]} />,
            children: [{ path: "/chat", element: <ChatPage /> }],
          },

          // Pipeline
          {
            element: <ProtectedRoute modules={[M.Leads]} />,
            children: [
              { path: "/leads",                  element: <LeadsPage /> },
              { path: "/leads/:id",              element: <LeadDetailPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.LeadsSearch]} />,
            children: [{ path: "/leads/search", element: <LeadSearchPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Sales]} />,
            children: [
              { path: "/sales",     element: <SalesPage /> },
              { path: "/sales/:id", element: <SaleDetailPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.Commissions]} />,
            children: [{ path: "/commissions", element: <CommissionsPage /> }],
          },

          // Operations
          {
            element: <ProtectedRoute modules={[M.Supervisor]} />,
            children: [
              { path: "/supervisor", element: <SupervisorPage /> },
              { path: "/wallboard",  element: <WallboardPage /> },
              // Lead troubleshooting is a supervisory diagnostics tool, not a floor-agent one.
              { path: "/leads/troubleshoot",     element: <LeadTroubleshootPage /> },
              { path: "/leads/:id/troubleshoot", element: <LeadTroubleshootPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.Attendance]} />,
            children: [{ path: "/attendance", element: <AttendancePage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Reports]} />,
            children: [{ path: "/kpis", element: <KpiDashboardPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Qa]} />,
            children: [
              { path: "/qa",         element: <QaPage /> },
              { path: "/qa/browser", element: <QaBrowserPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.Knowledge]} roles={["HR"]} />,
            children: [{ path: "/kb", element: <KnowledgeBasePage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Documents]} />,
            children: [{ path: "/documents", element: <DocumentsPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.CallCenter]} />,
            children: [
              { path: "/calls",  element: <CallsHistoryPage /> },
              { path: "/queues", element: <QueuesPage /> },
            ],
          },

          // Lead lifecycle config (manager-tier — not floor agents)
          {
            element: <ProtectedRoute modules={[M.Campaigns]} />,
            children: [
              { path: "/lists",     element: <LeadListsPage /> },
              { path: "/cadences",  element: <CadencesPage /> },
            ],
          },

          // Compliance / call-center config
          {
            element: <ProtectedRoute modules={[M.Dnc]} />,
            children: [{ path: "/dnc", element: <DncPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Campaigns]} />,
            children: [{ path: "/campaigns", element: <CampaignsPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Scripts]} />,
            children: [{ path: "/scripts", element: <ScriptsPage /> }],
          },

          // Administration — admin-only
          {
            element: <ProtectedRoute modules={[M.UsersManagement]} roles={adminRoles} />,
            children: [
              { path: "/users",          element: <UsersPage /> },
              { path: "/admin/users",    element: <UserManagementPage /> },
              { path: "/admin/register", element: <RegisterPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.RolesManagement]} roles={adminRoles} />,
            children: [
              { path: "/admin/roles",    element: <RolesPage /> },
              { path: "/admin/security", element: <SecurityCenterPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.Workflows]} roles={adminRoles} />,
            children: [{ path: "/workflows", element: <WorkflowsPage /> }],
          },
          {
            // Confidential vault — Admin / SuperAdmin only (both bypass in ProtectedRoute).
            element: <ProtectedRoute roles={["Admin"]} />,
            children: [{ path: "/confidential", element: <ConfidentialPage /> }],
          },
          {
            // HR module — HR staff + Admin/SuperAdmin (both bypass in ProtectedRoute).
            element: <ProtectedRoute roles={["HR"]} />,
            children: [
              { path: "/hr/employees", element: <EmployeesPage /> },
              { path: "/hr/attendance", element: <HrAttendancePage /> },
              { path: "/hr/interviews", element: <InterviewsPage /> },
              { path: "/hr/payroll", element: <PayrollPage /> },
              { path: "/hr/social", element: <SocialReportsPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.Admin]} roles={adminRoles} />,
            children: [
              { path: "/admin",              element: <AdminPage /> },
              { path: "/admin/audit",        element: <AuditLogPage /> },
              { path: "/admin/call-centers", element: <CallCentersPage /> },
              { path: "/admin/integrations", element: <IntegrationsPage /> },
            ],
          },

          // SuperAdmin-only: cross-tenant call-center management.
          {
            element: <ProtectedRoute roles={["SuperAdmin"]} />,
            children: [
              { path: "/admin/agencies", element: <AgenciesPage /> },
              { path: "/admin/agencies/:agencyId", element: <AgencyDetailPage /> },
              { path: "/admin/agencies/:agencyId/call-centers/:callCenterId", element: <CallCenterDetailPage /> },
              { path: "/admin/submission-agents", element: <SubmissionAgentsPage /> },
              { path: "/admin/chat-oversight", element: <ChatOversightPage /> },
            ],
          },
        ],
      },
    ],
  },
  { path: "/", element: <Navigate to="/dashboard" replace /> },
  { path: "*", element: <Navigate to="/dashboard" replace /> },
]);

export function AppRouter() {
  return <RouterProvider router={router} />;
}
