import { createBrowserRouter, Navigate, RouterProvider } from "react-router-dom";
import { LoginPage } from "../features/auth/LoginPage";
import { TwoFactorEnrollPage } from "../features/auth/TwoFactorEnrollPage";
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
import { LeadSearchPage } from "../features/leads/LeadSearchPage";
import { LeadTroubleshootPage } from "../features/leads/LeadTroubleshootPage";
import { UsersPage } from "../features/users/UsersPage";
import { SalesPage } from "../features/sales/SalesPage";
import { CommissionsPage } from "../features/sales/CommissionsPage";
import { CallbacksPage } from "../features/callbacks/CallbacksPage";
import { KpiDashboardPage } from "../features/dashboard/KpiDashboardPage";
import { QaPage } from "../features/qa/QaPage";
import { QaBrowserPage } from "../features/qa/QaBrowserPage";
import { ChatPage } from "../features/chat/ChatPage";
import { AdminPage } from "../features/admin/AdminPage";
import { AgenciesPage } from "../features/admin/AgenciesPage";
import { AuditLogPage } from "../features/admin/AuditLogPage";
import { TeamPage } from "../features/team/TeamPage";
import { CallsHistoryPage } from "../features/callcenter/CallsHistoryPage";
import { UserManagementPage } from "../features/admin/UserManagementPage";
import { RolesPage } from "../features/admin/RolesPage";
import { RegisterPage } from "../features/auth/RegisterPage";
import { ChangePasswordPage } from "../features/auth/ChangePasswordPage";
import { GlobalSearchPage } from "../features/search/GlobalSearchPage";
import { AgentPanelPage } from "../features/callcenter/AgentPanelPage";
import { SupervisorPage } from "../features/callcenter/SupervisorPage";
import { DncPage } from "../features/callcenter/DncPage";
import { CampaignsPage } from "../features/callcenter/CampaignsPage";
import { ScriptsPage } from "../features/callcenter/ScriptsPage";
import { WorkflowsPage } from "../features/workflows/WorkflowsPage";
import { LeadListsPage } from "../features/lists/LeadListsPage";
import { CadencesPage } from "../features/cadences/CadencesPage";
import { WallboardPage } from "../features/wallboard/WallboardPage";
import { KnowledgeBasePage } from "../features/kb/KnowledgeBasePage";
import { DocumentsPage } from "../features/documents/DocumentsPage";
import { QueuesPage } from "../features/queues/QueuesPage";
import { Dashboard } from "../pages/Dashboard";
import { Layout } from "../shared/components/Layout";
import { ProtectedRoute } from "../shared/components/ProtectedRoute";
import { ForbiddenPage } from "../shared/components/ForbiddenPage";
import { IntegrationsPage } from "../features/admin/IntegrationsPage";

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
          { path: "/search",    element: <GlobalSearchPage /> },
          { path: "/2fa",       element: <TwoFactorEnrollPage /> },
          { path: "/team",      element: <TeamPage /> },

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
              { path: "/leads/:id/troubleshoot", element: <LeadTroubleshootPage /> },
              { path: "/leads/troubleshoot",     element: <LeadTroubleshootPage /> },
            ],
          },
          {
            element: <ProtectedRoute modules={[M.LeadsSearch]} />,
            children: [{ path: "/leads/search", element: <LeadSearchPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Sales]} />,
            children: [{ path: "/sales", element: <SalesPage /> }],
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
            ],
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
            element: <ProtectedRoute modules={[M.Knowledge]} />,
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

          // Lead lifecycle config (TeamLead+)
          {
            element: <ProtectedRoute modules={[M.Leads]} />,
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
            children: [{ path: "/admin/roles", element: <RolesPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Workflows]} roles={adminRoles} />,
            children: [{ path: "/workflows", element: <WorkflowsPage /> }],
          },
          {
            element: <ProtectedRoute modules={[M.Admin]} roles={adminRoles} />,
            children: [
              { path: "/admin",              element: <AdminPage /> },
              { path: "/admin/audit",        element: <AuditLogPage /> },
              { path: "/admin/integrations", element: <IntegrationsPage /> },
            ],
          },

          // SuperAdmin-only: cross-tenant call-center management.
          {
            element: <ProtectedRoute roles={["SuperAdmin"]} />,
            children: [
              { path: "/admin/agencies", element: <AgenciesPage /> },
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
