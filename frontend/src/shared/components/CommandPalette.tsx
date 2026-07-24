import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useDispatch, useSelector } from "react-redux";
import { clearAuth, type RootState } from "../../app/store";
import {
  useClockInMutation, useClockOutMutation, useDialLeadMutation,
  useListUsersQuery, useSearchKbQuery, useSearchLeadsQuery, useSetAgentStatusMutation,
} from "../api/baseApi";
import { Icon, useToast, type IconName } from "../ui";
import { cn } from "../ui/cn";

/**
 * Command palette ("⌘K"-style launcher).
 * - Open via context (`useCommandPalette().open()`), the global keyboard shortcut, or programmatically.
 * - Type to fuzzy-filter, arrow keys to navigate, Enter to run, Esc to dismiss.
 * - Items are filtered by the user's available modules so we never link them somewhere they can't go.
 */

interface PaletteItem {
  id: string;
  label: string;
  to?: string;
  /** Run a side-effect instead of (or in addition to) navigation. */
  onRun?: () => void;
  hint?: string;
  group: string;
  icon: IconName;
  /** Module code from the backend ModuleCatalog — used to filter. Omit for items everyone can access. */
  module?: string;
  keywords?: string[];
}

const ALL_ITEMS: PaletteItem[] = [
  // Workspace
  { id: "dashboard", label: "Go to Dashboard", to: "/dashboard", group: "Workspace", icon: "dashboard", module: "dashboard", keywords: ["home", "overview"] },
  { id: "agent",     label: "Open Agent Panel", to: "/agent", group: "Workspace", icon: "phone", module: "agent" },
  { id: "queue",     label: "My Queue", to: "/queue", group: "Workspace", icon: "inbox", module: "queue" },
  { id: "callbacks", label: "Callbacks", to: "/callbacks", group: "Workspace", icon: "calendar", module: "callbacks" },
  { id: "chat",      label: "Team Chat", to: "/chat", group: "Workspace", icon: "chat", module: "chat" },

  // Pipeline
  { id: "leads",          label: "Browse Leads", to: "/leads", group: "Pipeline", icon: "list", module: "leads" },
  { id: "leads-search",   label: "Search & Dedup leads", to: "/leads/search", group: "Pipeline", icon: "search", module: "leads.search", keywords: ["dedup", "duplicate", "find"] },
  { id: "lists",          label: "Lead Lists", to: "/lists", group: "Pipeline", icon: "inbox", module: "leads", keywords: ["list", "import"] },
  { id: "cadences",       label: "Cadences", to: "/cadences", group: "Pipeline", icon: "filter", module: "campaigns" },
  { id: "sales",          label: "Sales", to: "/sales", group: "Pipeline", icon: "briefcase", module: "sales" },
  { id: "commissions",    label: "Commissions", to: "/commissions", group: "Pipeline", icon: "doc", module: "commissions", keywords: ["pay", "earnings"] },

  // Operations
  { id: "supervisor", label: "Supervisor View", to: "/supervisor", group: "Operations", icon: "shield", module: "supervisor" },
  { id: "wallboard",  label: "Wallboard", to: "/wallboard", group: "Operations", icon: "chart", module: "reports" },
  { id: "kpis",       label: "KPIs", to: "/kpis", group: "Operations", icon: "chart", module: "reports", keywords: ["analytics", "metrics", "performance"] },
  { id: "queues",     label: "Queues + IVR", to: "/queues", group: "Operations", icon: "phone", module: "campaigns" },
  { id: "kb",         label: "Knowledge Base", to: "/kb", group: "Operations", icon: "doc", module: "knowledge", keywords: ["help", "articles", "docs"] },
  { id: "qa",         label: "QA Reviews", to: "/qa", group: "Operations", icon: "star", module: "qa" },
  { id: "qa-browser", label: "QA Browser", to: "/qa/browser", group: "Operations", icon: "doc", module: "qa" },
  { id: "dnc",        label: "DNC List", to: "/dnc", group: "Operations", icon: "flag", module: "dnc", keywords: ["do not call", "compliance"] },
  { id: "campaigns",  label: "Campaigns", to: "/campaigns", group: "Operations", icon: "target", module: "campaigns" },
  { id: "scripts",    label: "Scripts", to: "/scripts", group: "Operations", icon: "doc", module: "scripts" },
  { id: "workflows",  label: "Workflows", to: "/workflows", group: "Operations", icon: "filter", module: "workflows" },

  // Administration
  { id: "users-mgmt",   label: "User Management", to: "/admin/users", group: "Administration", icon: "users", module: "users.manage" },
  { id: "roles-mgmt",   label: "Role Management", to: "/admin/roles", group: "Administration", icon: "shield", module: "roles.manage", keywords: ["permissions", "rbac"] },
  { id: "register",     label: "Register a new user", to: "/admin/register", group: "Administration", icon: "plus", module: "users.manage", keywords: ["invite", "create user", "add user"] },
  { id: "admin",        label: "Admin Settings", to: "/admin", group: "Administration", icon: "building", module: "admin", keywords: ["settings", "config"] },

  // Account (always available)
  { id: "2fa",     label: "Security & 2FA", to: "/2fa", group: "Account", icon: "shield" },
  { id: "search",  label: "Search results", to: "/search", group: "Account", icon: "search", keywords: ["find", "lookup"] },
];

