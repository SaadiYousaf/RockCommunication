/**
 * THE navigation tree — the single source of truth for every page in the app.
 *
 * Both the sidebar (Layout) and the ⌘K command palette read this list, so a page added here is
 * automatically reachable from BOTH. It lives in its own module because Layout imports the command
 * palette, and the palette needs the tree — importing it from Layout would be circular.
 */
import type { IconName } from "../ui";
import type { QueueCounts } from "../api/types";

/**
 * A node in the multi-level navigation tree. A node is either a LEAF (has `to`, links to a page)
 * or a BRANCH (has `children`, expands to reveal them). Top-level nodes are sections. Visibility
 * gates (module / roles / superAdminOnly) apply to leaves; a branch is shown when any descendant is.
 */
export interface NavNode {
  /** Stable id — drives expand-state persistence and breadcrumbs. */
  key: string;
  label: string;
  icon?: IconName;
  /** Target route for a leaf (or a navigable branch). */
  to?: string;
  children?: NavNode[];
  /** Module code from the backend ModuleCatalog. Visibility is driven by user.modules. */
  module?: string;
  /** When true, hidden unless the user holds the SuperAdmin role. */
  superAdminOnly?: boolean;
  /** When set, shown only to these roles (plus Admin / SuperAdmin). */
  roles?: string[];
  badge?: string;
  /** When set, shows a live "N waiting" count from /api/work-queues/counts. */
  countKey?: keyof QueueCounts;
}

