import { roleLabel } from "../constants/roles";
import { Suspense, useEffect, useMemo, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useDispatch, useSelector } from "react-redux";
import { clearAuth, type RootState } from "../../app/store";
import { Badge, Icon, type IconName, Spinner, cn } from "../ui";
import { useQueueCountsQuery } from "../api/baseApi";
import { useMyProfileQuery } from "../../features/profile/baseApi";
import { AvatarImage } from "../../features/profile/ProfileAvatar";
import type { QueueCounts } from "../api/types";
import { CallDock } from "../../features/softphone/CallDock";
import { ReportBugButton } from "../../features/bugs/ReportBugButton";
import { ContextSwitcher } from "../../features/context/ContextSwitcher";
import { CommandPaletteProvider, useCommandPalette } from "./CommandPalette";
import { BrandLogo } from "./BrandLogo";
import { NotificationsBell } from "./NotificationsBell";
import { ScrollToTop } from "./ScrollToTop";
import { RouteErrorBoundary } from "./RouteErrorBoundary";
import { AgentStatusBar } from "./AgentStatusBar";
import { usePersistentState } from "../hooks/usePersistentState";

/**
 * A node in the multi-level navigation tree. A node is either a LEAF (has `to`, links to a page)
 * or a BRANCH (has `children`, expands to reveal them). Top-level nodes are sections. Visibility
 * gates (module / roles / superAdminOnly) apply to leaves; a branch is shown when any descendant is.
 */