/* -------------------- score / filter -------------------- */

function scoreItem(item: PaletteItem, q: string): number {
  if (!q) return 1;
  const hay = `${item.label} ${item.group} ${(item.keywords ?? []).join(" ")}`.toLowerCase();
  const needle = q.toLowerCase();
  if (hay.startsWith(needle)) return 100;
  if (hay.includes(` ${needle}`)) return 80;
  if (hay.includes(needle)) return 60;
  // letter-subsequence fuzzy match
  let i = 0;
  for (const c of hay) {
    if (c === needle[i]) i++;
    if (i === needle.length) return 30;
  }
  return 0;
}

/* -------------------- context + provider -------------------- */

interface CommandPaletteApi {
  open: () => void;
  close: () => void;
  toggle: () => void;
}

const Ctx = createContext<CommandPaletteApi | null>(null);

export function useCommandPalette() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useCommandPalette must be used inside <CommandPaletteProvider>");
  return ctx;
}

export function CommandPaletteProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const api = useMemo<CommandPaletteApi>(() => ({
    open: () => setOpen(true),
    close: () => setOpen(false),
    toggle: () => setOpen((o) => !o),
  }), []);

  // Global ⌘K / Ctrl+K
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        api.toggle();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [api]);

  return (
    <Ctx.Provider value={api}>
      {children}
      {open && <Palette onClose={api.close} />}
    </Ctx.Provider>
  );
}

/* -------------------- the palette UI -------------------- */

