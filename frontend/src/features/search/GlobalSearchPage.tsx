import { roleLabel } from "../../shared/constants/roles";
import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import {
  useDialLeadMutation,
  useListUsersQuery,
  useSearchLeadsQuery,
} from "../../shared/api/baseApi";
import type { Lead, UserSummary } from "../../shared/api/types";
import {
  Avatar, Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, cn, EmptyState, ErrorState, Icon, InfoHint,
  PageHeader, SearchInput, Skeleton, Stat, Tabs, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { STAGE_TONE as stageTone, stageLabel } from "../../shared/constants/leadStage";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { formatPhone } from "../../shared/lib/format";
import { SEARCH_MSG } from "./messages";


function useDebounced<T>(value: T, delay = 300) {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return v;
}

type Tab = "all" | "leads" | "users";

/**
 * Global search results page. Search is one query that matches:
 *  - Leads: by phone fragment, email, or partial name (server-side via /api/leads/search)
 *  - Users: by username, email, or display name (client-side filter over /api/users)
 *
 * The header search bar in Layout.tsx pushes here with `?q=...`.
 */
export function GlobalSearchPage() {
  const [params, setParams] = useSearchParams();
  const initial = params.get("q") ?? "";
  const tabParam = (params.get("tab") as Tab | null) ?? "all";

  const [query, setQuery] = useState(initial);
  const [tab, setTab] = useState<Tab>(tabParam);
  const debounced = useDebounced(query);
  const hasQuery = debounced.trim().length >= 2;
  const navigate = useNavigate();
  const toast = useToast();

  // Adopt a fresh query pushed by the always-present header search while this page is already
  // mounted. Compare against `debounced` (what this page writes to the URL) so in-progress typing
  // is never clobbered — only a genuinely external navigation drives setQuery.
  useEffect(() => {
    const urlQ = params.get("q") ?? "";
    if (urlQ !== debounced) setQuery(urlQ);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  // Keep URL in sync so the result is shareable / bookmarkable / refresh-safe.
  useEffect(() => {
    const next = new URLSearchParams();
    if (debounced) next.set("q", debounced);
    if (tab !== "all") next.set("tab", tab);
    setParams(next, { replace: true });
  }, [debounced, tab, setParams]);

  // Heuristic: if it looks like a phone number search the phone field; if it has '@' use email; else name.
  const looksLikePhone = /^[\d\s()+\-.]{4,}$/.test(debounced);
  const looksLikeEmail = debounced.includes("@");

  const { data: leads, isFetching: leadsLoading, isError: leadsFailed, error: leadsError, refetch: refetchLeads } = useSearchLeadsQuery(
    {
      phone: looksLikePhone ? debounced : undefined,
      email: looksLikeEmail ? debounced : undefined,
      name: !looksLikePhone && !looksLikeEmail ? debounced : undefined,
      take: 50,
    },
    { skip: !hasQuery },
  );

  const { data: allUsers, isLoading: usersLoading } = useListUsersQuery(undefined, { skip: !hasQuery });

  const matchingUsers = useMemo<UserSummary[]>(() => {
    if (!hasQuery || !allUsers) return [];
    const q = debounced.toLowerCase();
    return allUsers.filter((u) =>
      u.userName.toLowerCase().includes(q)
      || u.email.toLowerCase().includes(q)
      || u.roles.some((r) => r.toLowerCase().includes(q))
    );
  }, [allUsers, debounced, hasQuery]);

  const { sorted: sortedUsers, dirFor: userDir, toggle: sortUser } = useTableSort(matchingUsers, {
    accessors: {
      roles: (u) => u.roles[0] ?? "",
      modules: (u) => u.modules?.length ?? 0,
    },
  });

  const leadCount = leads?.length ?? 0;
  const userCount = matchingUsers.length;
  const totalCount = leadCount + userCount;
  const loading = leadsLoading || usersLoading;

  const sel = useRowSelection((leads ?? []).map((l) => l.id));

  function exportSelected() {
    const chosen = (leads ?? []).filter((l) => sel.isSelected(l.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (l) => `${l.firstName} ${l.lastName}` },
      { header: "Phone", value: (l) => formatPhone(l.phoneNumber) },
      { header: "Stage", value: (l) => stageLabel(l.stage) },
    ], `search-leads-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(SEARCH_MSG.exportReadyTitle, SEARCH_MSG.exportReadyDesc(chosen.length));
  }

  const [dialLead] = useDialLeadMutation();
  async function handleDial(id: string, name: string) {
    try {
      await dialLead({ leadId: id }).unwrap();
      toast.success(SEARCH_MSG.dialingTitle, SEARCH_MSG.dialingDesc(name));
    } catch (err: unknown) {
      toast.error(SEARCH_MSG.dialFailed, getErrorDetail(err) ?? "");
    }
  }

  return (
    <>
      <PageHeader
        title="Search results"
        description={
          hasQuery
            ? `Showing matches for "${debounced}"`
            : "Type at least 2 characters to search across leads and users."
        }
      />

      <Card className="mb-6">
        <CardBody>
          <SearchInput
            value={query}
            onChange={setQuery}
            placeholder={SEARCH_MSG.searchPlaceholder}
            autoFocus
          />
          <div className="mt-3 flex items-center gap-2 text-xs text-ink-500">
            <Icon name="search" size={12} />
            <span className="tabular-nums">
              {looksLikePhone && "Phone search"}
              {looksLikeEmail && "Email search"}
              {!looksLikePhone && !looksLikeEmail && "Name / role search"}
              {hasQuery && (loading ? " · Searching…" : ` · ${totalCount} result${totalCount === 1 ? "" : "s"}`)}
            </span>
          </div>
        </CardBody>
      </Card>

      <div className="grid sm:grid-cols-3 gap-4 mb-6">
        <Stat label="Total matches" value={hasQuery ? totalCount : "—"} icon={<Icon name="search" size={16} />} />
        <Stat label="Leads" value={hasQuery ? leadCount : "—"} icon={<Icon name="list" size={16} />} />
        <Stat label="Users" value={hasQuery ? userCount : "—"} icon={<Icon name="users" size={16} />} />
      </div>

      <Tabs
        value={tab}
        onChange={(v) => setTab(v as Tab)}
        items={[
          { value: "all",   label: <span className="inline-flex items-center gap-1.5"><Icon name="layers" size={14} /> All <Badge tone="neutral" variant="soft">{hasQuery ? totalCount : 0}</Badge></span> },
          { value: "leads", label: <span className="inline-flex items-center gap-1.5"><Icon name="list" size={14} /> Leads <Badge tone="brand" variant="soft">{hasQuery ? leadCount : 0}</Badge></span> },
          { value: "users", label: <span className="inline-flex items-center gap-1.5"><Icon name="users" size={14} /> Users <Badge tone="info" variant="soft">{hasQuery ? userCount : 0}</Badge></span> },
        ]}
      />

      {!hasQuery ? (
        <Card>
          <CardBody>
            <EmptyState
              icon={<Icon name="search" size={20} />}
              title={SEARCH_MSG.startTypingTitle}
              description={SEARCH_MSG.startTypingDesc}
            />
          </CardBody>
        </Card>
      ) : loading && totalCount === 0 ? (
        <Card>
          <CardBody className="space-y-2">
            <Skeleton className="h-9" />
            <Skeleton className="h-9" />
            <Skeleton className="h-9" />
          </CardBody>
        </Card>
      ) : leadsFailed ? (
        // A failed search must never read as "no matches" — that tells people their records are
        // gone when the request simply broke.
        <Card>
          <CardBody>
            <ErrorState error={leadsError} resource={SEARCH_MSG.resourceName} onRetry={refetchLeads} />
          </CardBody>
        </Card>
      ) : totalCount === 0 ? (
        <Card>
          <CardBody>
            <EmptyState
              icon={<Icon name="search" size={20} />}
              title={SEARCH_MSG.noMatchesTitle}
              description={SEARCH_MSG.noMatchesDesc(debounced)}
            />
          </CardBody>
        </Card>
      ) : (
        <div className="space-y-6">
          {(tab === "all" || tab === "leads") && leadCount > 0 && (
            <Card>
              <CardHeader
                title={<span className="inline-flex items-center gap-2"><Icon name="list" size={16} className="text-brand-600" /> Leads <span className="text-ink-400 font-normal tabular-nums">· {leadCount}</span></span>}
                subtitle="Click a row to open the lead, or dial directly."
              />
              <CardBody className="p-0">
                <div className="overflow-x-auto">
                <Table className="border-0 shadow-none rounded-none">
                  <THead>
                    <TR>
                      <TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH>
                      <TH>Name</TH>
                      <TH>Phone</TH>
                      <TH>Email</TH>
                      <TH><span className="inline-flex items-center gap-1">Stage<InfoHint title="Stage" side="top">Where this lead sits in the sales pipeline — e.g. New, Contacted, Verified, or Sold.</InfoHint></span></TH>
                      <TH><span className="inline-flex items-center gap-1">State<InfoHint title="State" side="top">The US state the lead lives in — it drives carrier licensing and who can work the lead.</InfoHint></span></TH>
                      <TH className="sticky right-0 bg-ink-50 border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">Actions</TH>
                    </TR>
                  </THead>
                  <TBody>
                    {(leads ?? []).map((l: Lead) => (
                      <TR
                        key={l.id}
                        onClick={() => navigate(`/leads/${l.id}`)}
                        className={cn("cursor-pointer", sel.isSelected(l.id) && "bg-brand-50/40")}
                      >
                        <TD><Checkbox aria-label={`Select ${l.firstName} ${l.lastName}`} {...sel.checkboxProps(l.id)} /></TD>
                        <TD>
                          <div className="flex items-center gap-2.5">
                            <Avatar name={`${l.firstName} ${l.lastName}`} size={28} />
                            <div className="min-w-0">
                              <div className="text-sm font-medium text-ink-900 truncate">
                                {l.firstName} {l.lastName}
                              </div>
                              <div className="text-[11px] text-ink-500 font-mono tabular-nums">{l.id.slice(0, 8)}</div>
                            </div>
                          </div>
                        </TD>
                        <TD className="font-mono text-sm tabular-nums whitespace-nowrap">{formatPhone(l.phoneNumber)}</TD>
                        <TD className="text-sm text-ink-700 truncate max-w-[220px]">{l.email ?? "—"}</TD>
                        <TD>
                          <Badge tone={stageTone[l.stage] ?? "neutral"} variant="soft">{stageLabel(l.stage)}</Badge>
                        </TD>
                        <TD className="text-sm text-ink-700">{l.state ?? "—"}</TD>
                        <TD onClick={(e) => e.stopPropagation()} className="text-right sticky right-0 bg-white border-l hairline shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
                          <div className="inline-flex gap-1.5">
                            <Button
                              size="sm"
                              variant="outline"
                              leftIcon={<Icon name="phone" size={14} />}
                              onClick={() => handleDial(l.id, `${l.firstName} ${l.lastName}`)}
                            >
                              Dial
                            </Button>
                            <Link to={`/leads/${l.id}`}>
                              <Button size="sm" variant="ghost" rightIcon={<Icon name="arrowRight" size={14} />}>
                                Open
                              </Button>
                            </Link>
                          </div>
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
                <BulkActionBar count={sel.selectedCount} itemNoun="lead" onClear={sel.clear}
                  actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]} />
                </div>
              </CardBody>
            </Card>
          )}

          {(tab === "all" || tab === "users") && userCount > 0 && (
            <Card>
              <CardHeader
                title={<span className="inline-flex items-center gap-2"><Icon name="users" size={16} className="text-brand-600" /> Users <span className="text-ink-400 font-normal tabular-nums">· {userCount}</span></span>}
                subtitle="Members matching your query."
              />
              <CardBody className="p-0">
                <div className="overflow-x-auto">
                <Table className="border-0 shadow-none rounded-none">
                  <THead>
                    <TR>
                      <TH sortDir={userDir("userName")} onClick={() => sortUser("userName")}>User</TH>
                      <TH sortDir={userDir("email")} onClick={() => sortUser("email")}>Email</TH>
                      <TH sortDir={userDir("roles")} onClick={() => sortUser("roles")}>Roles</TH>
                      <TH sortDir={userDir("modules")} onClick={() => sortUser("modules")}><span className="inline-flex items-center gap-1">Modules<InfoHint title="Modules" side="top">How many app areas this user can open (Leads, Reports, Documents, etc.). More modules means broader access.</InfoHint></span></TH>
                    </TR>
                  </THead>
                  <TBody>
                    {sortedUsers.map((u) => (
                      <TR key={u.id}>
                        <TD>
                          <div className="flex items-center gap-2.5">
                            <Avatar name={u.userName} size={28} />
                            <div className="min-w-0">
                              <div className="text-sm font-medium text-ink-900 truncate">{u.userName}</div>
                              <div className="text-[11px] text-ink-500 font-mono tabular-nums">{u.id.slice(0, 8)}</div>
                            </div>
                          </div>
                        </TD>
                        <TD className="text-sm text-ink-700 truncate max-w-[260px]">{u.email}</TD>
                        <TD>
                          <div className="flex flex-wrap gap-1">
                            {u.roles.map((r) => (
                              <Badge key={r} tone="brand" variant="soft">{roleLabel(r)}</Badge>
                            ))}
                          </div>
                        </TD>
                        <TD className="text-sm text-ink-500 tabular-nums">{u.modules?.length ?? 0}</TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
                </div>
              </CardBody>
            </Card>
          )}
        </div>
      )}
    </>
  );
}