export const NAV: NavNode[] = [
  {
    key: "workspace", label: "Workspace",
    children: [
      { key: "dashboard", to: "/dashboard", label: "Dashboard", icon: "dashboard", module: "dashboard" },
      { key: "calendar",  to: "/calendar",  label: "Calendar",  icon: "calendar" },
      // The two work lists, side by side and named for what they contain. "My Leads" is the
      // primary experience: if a lead is yours, it is here and nowhere else.
      { key: "queue",     to: "/queue",     label: "My Leads",        icon: "inbox",     module: "queue", countKey: "myLeads" },
      { key: "available", to: "/available", label: "Available Leads", icon: "briefcase", module: "queue", countKey: "available" },
      { key: "intake-form", to: "/intake",  label: "Add Lead",        icon: "plus",      roles: ["Fronter", "Closer"] },
      { key: "chat",      to: "/chat",      label: "Chat",      icon: "chat",  module: "chat" },
      { key: "pulse",     to: "/pulse",     label: "Pulse",     icon: "activity" },
      { key: "callbacks", to: "/callbacks", label: "Callbacks", icon: "calendar", module: "callbacks", countKey: "callbacks" },
      // "Intake Pipeline" is gone: with the two role queues collapsed into Available Leads it held
      // nothing that belonged under a pipeline heading, and it forced users to learn which internal
      // queue their job mapped to. Its survivors are role-specific surfaces, listed plainly.
      { key: "validate-queue", to: "/validate-queue", label: "Submissions", icon: "shield",    roles: ["Validator"], countKey: "submissionQueue" },
      { key: "my-sales",       to: "/my-sales",       label: "My Sales",    icon: "briefcase", roles: ["LicenseAgent"] },
      { key: "agent", to: "/agent", label: "Agent Panel", icon: "phone", module: "agent" },
      { key: "team",  to: "/team",  label: "Team",        icon: "users" },
      { key: "guide", to: "/guide", label: "Guide",       icon: "book" },
      { key: "bugs",  to: "/bugs",  label: "Bugs",        icon: "alert" },
    ],
  },
  {
    key: "pipeline", label: "Pipeline",
    children: [
      { key: "leads", to: "/leads", label: "All Leads", icon: "list", module: "leads" },
      {
        key: "outreach", label: "Lists & Outreach", icon: "filter",
        children: [
          { key: "leads-search",       to: "/leads/search",       label: "Search & Dedup", icon: "search", module: "leads.search" },
          { key: "lists",              to: "/lists",              label: "Lead Lists",     icon: "inbox",  module: "campaigns" },
          { key: "cadences",           to: "/cadences",           label: "Cadences",       icon: "filter", module: "campaigns" },
          { key: "campaigns",          to: "/campaigns",          label: "Campaigns",      icon: "target", module: "campaigns" },
          { key: "leads-troubleshoot", to: "/leads/troubleshoot", label: "Troubleshoot",   icon: "shield", module: "supervisor" },
        ],
      },
      { key: "sales",       to: "/sales",       label: "Sales",       icon: "briefcase", module: "sales" },
      { key: "retention",   to: "/retention",   label: "Retention",   icon: "refresh",   module: "retention" },
      {
        // Everything commission-related lives under one heading. Labels drop the redundant
        // "Commission" prefix here — the parent already says it.
        key: "commission", label: "Commission", icon: "dollar",
        children: [
          { key: "commission-desk",  to: "/commission-desk",           label: "Commission Desk", icon: "dollar", module: "commission-desk" },
          { key: "commission-dash",  to: "/commission-desk/dashboard", label: "Dashboard",       icon: "chart",  module: "commission-desk" },
          { key: "commissions",      to: "/commissions",               label: "My Commissions",  icon: "doc",    module: "commissions" },
          { key: "commission-rates", to: "/commission-rates",          label: "Rates",           icon: "dollar", module: "commission-rates" },
          { key: "carrier-rules",    to: "/carrier-rules",             label: "Carrier Rules",   icon: "doc",    module: "carrier-rules" },
        ],
      },
      { key: "calls",       to: "/calls",       label: "Call History", icon: "phone",    module: "callcenter" },
    ],
  },
  {
    key: "operations", label: "Operations",
    children: [
      {
        key: "supervision", label: "Supervision", icon: "chart",
        children: [
          { key: "supervisor", to: "/supervisor", label: "Supervisor", icon: "shield", module: "supervisor" },
          { key: "wallboard",  to: "/wallboard",  label: "Wallboard",  icon: "chart",  module: "supervisor" },
          { key: "kpis",       to: "/kpis",       label: "KPIs",       icon: "chart",  module: "reports" },
          { key: "attendance", to: "/attendance", label: "Attendance", icon: "clock",  module: "attendance" },
        ],
      },
      {
        key: "telephony", label: "Call Center", icon: "phone",
        children: [
          { key: "queues",  to: "/queues",  label: "Queues + IVR", icon: "phone", module: "callcenter" },
          { key: "scripts", to: "/scripts", label: "Scripts",      icon: "doc",   module: "scripts" },
          { key: "dnc",     to: "/dnc",     label: "DNC List",     icon: "flag",  module: "dnc" },
        ],
      },
      {
        key: "quality", label: "Quality & Content", icon: "star",
        children: [
          { key: "qa",         to: "/qa",         label: "QA Reviews", icon: "star", module: "qa" },
          { key: "qa-browser", to: "/qa/browser", label: "QA Browser", icon: "doc",  module: "qa" },
          { key: "kb",         to: "/kb",         label: "Knowledge",  icon: "book", module: "knowledge" },
          { key: "documents",  to: "/documents",  label: "Documents",  icon: "doc",  module: "documents" },
        ],
      },
      { key: "workflows", to: "/workflows", label: "Workflows", icon: "filter", module: "workflows" },
    ],
  },
  {
    key: "hr", label: "Human Resources",
    children: [
      { key: "hr-employees",  to: "/hr/employees",  label: "Employees",    icon: "users",    roles: ["HR"] },
      { key: "hr-attendance", to: "/hr/attendance", label: "Attendance",   icon: "clock",    roles: ["HR"] },
      { key: "hr-interviews", to: "/hr/interviews", label: "Interviews",   icon: "userPlus", roles: ["HR"] },
      { key: "hr-payroll",    to: "/hr/payroll",    label: "Payroll",      icon: "doc",      roles: ["HR"] },
      { key: "hr-social",     to: "/hr/social",     label: "Social Media", icon: "chat",     roles: ["HR"] },
    ],
  },
  {
    key: "administration", label: "Administration",
    children: [
      {
        key: "org", label: "Organization", icon: "building",
        children: [
          { key: "agencies",          to: "/admin/agencies",          label: "Agencies",          icon: "building", superAdminOnly: true },
          { key: "call-centers",      to: "/admin/call-centers",      label: "Call Centers",      icon: "building", module: "admin" },
          { key: "submission-agents", to: "/admin/submission-agents", label: "Submission Agents", icon: "shield",   superAdminOnly: true },
        ],
      },
      {
        key: "access", label: "Users & Access", icon: "users",
        children: [
          { key: "users",    to: "/admin/users",    label: "User Mgmt",       icon: "users",  module: "users.manage" },
          { key: "roles",    to: "/admin/roles",    label: "Role Management", icon: "shield", module: "roles.manage" },
          { key: "register", to: "/admin/register", label: "Register User",   icon: "plus",   module: "users.manage" },
          { key: "security", to: "/admin/security", label: "Security Center", icon: "shield", module: "roles.manage" },
        ],
      },
      {
        key: "platform", label: "Platform", icon: "cog",
        children: [
          { key: "admin",         to: "/admin",              label: "Admin",         icon: "cog",    module: "admin" },
          { key: "integrations",  to: "/admin/integrations", label: "Integrations",  icon: "filter", module: "admin" },
          { key: "audit",         to: "/admin/audit",        label: "Audit Log",     icon: "doc",    module: "admin" },
          { key: "chat-oversight", to: "/admin/chat-oversight", label: "Chat Oversight", icon: "chat", superAdminOnly: true },
        ],
      },
      { key: "confidential", to: "/confidential", label: "Confidential", icon: "lock", roles: ["Admin"] },
    ],
  },
];