function Palette({ onClose }: { onClose: () => void }) {
  const auth = useSelector((s: RootState) => s.auth);
  const userModules = auth.user?.modules ?? [];
  const isAdmin = (auth.user?.roles ?? []).includes("Admin");
  const navigate = useNavigate();
  const dispatch = useDispatch();
  const toast = useToast();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [q, setQ] = useState("");
  const [debounced, setDebounced] = useState("");
  const [active, setActive] = useState(0);
  const listRef = useRef<HTMLDivElement | null>(null);

  // Debounce server-side search 200ms
  useEffect(() => {
    const t = setTimeout(() => setDebounced(q.trim()), 200);
    return () => clearTimeout(t);
  }, [q]);

  // Server-side queries; only fire after 2 chars
  const { data: leads } = useSearchLeadsQuery(
    { name: debounced, take: 6 },
    { skip: debounced.length < 2 },
  );
  const { data: kb } = useSearchKbQuery(
    { q: debounced || undefined, publishedOnly: true },
    { skip: debounced.length < 2 },
  );
  const { data: users } = useListUsersQuery();

  const [dialLead] = useDialLeadMutation();
  const [clockIn] = useClockInMutation();
  const [clockOut] = useClockOutMutation();
  const [setAgentStatus] = useSetAgentStatusMutation();

  // Dynamic action items (clock-in, status changes, sign out, etc.)
  const actionItems: PaletteItem[] = useMemo(() => [
    {
      id: "act-clockin", label: "Clock in", group: "Actions", icon: "phone", keywords: ["start shift", "available"],
      onRun: async () => { try { await clockIn().unwrap(); toast.success("Clocked in"); } catch (e: any) { toast.error("Couldn't clock in", e?.data?.detail); } },
    },
    {
      id: "act-clockout", label: "Clock out", group: "Actions", icon: "x", keywords: ["end shift"],
      onRun: async () => { try { await clockOut().unwrap(); toast.success("Clocked out"); } catch (e: any) { toast.error("Couldn't clock out", e?.data?.detail); } },
    },
    {
      id: "act-status-available", label: "Set status: Available", group: "Actions", icon: "check", keywords: ["status", "ready"],
      onRun: async () => { try { await setAgentStatus({ status: "Available" }).unwrap(); toast.success("Now Available"); } catch (e: any) { toast.error("Couldn't change status", e?.data?.detail); } },
    },
    {
      id: "act-status-break", label: "Set status: Break", group: "Actions", icon: "clock", keywords: ["status", "pause"],
      onRun: async () => { try { await setAgentStatus({ status: "Break" }).unwrap(); toast.success("On break"); } catch (e: any) { toast.error("Couldn't change status", e?.data?.detail); } },
    },
    {
      id: "act-status-lunch", label: "Set status: Lunch", group: "Actions", icon: "clock", keywords: ["status", "afk"],
      onRun: async () => { try { await setAgentStatus({ status: "Lunch" }).unwrap(); toast.success("Lunch started"); } catch (e: any) { toast.error("Couldn't change status", e?.data?.detail); } },
    },
    {
      id: "act-signout", label: "Sign out", group: "Actions", icon: "x", keywords: ["log out", "logout"],
      onRun: () => { dispatch(clearAuth()); navigate("/login"); },
    },
  ], [clockIn, clockOut, dispatch, navigate, setAgentStatus, toast]);

  // Live result items (Leads / Users / Knowledge / Dial-this-lead)
  const liveItems: PaletteItem[] = useMemo(() => {
    if (debounced.length < 2) return [];
    const out: PaletteItem[] = [];

    if (leads) {
      for (const l of leads.slice(0, 6)) {
        const name = `${l.firstName} ${l.lastName}`.trim() || "(no name)";
        out.push({
          id: `lead-${l.id}`,
          label: name,
          hint: `${l.phoneNumber}${l.email ? ` · ${l.email}` : ""}${l.state ? ` · ${l.state}` : ""}`,
          group: "Leads",
          icon: "list",
          to: `/leads/${l.id}`,
        });
      }
      for (const l of leads.slice(0, 4)) {
        out.push({
          id: `dial-${l.id}`,
          label: `Dial ${l.firstName} ${l.lastName}`.trim(),
          hint: l.phoneNumber,
          group: "Dial",
          icon: "phone",
          onRun: async () => {
            try { await dialLead({ leadId: l.id }).unwrap(); toast.success("Calling…"); navigate(`/leads/${l.id}`); }
            catch (e: any) { toast.error("Couldn't dial", e?.data?.detail); }
          },
        });
      }
    }

    if (users) {
      const ql = debounced.toLowerCase();
      const matches = users.filter((u) =>
        u.userName.toLowerCase().includes(ql) ||
        u.email.toLowerCase().includes(ql) ||
        u.roles.some((r: string) => r.toLowerCase().includes(ql))
      ).slice(0, 5);
      for (const u of matches) {
        out.push({
          id: `user-${u.id}`,
          label: u.userName,
          hint: `${u.email} · ${u.roles.slice(0, 2).join(", ")}`,
          group: "Users",
          icon: "users",
          to: "/users",
        });
      }
    }

    if (kb) {
      for (const a of kb.slice(0, 4)) {
        out.push({
          id: `kb-${a.id}`,
          label: a.title,
          hint: a.category ?? a.tags ?? undefined,
          group: "Knowledge",
          icon: "doc",
          to: "/kb",
        });
      }
    }

    return out;
  }, [debounced, leads, users, kb, dialLead, navigate, toast]);

  // Filter to what the user can reach + score by query.
  const items = useMemo<PaletteItem[]>(() => {
    const accessible = ALL_ITEMS.filter((i) => !i.module || isAdmin || userModules.includes(i.module));
    const accessibleActions = actionItems; // actions are not module-gated; backend will reject if forbidden
    const navAndAct = [...accessibleActions, ...accessible];
    const scored = navAndAct
      .map((i) => ({ item: i, score: scoreItem(i, q.trim()) }))
      .filter((x) => x.score > 0)
      .sort((a, b) => b.score - a.score);
    // Prepend live results when present (they came from the server keyed on the query)
    return [...liveItems, ...scored.map((x) => x.item)];
  }, [q, userModules, isAdmin, liveItems, actionItems]);

  // Group preserving sort order (group as items first appear).
  const groups = useMemo<[string, PaletteItem[]][]>(() => {
    const map = new Map<string, PaletteItem[]>();
    for (const it of items) {
      const arr = map.get(it.group) ?? [];
      arr.push(it);
      map.set(it.group, arr);
    }
    return Array.from(map.entries());
  }, [items]);

  // Reset active when items shrink/grow.
  useEffect(() => { setActive(0); }, [q]);

  // Lock body scroll, focus input.
  useEffect(() => {
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    inputRef.current?.focus();
    return () => { document.body.style.overflow = prev; };
  }, []);

  function run(i: number) {
    const it = items[i];
    if (!it) return;
    onClose();
    if (it.onRun) it.onRun();
    if (it.to) navigate(it.to);
  }

  // Scroll active into view
  useEffect(() => {
    const el = listRef.current?.querySelector<HTMLElement>(`[data-i="${active}"]`);
    el?.scrollIntoView({ block: "nearest" });
  }, [active]);

  function onKeyDown(e: React.KeyboardEvent) {
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((a) => Math.min(items.length - 1, a + 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setActive((a) => Math.max(0, a - 1)); }
    else if (e.key === "Enter") { e.preventDefault(); run(active); }
    else if (e.key === "Escape") { e.preventDefault(); onClose(); }
  }

  return (
    <div className="fixed inset-0 z-[60] grid place-items-start pt-[12vh] p-4 animate-fade-in">
      <div className="absolute inset-0 bg-ink-900/55 backdrop-blur-sm" onClick={onClose} />

      <div
        role="dialog"
        aria-label="Command palette"
        className="relative w-full max-w-xl bg-white rounded-2xl shadow-pop border border-ink-200/70 overflow-hidden animate-scale-in"
        onKeyDown={onKeyDown}
      >
        {/* gradient strip */}
        <div className="absolute inset-x-0 top-0 h-1 bg-gradient-to-r from-brand-500 via-brand-600 to-accent-500" />

        {/* search */}
        <div className="flex items-center gap-3 px-4 pt-5 pb-3">
          <Icon name="search" size={18} className="text-ink-500 shrink-0" />
          <input
            ref={inputRef}
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Type a command, page, or person…"
            className="flex-1 bg-transparent text-base text-ink-900 placeholder-ink-400 focus:outline-none"
            autoFocus
          />
          <kbd className="text-[10px] font-mono px-1.5 py-0.5 rounded border border-ink-200 text-ink-500 bg-ink-50">esc</kbd>
        </div>

        <div className="border-t hairline" />

        {/* results */}
        <div ref={listRef} className="max-h-[55vh] overflow-y-auto py-2">
          {items.length === 0 ? (
            <div className="px-6 py-10 text-center text-sm text-ink-500">
              <Icon name="search" size={18} className="mx-auto mb-2 text-ink-400" />
              No matches for "{q}"
            </div>
          ) : (
            groups.map(([group, list], gi) => {
              const offset = items.indexOf(list[0]);
              return (
                <div key={group}>
                  {gi > 0 && <div className="h-px bg-ink-100 mx-3 my-1" />}
                  <div className="px-4 pt-2 pb-1 text-[10px] font-semibold uppercase tracking-[0.14em] text-ink-500">
                    {group}
                  </div>
                  {list.map((it, idx) => {
                    const i = offset + idx;
                    const isActive = i === active;
                    return (
                      <button
                        key={it.id}
                        data-i={i}
                        onMouseEnter={() => setActive(i)}
                        onClick={() => run(i)}
                        className={cn(
                          "w-full flex items-center gap-3 px-3 mx-1.5 py-2 rounded-lg text-left transition-colors",
                          isActive ? "bg-brand-50 text-brand-900" : "text-ink-800 hover:bg-ink-50"
                        )}
                      >
                        <span className={cn(
                          "h-8 w-8 rounded-lg grid place-items-center shrink-0",
                          isActive ? "bg-white text-brand-600 ring-1 ring-brand-200" : "bg-ink-100 text-ink-600"
                        )}>
                          <Icon name={it.icon} size={16} />
                        </span>
                        <span className="flex-1 min-w-0">
                          <span className="block text-sm font-medium truncate">{it.label}</span>
                          {it.hint && <span className="block text-[11px] text-ink-500 truncate">{it.hint}</span>}
                        </span>
                        {isActive && <Icon name="arrowRight" size={14} className="text-brand-600" />}
                      </button>
                    );
                  })}
                </div>
              );
            })
          )}
        </div>

        {/* footer */}
        <div className="flex items-center justify-between gap-3 px-4 py-2.5 border-t hairline bg-ink-50/60 text-[11px] text-ink-500">
          <div className="flex items-center gap-3">
            <span className="inline-flex items-center gap-1"><Kbd>↑</Kbd><Kbd>↓</Kbd> navigate</span>
            <span className="inline-flex items-center gap-1"><Kbd>↵</Kbd> open</span>
          </div>
          <span className="inline-flex items-center gap-1"><Kbd>⌘</Kbd><Kbd>K</Kbd> toggle</span>
        </div>
      </div>
    </div>
  );
}

function Kbd({ children }: { children: ReactNode }) {
  return (
    <kbd className="font-mono px-1 py-0.5 rounded border border-ink-200 text-ink-600 bg-white">
      {children}
    </kbd>
  );
}
