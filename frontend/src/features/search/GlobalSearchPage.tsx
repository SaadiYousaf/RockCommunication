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
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input,
  PageHeader, Skeleton, Stat, Tabs, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { STAGE_TONE as stageTone } from "../../shared/constants/leadStage";


function formatPhone(p?: string | null) {
  if (!p) return "";
  const d = p.replace(/\D/g, "");
  if (d.length === 10) return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
  return p;
}

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

  const { data: leads, isFetching: leadsLoading } = useSearchLeadsQuery(
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

  const leadCount = leads?.length ?? 0;
  const userCount = matchingUsers.length;
  const totalCount = leadCount + userCount;
  const loading = leadsLoading || usersLoading;

  const [dialLead] = useDialLeadMutation();
  async function handleDial(id: string, name: string) {
    try {
      await dialLead({ leadId: id }).unwrap();
      toast.success("Dialing", `Calling ${name}…`);
    } catch (err: unknown) {
      toast.error("Dial failed", getErrorDetail(err) ?? "");
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
          <Input
            placeholder="Search by name, phone, email, role…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            leftIcon={<Icon name="search" size={16} />}
            rightSlot={
              query ? (
                <button
                  type="button"
                  onClick={() => setQuery("")}
                  className="text-xs text-ink-500 hover:text-ink-800 px-2"
                >
                  Clear
                </button>
              ) : undefined
            }
            autoFocus
          />
          <div className="mt-3 flex items-center gap-2 text-xs text-ink-500">
            <Icon name="search" size={12} />
            <span>
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
          { value: "all",   label: <span className="inline-flex items-center gap-1.5">All <Badge tone="neutral" variant="soft">{hasQuery ? totalCount : 0}</Badge></span> },
          { value: "leads", label: <span className="inline-flex items-center gap-1.5"><Icon name="list" size={14} /> Leads <Badge tone="brand" variant="soft">{hasQuery ? leadCount : 0}</Badge></span> },
          { value: "users", label: <span className="inline-flex items-center gap-1.5"><Icon name="users" size={14} /> Users <Badge tone="info" variant="soft">{hasQuery ? userCount : 0}</Badge></span> },
        ]}
      />

      {!hasQuery ? (
        <Card>
          <CardBody>
            <EmptyState
              icon={<Icon name="search" size={20} />}
              title="Start typing"
              description="Search by lead phone, email, partial name, or username/role for users."
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
      ) : totalCount === 0 ? (
        <Card>
          <CardBody>
            <EmptyState
              icon={<Icon name="search" size={20} />}
              title="No matches"
              description={`Nothing matched "${debounced}". Try a different phone, email, or name fragment.`}
            />
          </CardBody>
        </Card>
      ) : (
        <div className="space-y-6">
          {(tab === "all" || tab === "leads") && leadCount > 0 && (
            <Card>
              <CardHeader title={`Leads · ${leadCount}`} subtitle="Click a row to open the lead, or dial directly." />
              <CardBody className="p-0">
                <Table>
                  <THead>
                    <TR>
                      <TH>Name</TH>
                      <TH>Phone</TH>
                      <TH>Email</TH>
                      <TH>Stage</TH>
                      <TH>State</TH>
                      <TH className="text-right">Actions</TH>
                    </TR>
                  </THead>
                  <TBody>
                    {(leads ?? []).map((l: Lead) => (
                      <TR
                        key={l.id}
                        onClick={() => navigate(`/leads/${l.id}`)}
                        className="cursor-pointer"
                      >
                        <TD>
                          <div className="flex items-center gap-2.5">
                            <Avatar name={`${l.firstName} ${l.lastName}`} size={28} />
                            <div className="min-w-0">
                              <div className="text-sm font-medium text-ink-900">
                                {l.firstName} {l.lastName}
                              </div>
                              <div className="text-[11px] text-ink-500 font-mono">{l.id.slice(0, 8)}</div>
                            </div>
                          </div>
                        </TD>
                        <TD className="font-mono text-sm">{formatPhone(l.phoneNumber)}</TD>
                        <TD className="text-sm text-ink-700 truncate max-w-[220px]">{l.email ?? "—"}</TD>
                        <TD>
                          <Badge tone={stageTone[l.stage] ?? "neutral"} variant="soft">{l.stage}</Badge>
                        </TD>
                        <TD className="text-sm text-ink-700">{l.state ?? "—"}</TD>
                        <TD onClick={(e) => e.stopPropagation()} className="text-right">
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
              </CardBody>
            </Card>
          )}

          {(tab === "all" || tab === "users") && userCount > 0 && (
            <Card>
              <CardHeader title={`Users · ${userCount}`} subtitle="Members matching your query." />
              <CardBody className="p-0">
                <Table>
                  <THead>
                    <TR>
                      <TH>User</TH>
                      <TH>Email</TH>
                      <TH>Roles</TH>
                      <TH>Modules</TH>
                    </TR>
                  </THead>
                  <TBody>
                    {matchingUsers.map((u) => (
                      <TR key={u.id}>
                        <TD>
                          <div className="flex items-center gap-2.5">
                            <Avatar name={u.userName} size={28} />
                            <div className="min-w-0">
                              <div className="text-sm font-medium text-ink-900">{u.userName}</div>
                              <div className="text-[11px] text-ink-500 font-mono">{u.id.slice(0, 8)}</div>
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
                        <TD className="text-sm text-ink-500">{u.modules?.length ?? 0}</TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              </CardBody>
            </Card>
          )}
        </div>
      )}
    </>
  );
}
