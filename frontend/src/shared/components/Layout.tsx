import { useEffect, useMemo, useRef, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useDispatch, useSelector } from "react-redux";
import { clearAuth, type RootState } from "../../app/store";
import { Avatar, Badge, Icon, type IconName, cn } from "../ui";
import { CallDock } from "../../features/softphone/CallDock";
import { CommandPaletteProvider, useCommandPalette } from "./CommandPalette";
import { BrandLogo } from "./BrandLogo";
import { NotificationsBell } from "./NotificationsBell";
import { ScrollToTop } from "./ScrollToTop";
import { AgentStatusBar } from "./AgentStatusBar";
import { usePersistentState } from "../hooks/usePersistentState";

interface NavItem {
  to: string;
  label: string;
  icon: IconName;
  /** Module code from the backend ModuleCatalog. Visibility is driven by user.modules. */
  module?: string;
  /** When true, the item is hidden unless the user holds the SuperAdmin role. */
  superAdminOnly?: boolean;
  /** When set, the item is shown only to these roles (plus Admin / SuperAdmin). */
  roles?: string[];
  badge?: string;
}
interface NavGroup { label: string; items: NavItem[] }

const groups: NavGroup[] = [
  {
    label: "Workspace",
    items: [
      { to: "/dashboard", label: "Dashboard",   icon: "dashboard", module: "dashboard" },
      { to: "/team",      label: "Team",        icon: "users",     module: "team" },
      { to: "/agent",     label: "Agent Panel", icon: "phone",     module: "agent" },
      { to: "/queue",     label: "My Queue",    icon: "inbox",     module: "queue" },
      { to: "/intake",       label: "Lead Intake",    icon: "plus",  roles: ["Fronter"] },
      { to: "/verify-queue", label: "Verifier Queue", icon: "check", roles: ["Verifier"] },
      { to: "/close-queue",  label: "Closer Queue",   icon: "briefcase", roles: ["Closer"] },
      { to: "/callbacks", label: "Callbacks",   icon: "calendar",  module: "callbacks" },
      { to: "/chat",      label: "Chat",        icon: "chat",      module: "chat" },
    ],
  },
  {
    label: "Pipeline",
    items: [
      { to: "/leads",        label: "Leads",          icon: "list",      module: "leads" },
      { to: "/leads/search", label: "Search & Dedup", icon: "search",    module: "leads.search" },
      { to: "/leads/troubleshoot", label: "Troubleshoot", icon: "shield", module: "leads" },
      { to: "/lists",        label: "Lead Lists",     icon: "inbox",     module: "leads" },
      { to: "/cadences",     label: "Cadences",       icon: "filter",    module: "campaigns" },
      { to: "/sales",        label: "Sales",          icon: "briefcase", module: "sales" },
      { to: "/calls",        label: "Call History",   icon: "phone",     module: "calls" },
      { to: "/commissions",  label: "Commissions",    icon: "doc",       module: "commissions" },
    ],
  },
  {
    label: "Operations",
    items: [
      { to: "/supervisor", label: "Supervisor", icon: "shield", module: "supervisor" },
      { to: "/wallboard",  label: "Wallboard",  icon: "chart",  module: "reports" },
      { to: "/kpis",       label: "KPIs",       icon: "chart",  module: "reports" },
      { to: "/queues",     label: "Queues + IVR", icon: "phone", module: "campaigns" },
      { to: "/kb",         label: "Knowledge",  icon: "doc",    module: "knowledge" },
      { to: "/documents",  label: "Documents",  icon: "doc",    module: "documents" },
      { to: "/qa",         label: "QA Reviews", icon: "star",   module: "qa" },
      { to: "/qa/browser", label: "QA Browser", icon: "doc",    module: "qa" },
      { to: "/dnc",        label: "DNC List",   icon: "flag",   module: "dnc" },
      { to: "/campaigns",  label: "Campaigns",  icon: "target", module: "campaigns" },
      { to: "/scripts",    label: "Scripts",    icon: "doc",    module: "scripts" },
      { to: "/workflows",  label: "Workflows",  icon: "filter", module: "workflows" },
    ],
  },
  {
    label: "Administration",
    items: [
      { to: "/admin/agencies", label: "Call Centers",     icon: "building", superAdminOnly: true },
      { to: "/admin/users",    label: "User Mgmt",        icon: "users",    module: "users.manage" },
      { to: "/admin/roles",    label: "Role Management",  icon: "shield",   module: "roles.manage" },
      { to: "/admin/register", label: "Register User",    icon: "plus",     module: "users.manage" },
      { to: "/admin",              label: "Admin",            icon: "cog",      module: "admin" },
      { to: "/admin/integrations", label: "Integrations",     icon: "filter",   module: "admin" },
      { to: "/admin/audit",        label: "Audit Log",        icon: "doc",      module: "admin" },
    ],
  },
];

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
  const [collapsed, setCollapsed] = usePersistentState<boolean>("ui.sidebar.collapsed", false);
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const visibleGroups = useMemo(() => {
    const userModules = auth.user?.modules ?? [];
    const userRoles = auth.user?.roles ?? [];
    const isSuperAdmin = userRoles.includes("SuperAdmin");
    const isAdmin = userRoles.includes("Admin");
    return groups
      .map((g) => ({
        ...g,
        items: g.items.filter((i) => {
          if (i.superAdminOnly) return isSuperAdmin;
          if (i.roles) return isAdmin || isSuperAdmin || i.roles.some((r) => userRoles.includes(r));
          return !i.module || isAdmin || isSuperAdmin || userModules.includes(i.module);
        }),
      }))
      .filter((g) => g.items.length > 0);
  }, [auth.user?.modules, auth.user?.roles]);

  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener("mousedown", onClick);
    return () => document.removeEventListener("mousedown", onClick);
  }, []);

  const userName = auth.user?.userName ?? "User";
  const primaryRole = auth.user?.roles[0] ?? "Member";

  return (
    <div className="min-h-screen flex">
      <aside
        className={cn(
          "sticky top-0 h-screen flex-shrink-0 flex flex-col text-ink-700",
          // Light sidebar — white with a soft sky tint at the bottom, paired with a
          // hairline divider on the right. Matches the rest of the light theme.
          "bg-gradient-to-b from-white via-white to-brand-50/60 relative overflow-hidden",
          "border-r border-ink-200/70",
          "transition-[width] duration-200",
          collapsed ? "w-[72px]" : "w-64 xl:w-72 2xl:w-80",
        )}
      >
        {/* Soft brand glow — bottom corner only, very subtle */}
        <div className="pointer-events-none absolute -bottom-24 -right-16 h-72 w-72 rounded-full bg-brand-500/10 blur-3xl" />

        {/* Brand */}
        <div className="relative h-16 flex items-center gap-2.5 px-4 border-b border-ink-200/70">
          <BrandLogo
            variant="mark"
            size={collapsed ? 32 : 36}
            className="drop-shadow-[0_2px_12px_rgba(14,165,233,0.30)]"
          />
          {!collapsed && (
            <div className="leading-tight">
              <div className="text-sm font-semibold text-ink-900 tracking-tight">Rock Communication</div>
              <div className="text-[10px] uppercase tracking-[0.18em] text-ink-500">Insurance Agency</div>
            </div>
          )}
        </div>

        <nav className="relative flex-1 overflow-y-auto py-4 px-2.5 space-y-5">
          {visibleGroups.map((g, gi) => (
            <div key={g.label}>
              {!collapsed && (
                <div className="px-2 pb-2 flex items-center gap-2">
                  <span className="text-[10px] font-semibold uppercase tracking-[0.14em] text-ink-500">
                    {g.label}
                  </span>
                  <span className="flex-1 h-px bg-gradient-to-r from-ink-200/80 via-ink-100 to-transparent" />
                </div>
              )}
              {collapsed && gi > 0 && (
                <div className="mx-3 mb-3 h-px bg-ink-200/60" aria-hidden />
              )}
              <div className="space-y-0.5">
                {g.items.map((i) => (
                  <NavLink
                    key={i.to} to={i.to} title={collapsed ? i.label : undefined}
                    className={({ isActive }) =>
                      cn(
                        "group relative flex items-center gap-3 px-2.5 py-2 rounded-lg text-sm font-medium",
                        "transition-all duration-150",
                        isActive
                          ? "text-brand-700 bg-brand-50 ring-1 ring-brand-100 shadow-[0_1px_2px_0_rgba(14,165,233,0.06)]"
                          : "text-ink-600 hover:bg-ink-100/80 hover:text-ink-900",
                        collapsed && "justify-center",
                      )
                    }
                  >
                    {({ isActive }) => (
                      <>
                        {/* Active accent bar */}
                        {isActive && !collapsed && (
                          <span className="absolute left-0 top-1.5 bottom-1.5 w-0.5 rounded-r-full bg-gradient-to-b from-brand-400 to-brand-600 shadow-[0_0_8px_rgba(14,165,233,0.45)]" />
                        )}
                        <Icon
                          name={i.icon}
                          size={18}
                          className={cn(
                            "shrink-0 transition-colors",
                            isActive ? "text-brand-600" : "text-ink-500 group-hover:text-ink-900",
                          )}
                        />
                        {!collapsed && <span className="flex-1 truncate">{i.label}</span>}
                        {!collapsed && i.badge && (
                          <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-brand-500 text-white shadow-sm">
                            {i.badge}
                          </span>
                        )}
                      </>
                    )}
                  </NavLink>
                ))}
              </div>
            </div>
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
              <span className="ml-auto text-[10px] text-ink-500 font-mono">v1.0</span>
            </div>
          )}
          <button
            onClick={() => setCollapsed((c) => !c)}
            className="w-full flex items-center justify-center gap-2 text-xs text-ink-500 hover:text-ink-900 py-2 rounded-lg hover:bg-ink-100/70 transition-colors"
            title={collapsed ? "Expand" : "Collapse"}
          >
            <Icon name={collapsed ? "arrowRight" : "menu"} size={16} />
            {!collapsed && <span>Collapse</span>}
          </button>
        </div>
      </aside>

      <div className="flex-1 min-w-0 flex flex-col">
        <header className="sticky top-0 z-30 h-16 bg-white/75 backdrop-saturate-160 border-b hairline flex items-center gap-4 px-6">
          <div className="flex-1 max-w-xl">
            <PaletteTrigger />
          </div>

          <NotificationsBell />

          <div className="h-6 w-px bg-ink-200" />

          <div className="relative" ref={menuRef}>
            <button
              onClick={() => setMenuOpen((o) => !o)}
              className="flex items-center gap-3 pr-2 pl-1 py-1 rounded-xl hover:bg-ink-100/60 transition-colors"
            >
              <Avatar name={userName} size={32} />
              <div className="text-left hidden sm:block">
                <div className="text-sm font-medium text-ink-900 leading-tight">{userName}</div>
                <div className="text-[11px] text-ink-500 leading-tight">{primaryRole}</div>
              </div>
            </button>
            {menuOpen && (
              <div className="absolute right-0 mt-2 w-64 surface-elevated overflow-hidden animate-scale-in">
                <div className="px-4 py-3 border-b hairline bg-gradient-to-b from-brand-soft to-white">
                  <div className="text-sm font-semibold text-ink-900">{userName}</div>
                  <div className="text-xs text-ink-500 truncate">{auth.user?.email}</div>
                  <div className="mt-2 flex flex-wrap gap-1">
                    {auth.user?.roles.map((r) => (
                      <Badge key={r} tone="brand" variant="soft">{r}</Badge>
                    ))}
                  </div>
                </div>
                <nav className="py-1.5 text-sm">
                  <button
                    onClick={() => { setMenuOpen(false); navigate("/2fa"); }}
                    className="w-full flex items-center gap-2.5 px-4 py-2 text-ink-700 hover:bg-ink-50 transition-colors"
                  >
                    <Icon name="shield" size={16} /> Security & 2FA
                  </button>
                  <button
                    onClick={() => { setMenuOpen(false); navigate("/dashboard"); }}
                    className="w-full flex items-center gap-2.5 px-4 py-2 text-ink-700 hover:bg-ink-50 transition-colors"
                  >
                    <Icon name="cog" size={16} /> Preferences
                  </button>
                </nav>
                <div className="border-t hairline p-1.5">
                  <button
                    onClick={() => { dispatch(clearAuth()); navigate("/login"); }}
                    className="w-full flex items-center gap-2.5 px-3 py-2 rounded-md text-rose-600 hover:bg-rose-50 transition-colors"
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
          <div className="max-w-[1920px] 2xl:max-w-[2200px] mx-auto p-6 lg:p-8 xl:p-10 2xl:p-12">
            <Outlet />
          </div>
        </main>
      </div>
      <CallDock />
    </div>
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

