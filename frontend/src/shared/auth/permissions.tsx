import { type ReactNode } from "react";
import { useMyPermissionsQuery } from "../api/baseApi";

/**
 * Permission codes mirror backend `Permissions` constants in
 * backend/src/CRM.Application/Common/Authorization/IPermissionService.cs.
 * Keep this in sync when adding new codes.
 */
export const Perm = {
  // Pipeline
  LeadsRead: "leads.read",
  LeadsWrite: "leads.write",
  LeadsAssign: "leads.assign",
  LeadsTransition: "leads.transition",
  LeadsImport: "leads.import",
  LeadsExport: "leads.export",
  LeadsDelete: "leads.delete",
  SalesRead: "sales.read",
  SalesRecord: "sales.record",
  SalesValidate: "sales.validate",
  SalesFund: "sales.fund",
  SalesWrite: "sales.write",
  CallbacksRead: "callbacks.read",
  CallbacksWrite: "callbacks.write",
  // Workspace
  DashboardView: "dashboard.view",
  AgentPanelUse: "agent.use",
  QueueRead: "queue.read",
  QueueWrite: "queue.write",
  // Operations
  SupervisorView: "supervisor.view",
  SupervisorControl: "supervisor.control",
  CallCenterView: "callcenter.view",
  CallCenterControl: "callcenter.control",
  QaView: "qa.view",
  QaSubmit: "qa.submit",
  QaWrite: "qa.write",
  // Finance
  CommissionsView: "commissions.view",
  CommissionsWrite: "commissions.write",
  PayrollView: "payroll.view",
  PayrollProcess: "payroll.process",
  // Resources
  KnowledgeView: "knowledge.view",
  KnowledgeWrite: "knowledge.write",
  ChatRead: "chat.read",
  ChatWrite: "chat.write",
  // Insights
  ReportsView: "reports.view",
  // Administration
  WorkflowsView: "workflows.view",
  WorkflowsManage: "workflows.manage",
  CampaignsView: "campaigns.view",
  CampaignsManage: "campaigns.manage",
  ScriptsView: "scripts.view",
  ScriptsManage: "scripts.manage",
  DncView: "dnc.view",
  DncManage: "dnc.manage",
  UsersRead: "users.read",
  UsersManage: "users.manage",
  TeamRead: "team.read",
  TeamWrite: "team.write",
  RolesRead: "roles.read",
  RolesManage: "roles.manage",
  PermissionsManage: "permissions.manage",
  AgenciesView: "agencies.view",
  AgenciesCreate: "agencies.create",
  AgenciesManage: "agencies.manage",
  IpAllowlistManage: "ip-allowlist.manage",
  IntegrationsView: "integrations.view",
  IntegrationsManage: "integrations.manage",
} as const;

export type PermissionCode = (typeof Perm)[keyof typeof Perm];

/**
 * Hook returning permission helpers for the current user.
 * `has(code)` returns true if the user holds the code, OR is SuperAdmin (server returns "*").
 */
export function usePermissions() {
  const { data, isLoading } = useMyPermissionsQuery();
  const set = new Set(data ?? []);
  const isSuperAdmin = set.has("*");

  function has(code: string | string[]): boolean {
    if (isSuperAdmin) return true;
    if (Array.isArray(code)) return code.some((c) => set.has(c));
    return set.has(code);
  }

  function hasAll(codes: string[]): boolean {
    if (isSuperAdmin) return true;
    return codes.every((c) => set.has(c));
  }

  return { has, hasAll, isSuperAdmin, isLoading, codes: Array.from(set) };
}

/**
 * Convenience hook: `const canEdit = usePermission("team.write");`
 */
export function usePermission(code: string | string[]): boolean {
  const { has } = usePermissions();
  return has(code);
}

interface CanProps {
  /** Required permission code, or array of codes (any-of). */
  permission: string | string[];
  /** Render `fallback` instead of nothing when permission is missing. */
  fallback?: ReactNode;
  children: ReactNode;
}

/**
 * `<Can permission="team.write">…</Can>` — render children only if the
 * current user holds the permission. Use to gate buttons and write-side UI.
 *
 * For inputs/buttons that should *display* in disabled state instead of
 * disappearing, use `usePermission()` directly:
 *   const canEdit = usePermission(Perm.TeamWrite);
 *   <Button disabled={!canEdit} …/>
 */
export function Can({ permission, fallback = null, children }: CanProps) {
  const ok = usePermission(permission);
  return <>{ok ? children : fallback}</>;
}