interface NavNode {
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

const NAV: NavNode[] = [
  {
    key: "workspace", label: "Workspace",
    children: [
      { key: "dashboard", to: "/dashboard", label: "Dashboard", icon: "dashboard", module: "dashboard" },
      { key: "calendar",  to: "/calendar",  label: "Calendar",  icon: "calendar" },
      { key: "queue",     to: "/queue",     label: "My Queue",  icon: "inbox", module: "queue", countKey: "myQueue" },
      { key: "chat",      to: "/chat",      label: "Chat",      icon: "chat",  module: "chat" },
      { key: "pulse",     to: "/pulse",     label: "Pulse",     icon: "activity" },
      { key: "callbacks", to: "/callbacks", label: "Callbacks", icon: "calendar", module: "callbacks", countKey: "callbacks" },
      {
        key: "intake", label: "Intake Pipeline", icon: "filter",
        children: [
          { key: "intake-form",   to: "/intake",          label: "Lead Intake",      icon: "plus",      roles: ["Fronter"] },
          { key: "verify-queue",  to: "/verify-queue",    label: "Verifier Queue",   icon: "check",     roles: ["Verifier"], countKey: "verifierQueue" },
          { key: "close-queue",   to: "/close-queue",     label: "Closer Queue",     icon: "briefcase", roles: ["Closer"], countKey: "closerQueue" },
          { key: "validate-queue", to: "/validate-queue", label: "Submission Queue", icon: "shield",    roles: ["Validator"], countKey: "submissionQueue" },
          { key: "my-sales",      to: "/my-sales",        label: "My Sales",         icon: "briefcase", roles: ["LicenseAgent"] },
        ],
      },
      { key: "agent", to: "/agent", label: "Agent Panel", icon: "phone", module: "agent" },
      { key: "team",  to: "/team",  label: "Team",        icon: "users" },
      { key: "guide", to: "/guide", label: "Guide",       icon: "book" },
      { key: "bugs",  to: "/bugs",  label: "Bugs",        icon: "alert" },
    ],
  },
  {
    key: "pipeline", label: "Pipeline",
    children: [
      { key: "leads", to: "/leads", label: "Leads", icon: "list", module: "leads" },
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
      { key: "commissions", to: "/commissions", label: "Commissions", icon: "doc",       module: "commissions" },
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

interface NavCtx { modules: string[]; roles: string[]; isAdmin: boolean; isSuperAdmin: boolean }

function leafAllowed(n: NavNode, c: NavCtx): boolean {
  if (n.superAdminOnly) return c.isSuperAdmin;
  if (n.roles) return c.isAdmin || c.isSuperAdmin || n.roles.some((r) => c.roles.includes(r));
  return !n.module || c.isAdmin || c.isSuperAdmin || c.modules.includes(n.module);
}

/** Prune the tree to what the user may see (a branch survives if any descendant does). */
function visibleTree(nodes: NavNode[], c: NavCtx): NavNode[] {
  const out: NavNode[] = [];
  for (const n of nodes) {
    if (n.children) {
      const kids = visibleTree(n.children, c);
      if (kids.length > 0 || (n.to && leafAllowed(n, c))) out.push({ ...n, children: kids.length ? kids : undefined });
    } else if (leafAllowed(n, c)) {
      out.push(n);
    }
  }
  return out;
}

/** All leaf descendants, in order — used by the collapsed icon rail. */
function flattenLeaves(nodes: NavNode[]): NavNode[] {
  return nodes.flatMap((n) => (n.children ? flattenLeaves(n.children) : [n]));
}

/** Keys from root to the deepest node whose `to` best-matches the current path (longest prefix wins). */
function findActivePath(nodes: NavNode[], pathname: string): string[] {
  let best: string[] = [];
  let bestLen = -1;
  const walk = (list: NavNode[], trail: string[]) => {
    for (const n of list) {
      const path = [...trail, n.key];
      if (n.to && (pathname === n.to || pathname.startsWith(n.to + "/")) && n.to.length > bestLen) {
        best = path; bestLen = n.to.length;
      }
      if (n.children) walk(n.children, path);
    }
  };
  walk(nodes, []);
  return best;
}

/** Labels + optional route for the active trail, for the breadcrumb bar. */
function breadcrumbTrail(nodes: NavNode[], pathname: string): { label: string; to?: string }[] {
  const keys = findActivePath(nodes, pathname);
  const trail: { label: string; to?: string }[] = [];
  let level = nodes;
  for (const k of keys) {
    const node = level.find((n) => n.key === k);
    if (!node) break;
    trail.push({ label: node.label, to: node.to });
    level = node.children ?? [];
  }
  return trail;
}

export function Layout() {
  return (
    <CommandPaletteProvider>
      <ScrollToTop />
      <LayoutInner />
    </CommandPaletteProvider>
  );
}

function LayoutInner() {
  const auth = useSelector((s: RootState) => s.auth);
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const location = useLocation();
  const [collapsed, setCollapsed] = usePersistentState<boolean>("ui.sidebar.collapsed", false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const visibleNav = useMemo(() => {
    const roles = auth.user?.roles ?? [];
    return visibleTree(NAV, {
      modules: auth.user?.modules ?? [],
      roles,
      isSuperAdmin: roles.includes("SuperAdmin"),
      isAdmin: roles.includes("Admin"),
    });
  }, [auth.user?.modules, auth.user?.roles]);

  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener("mousedown", onClick);
    return () => document.removeEventListener("mousedown", onClick);
  }, []);

  // Close the mobile nav drawer whenever the route changes (link tap, palette nav, …).
  useEffect(() => { setMobileNavOpen(false); }, [location.pathname]);

  // Lock body scroll while the mobile drawer is open so the page behind doesn't move.
  useEffect(() => {
    if (!mobileNavOpen) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") setMobileNavOpen(false); };
    window.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = prev;
      window.removeEventListener("keydown", onKey);
    };
  }, [mobileNavOpen]);

  const userName = auth.user?.userName ?? "User";
  const primaryRole = roleLabel(auth.user?.roles[0] ?? "Member");

  // The signed-in user's own profile — drives the header avatar (uploaded photo when set).
  // Skipped during onboarding, where non-auth API calls are gated server-side.
  const onboarding = !!(auth.user?.mustChangePassword || auth.user?.twoFactorSetupRequired);
  const { data: myProfile } = useMyProfileQuery(undefined, { skip: !auth.accessToken || onboarding });

  return (
    // overflow-x-clip is a mobile safety net: no stray-wide descendant can ever
    // push the whole page sideways. `clip` (not `hidden`) keeps sticky headers /
    // the fixed drawer working since it doesn't create a scroll container.
    <div className="min-h-screen flex overflow-x-clip">
      {/* Desktop sidebar — hidden below lg, where the drawer takes over */}
      <aside
        className={cn(
          "hidden lg:flex sticky top-0 h-screen flex-shrink-0 flex-col text-ink-700",
          // Light sidebar — white with a soft sky tint at the bottom, paired with a
          // hairline divider on the right. Matches the rest of the light theme.
          // (No `relative` — `sticky` already positions it + is a containing block for the
          // decorative absolute children; keeping both invites the drawer's fixed/relative trap.)
          "bg-gradient-to-b from-white via-white to-brand-50/60 overflow-hidden",
          "border-r border-ink-200/70",
          "transition-[width] duration-200",
          collapsed ? "w-[72px]" : "w-64 xl:w-72 2xl:w-80",
        )}
      >
        <SidebarContent
          nav={visibleNav}
          collapsed={collapsed}
          onToggleCollapse={() => setCollapsed((c) => !c)}
        />
      </aside>

      {/* Mobile drawer backdrop */}
      <div
        className={cn(
          "lg:hidden fixed inset-0 z-40 bg-ink-950/40 backdrop-blur-sm transition-opacity duration-300",
          mobileNavOpen ? "opacity-100" : "opacity-0 pointer-events-none",
        )}
        onClick={() => setMobileNavOpen(false)}
        aria-hidden
      />
      {/* Mobile off-canvas drawer — always expanded, slides in from the left */}
      <aside
        className={cn(
          // NOTE: no `relative` here — it would override `fixed` (Tailwind emits .relative
          // after .fixed), pulling the drawer back into flow so it steals ~320px of the row
          // and squishes the page into a narrow column on mobile. `fixed` already makes this
          // a containing block for the decorative absolute children below.
          "lg:hidden fixed inset-y-0 left-0 z-50 w-[min(84vw,20rem)] flex flex-col text-ink-700",
          "bg-gradient-to-b from-white via-white to-brand-50/60 overflow-hidden shadow-float",
          "border-r border-ink-200/70 transition-transform duration-300 ease-out-quint",
          mobileNavOpen ? "translate-x-0" : "-translate-x-full",
        )}
        role="dialog"
        aria-modal="true"
        aria-label="Navigation menu"
      >
        <SidebarContent
          nav={visibleNav}
          collapsed={false}
          mobile
          onClose={() => setMobileNavOpen(false)}
        />
      </aside>

      <div className="flex-1 min-w-0 flex flex-col">
        <header className="sticky top-0 z-30 h-16 bg-white/75 backdrop-saturate-160 border-b hairline flex items-center gap-3 sm:gap-4 px-4 sm:px-6">
          <button
            onClick={() => setMobileNavOpen(true)}
            className="lg:hidden -ml-1 p-2 rounded-lg text-ink-600 hover:bg-ink-100/70 hover:text-ink-900 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
            aria-label="Open navigation menu"
          >
            <Icon name="menu" size={20} />
          </button>
          <div className="flex-1 min-w-0 max-w-xl">
            <PaletteTrigger />
          </div>

          <ContextSwitcher />
          <NotificationsBell />

          <div className="h-6 w-px bg-ink-200" />

          <div className="relative" ref={menuRef}>
            <button
              onClick={() => setMenuOpen((o) => !o)}
              className="flex items-center gap-3 pr-2 pl-1 py-1 rounded-xl hover:bg-ink-100/60 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
            >
              <AvatarImage userId={auth.user?.id ?? ""} hasAvatar={!!myProfile?.hasAvatar} name={userName} size={32} />
              <div className="text-left hidden sm:block min-w-0">
                <div className="text-sm font-medium text-ink-900 leading-tight truncate">{userName}</div>
                <div className="text-[11px] text-ink-500 leading-tight truncate">{primaryRole}</div>
              </div>
            </button>
            {menuOpen && (
              <div className="absolute right-0 mt-2 w-64 surface-elevated overflow-hidden animate-scale-in">
                <div className="px-4 py-3 border-b hairline bg-gradient-to-b from-brand-soft to-white">
                  <div className="text-sm font-semibold text-ink-900 truncate">{userName}</div>
                  <div className="text-xs text-ink-500 truncate">{auth.user?.email}</div>
                  <div className="mt-2 flex flex-wrap gap-1">
                    {auth.user?.roles.map((r) => (
                      <Badge key={r} tone="brand" variant="soft">{roleLabel(r)}</Badge>
                    ))}
                  </div>
                </div>
                <nav className="py-1.5 text-sm">
                  <button
                    onClick={() => { setMenuOpen(false); navigate("/profile"); }}
                    className="w-full flex items-center gap-2.5 px-4 py-2 text-ink-700 hover:bg-ink-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500/40"
                  >
                    <Icon name="user" size={16} /> My Profile
                  </button>
                  <button
                    onClick={() => { setMenuOpen(false); navigate("/2fa"); }}
                    className="w-full flex items-center gap-2.5 px-4 py-2 text-ink-700 hover:bg-ink-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500/40"
                  >
                    <Icon name="shield" size={16} /> Security & 2FA
                  </button>
                  <button
                    onClick={() => { setMenuOpen(false); navigate("/dashboard"); }}
                    className="w-full flex items-center gap-2.5 px-4 py-2 text-ink-700 hover:bg-ink-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500/40"
                  >
                    <Icon name="cog" size={16} /> Preferences
                  </button>
                </nav>
                <div className="border-t hairline p-1.5">
                  <button
                    onClick={() => { dispatch(clearAuth()); navigate("/login"); }}
                    className="w-full flex items-center gap-2.5 px-3 py-2 rounded-md text-rose-600 hover:bg-rose-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose-500/40"
                  >
                    <Icon name="logout" size={16} /> Sign out
                  </button>
                </div>
              </div>
            )}
          </div>
        </header>

        {/* Persistent shift bar — only renders for users with the `agent` module */}
        <AgentStatusBar />

        <main className="flex-1 overflow-auto">
          <div className="max-w-[1920px] 2xl:max-w-[2200px] mx-auto p-4 sm:p-6 lg:p-8 xl:p-10 2xl:p-12">
            <Breadcrumbs />
            {/* Per-page error boundary keeps a single crashing page from taking
                down the whole shell — the nav stays usable. Wraps the Suspense
                boundary for the route-split (React.lazy) pages. See router.tsx. */}
            <RouteErrorBoundary>
              <Suspense fallback={<div className="p-10 flex justify-center"><Spinner /></div>}>
                <Outlet />
              </Suspense>
            </RouteErrorBoundary>
          </div>
        </main>
      </div>
      <CallDock />
      <ReportBugButton />
    </div>
  );
}

/**
 * Sidebar chrome — brand, grouped nav, footer. Rendered twice: as the sticky
 * desktop `<aside>` (with a collapse toggle) and as the mobile off-canvas drawer
 * (always expanded, with a close button). The parent `<aside>` owns width /
 * position / slide animation; this component only paints the interior.
 */
const DEFAULT_EXPANDED = NAV.map((n) => n.key);

function SidebarContent({
  nav: visibleNav, collapsed, onToggleCollapse, mobile = false, onClose,
}: {
  nav: NavNode[];
  collapsed: boolean;
  onToggleCollapse?: () => void;
  mobile?: boolean;
  onClose?: () => void;
}) {
  const location = useLocation();
  // Which branches are expanded (persisted). The active route's ancestors are always treated as
  // open (union below), so the section you're in is never collapsed out from under you.
  const [expandedArr, setExpandedArr] = usePersistentState<string[]>("ui.sidebar.expanded.v2", DEFAULT_EXPANDED);
  const activePath = useMemo(() => findActivePath(visibleNav, location.pathname), [visibleNav, location.pathname]);
  const activeKeys = useMemo(() => new Set(activePath), [activePath]);
  const expanded = useMemo(() => new Set([...expandedArr, ...activePath]), [expandedArr, activePath]);
  const toggle = (key: string) =>
    setExpandedArr((prev) => (prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]));
  // Show which agency the signed-in user belongs to (SuperAdmin/central users
  // have no agency and see the platform label instead).
  const agencyName = useSelector((s: RootState) => s.auth.user?.agencyName);
  const callCenterName = useSelector((s: RootState) => s.auth.user?.callCenterName);
  const isSuperAdmin = useSelector((s: RootState) => s.auth.user?.roles?.includes("SuperAdmin") ?? false);
  const orgLabel = agencyName || (isSuperAdmin ? "Platform Admin" : "Insurance Agency");

  // Live "N waiting" counts for the queue nav items (RTK dedupes across the two sidebar instances).
  const token = useSelector((s: RootState) => s.auth.accessToken);
  const onboarding = useSelector((s: RootState) => !!(s.auth.user?.mustChangePassword || s.auth.user?.twoFactorSetupRequired));
  const { data: counts } = useQueueCountsQuery(undefined, { skip: !token || onboarding, pollingInterval: 30_000 });
  return (
    <>
      {/* Soft brand glow — bottom corner only, very subtle */}
      <div className="pointer-events-none absolute -bottom-24 -right-16 h-72 w-72 rounded-full bg-brand-500/10 blur-3xl" />

      {/* Brand */}
      <div className="relative h-16 flex items-center gap-2.5 px-4 border-b border-ink-200/70">
        <BrandLogo
          variant="mark"
          size={collapsed ? 32 : 36}
          className="drop-shadow-[0_2px_12px_rgba(60,114,105,0.30)]"
        />
        {!collapsed && (
          <div className="leading-tight min-w-0">
            <div className="text-sm font-semibold text-ink-900 tracking-tight">Rock Communication</div>
            <div className="text-[10px] uppercase tracking-[0.18em] text-ink-500 truncate max-w-[160px]" title={orgLabel}>{orgLabel}</div>
            {callCenterName && (
              <div
                className="mt-0.5 text-[10px] uppercase tracking-[0.14em] text-brand-600 font-medium truncate max-w-[160px] flex items-center gap-1"
                title={`Call center: ${callCenterName}`}
              >
                <span aria-hidden className="inline-block h-1 w-1 rounded-full bg-brand-400 shrink-0" />
                {callCenterName}
              </div>
            )}
          </div>
        )}
        {mobile && (
          <button
            onClick={onClose}
            className="ml-auto -mr-1 p-2 rounded-lg text-ink-500 hover:bg-ink-100 hover:text-ink-900 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
            aria-label="Close navigation menu"
          >
            <Icon name="x" size={18} />
          </button>
        )}
      </div>

      <nav className="relative flex-1 overflow-y-auto py-4 px-2.5 space-y-2">
        {collapsed
          ? <CollapsedRail nav={visibleNav} counts={counts} />
          : visibleNav.map((section) => (
              <SidebarSection
                key={section.key} section={section}
                expanded={expanded} activeKeys={activeKeys} toggle={toggle} counts={counts}
              />
            ))}
      </nav>

      <div className="relative border-t border-ink-200/70 p-2.5 space-y-2">
        {!collapsed && (
          <div className="px-2 py-1.5 flex items-center gap-2 rounded-lg bg-emerald-50 ring-1 ring-emerald-100">
            <span className="relative flex h-2 w-2">
              <span className="absolute inset-0 rounded-full bg-emerald-500 animate-ping opacity-75" />
              <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500" />
            </span>
            <span className="text-[10px] uppercase tracking-[0.14em] font-semibold text-emerald-700">
              Production
            </span>
            <span className="ml-auto text-[10px] text-ink-500 font-mono tabular-nums">v1.0</span>
          </div>
        )}
        {/* Collapse toggle is desktop-only; the mobile drawer closes via backdrop / X. */}
        {!mobile && (
          <button
            onClick={onToggleCollapse}
            className="w-full flex items-center justify-center gap-2 text-xs text-ink-500 hover:text-ink-900 py-2 rounded-lg hover:bg-ink-100/70 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
            title={collapsed ? "Expand" : "Collapse"}
          >
            <Icon name={collapsed ? "arrowRight" : "menu"} size={16} />
            {!collapsed && <span>Collapse</span>}
          </button>
        )}
      </div>
    </>
  );
}

interface NavRenderProps {
  expanded: Set<string>;
  activeKeys: Set<string>;
  toggle: (key: string) => void;
  counts?: QueueCounts;
}

/** A live queue-count pill / static badge for a leaf. */
function CountBadge({ node, counts }: { node: NavNode; counts?: QueueCounts }) {
  const c = node.countKey ? (counts?.[node.countKey] ?? 0) : 0;
  if (node.countKey && c > 0)
    return <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-brand-500 text-white shadow-sm tabular-nums">{c > 99 ? "99+" : c}</span>;
  if (node.badge)
    return <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-brand-500 text-white shadow-sm">{node.badge}</span>;
  return null;
}

/** A top-level, collapsible section (uppercase header + chevron). */
function SidebarSection({ section, expanded, activeKeys, toggle, counts }: { section: NavNode } & NavRenderProps) {
  const open = expanded.has(section.key);
  return (
    <div>
      <button
        type="button" onClick={() => toggle(section.key)} aria-expanded={open}
        className="group w-full px-2 pb-1.5 pt-1 flex items-center gap-2 focus-visible:outline-none"
      >
        <span className="text-[10px] font-semibold uppercase tracking-[0.14em] text-ink-500 group-hover:text-ink-700 transition-colors">
          {section.label}
        </span>
        <span className="flex-1 h-px bg-gradient-to-r from-ink-200/80 via-ink-100 to-transparent" />
        <Icon name="chevronDown" size={12} className={cn("text-ink-400 transition-transform duration-200", !open && "-rotate-90")} />
      </button>
      {open && (
        <div className="space-y-0.5 pb-1">
          {(section.children ?? []).map((child) => (
            <NavRow key={child.key} node={child} expanded={expanded} activeKeys={activeKeys} toggle={toggle} counts={counts} />
          ))}
        </div>
      )}
    </div>
  );
}

/** A nested row — a collapsible parent (icon + label + chevron) or a leaf link. Recurses. */
function NavRow({ node, expanded, activeKeys, toggle, counts }: { node: NavNode } & NavRenderProps) {
  if (node.children) {
    const open = expanded.has(node.key);
    const active = activeKeys.has(node.key);
    return (
      <div>
        <button
          type="button" onClick={() => toggle(node.key)} aria-expanded={open}
          className={cn(
            "group w-full flex items-center gap-3 px-2.5 py-2 rounded-lg text-sm font-medium transition-all",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40",
            active ? "text-brand-700" : "text-ink-600 hover:bg-ink-100/80 hover:text-ink-900",
          )}
        >
          {node.icon && <Icon name={node.icon} size={18} className={cn("shrink-0 transition-colors", active ? "text-brand-600" : "text-ink-500 group-hover:text-ink-900")} />}
          <span className="flex-1 truncate text-left">{node.label}</span>
          <Icon name="chevronDown" size={14} className={cn("shrink-0 text-ink-400 transition-transform duration-200", !open && "-rotate-90")} />
        </button>
        {open && (
          <div className="mt-0.5 ml-[18px] pl-2 border-l border-ink-200/60 space-y-0.5">
            {node.children.map((child) => (
              <NavRow key={child.key} node={child} expanded={expanded} activeKeys={activeKeys} toggle={toggle} counts={counts} />
            ))}
          </div>
        )}
      </div>
    );
  }
  return (
    <NavLink
      to={node.to!}
      className={({ isActive }) =>
        cn(
          "group relative flex items-center gap-3 px-2.5 py-2 rounded-lg text-sm font-medium transition-all duration-150",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40",
          isActive
            ? "text-brand-700 bg-brand-50 ring-1 ring-brand-100 shadow-[0_1px_2px_0_rgba(60,114,105,0.06)]"
            : "text-ink-600 hover:bg-ink-100/80 hover:text-ink-900",
        )
      }
    >
      {({ isActive }) => (
        <>
          {isActive && <span className="absolute left-0 top-1.5 bottom-1.5 w-0.5 rounded-r-full bg-gradient-to-b from-brand-400 to-brand-600 shadow-[0_0_8px_rgba(60,114,105,0.45)]" />}
          {node.icon && <Icon name={node.icon} size={17} className={cn("shrink-0 transition-colors", isActive ? "text-brand-600" : "text-ink-500 group-hover:text-ink-900")} />}
          <span className="flex-1 truncate">{node.label}</span>
          <CountBadge node={node} counts={counts} />
        </>
      )}
    </NavLink>
  );
}

/** Collapsed sidebar: an icon rail of every leaf, grouped by section with a divider + count dots. */
function CollapsedRail({ nav, counts }: { nav: NavNode[]; counts?: QueueCounts }) {
  return (
    <div className="space-y-1">
      {nav.map((section, si) => (
        <div key={section.key}>
          {si > 0 && <div className="mx-3 my-2 h-px bg-ink-200/60" aria-hidden />}
          <div className="space-y-0.5">
            {flattenLeaves(section.children ?? [section]).map((leaf) => (
              <NavLink
                key={leaf.key} to={leaf.to!} title={leaf.label}
                className={({ isActive }) =>
                  cn(
                    "group relative flex items-center justify-center py-2 rounded-lg transition-colors",
                    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40",
                    isActive ? "bg-brand-50 ring-1 ring-brand-100" : "hover:bg-ink-100/80",
                  )
                }
              >
                {({ isActive }) => (
                  <>
                    <Icon name={leaf.icon ?? "dashboard"} size={18} className={cn("transition-colors", isActive ? "text-brand-600" : "text-ink-500 group-hover:text-ink-900")} />
                    {leaf.countKey && (counts?.[leaf.countKey] ?? 0) > 0 && (
                      <span className="absolute top-1 right-1 h-2 w-2 rounded-full bg-brand-500 ring-2 ring-white" aria-hidden />
                    )}
                  </>
                )}
              </NavLink>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

/** Enterprise breadcrumb trail (Section › Group › Page), derived from the nav tree + current route. */
function Breadcrumbs() {
  const location = useLocation();
  const trail = useMemo(() => breadcrumbTrail(NAV, location.pathname), [location.pathname]);
  if (trail.length <= 1) return null; // top-level pages (Dashboard, etc.) don't need a crumb trail
  return (
    <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-1.5 text-xs text-ink-500 flex-wrap">
      <NavLink to="/dashboard" className="hover:text-ink-800 transition-colors inline-flex items-center" aria-label="Home">
        <Icon name="home" size={13} />
      </NavLink>
      {trail.map((c, i) => {
        const last = i === trail.length - 1;
        return (
          <span key={i} className="inline-flex items-center gap-1.5">
            <Icon name="chevronRight" size={12} className="text-ink-300" />
            {c.to && !last
              ? <NavLink to={c.to} className="hover:text-ink-800 transition-colors">{c.label}</NavLink>
              : <span className={cn(last && "text-ink-800 font-medium")}>{c.label}</span>}
          </span>
        );
      })}
    </nav>
  );
}

/**
 * Compact "search" affordance in the header — looks like a search box but
 * actually opens the command palette. Communicates the ⌘K shortcut visually.
 */
function PaletteTrigger() {
  const palette = useCommandPalette();
  return (
    <button
      type="button"
      onClick={palette.open}
      className={cn(
        "w-full h-10 px-3.5 flex items-center gap-2.5 rounded-lg",
        "bg-ink-50/80 hover:bg-white border border-transparent hover:border-ink-200",
        "text-left text-sm text-ink-500 hover:text-ink-700 transition-colors",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40 focus-visible:bg-white",
      )}
    >
      <Icon name="search" size={16} className="text-ink-400" />
      <span className="flex-1 truncate">Search or jump to anything…</span>
      <span className="hidden sm:inline-flex items-center gap-1">
        <kbd className="font-mono text-[10px] px-1.5 py-0.5 rounded border border-ink-200 text-ink-500 bg-white">⌘</kbd>
        <kbd className="font-mono text-[10px] px-1.5 py-0.5 rounded border border-ink-200 text-ink-500 bg-white">K</kbd>
      </span>
    </button>
  );
}

