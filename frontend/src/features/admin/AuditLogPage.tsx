import { Fragment, useMemo, useState } from "react";
import { useAuditFiltersQuery, useListAuditQuery, type AuditEntry, type AuditQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, Input, PageHeader,
  Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tooltip, useToast,
} from "../../shared/ui";

type Tone = "brand" | "info" | "success" | "warning" | "danger" | "neutral";

const actionTone: Record<string, Tone> = {
  Created: "success", Create: "success",
  Updated: "info", Update: "info",
  Deleted: "danger", Delete: "danger",
  Login: "brand", Logout: "neutral",
  Transition: "warning", Transitioned: "warning",
};

const actionIcon: Record<string, "plus" | "check" | "x" | "arrowRight"> = {
  Created: "plus", Create: "plus",
  Updated: "check", Update: "check",
  Deleted: "x", Delete: "x",
  Transition: "arrowRight", Transitioned: "arrowRight",
};

function formatIp(ip: string | null): { display: string; full: string } | null {
  if (!ip) return null;
  if (ip === "::1") return { display: "localhost", full: "::1 (IPv6 loopback)" };
  if (ip === "127.0.0.1") return { display: "localhost", full: "127.0.0.1" };
  if (ip.startsWith("::ffff:")) {
    const v4 = ip.slice(7);
    return { display: v4, full: ip };
  }
  return { display: ip, full: ip };
}

function relativeTime(iso: string): string {
  const d = new Date(iso).getTime();
  const diff = Date.now() - d;
  const s = Math.round(diff / 1000);
  if (s < 5) return "just now";
  if (s < 60) return `${s}s ago`;
  const m = Math.round(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h}h ago`;
  const days = Math.round(h / 24);
  if (days < 7) return `${days}d ago`;
  return new Date(iso).toLocaleDateString();
}

function parseChanges(raw: string | null): Array<{ field: string; old: unknown; next: unknown }> | null {
  if (!raw) return null;
  try {
    const obj = JSON.parse(raw) as Record<string, { Old?: unknown; New?: unknown; old?: unknown; new?: unknown }>;
    return Object.entries(obj).map(([field, v]) => ({
      field,
      old: v?.Old ?? v?.old,
      next: v?.New ?? v?.new,
    }));
  } catch {
    return null;
  }
}

function fmtVal(v: unknown): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "string") return v.length > 120 ? v.slice(0, 120) + "…" : v;
  if (typeof v === "object") return JSON.stringify(v);
  return String(v);
}

export function AuditLogPage() {
  const [filters, setFilters] = useState<AuditQuery>({ skip: 0, take: 50 });
  const { data: filterOpts } = useAuditFiltersQuery();
  const { data, isLoading, isFetching } = useListAuditQuery(filters);
  const [expanded, setExpanded] = useState<string | null>(null);
  const toast = useToast();

  const total = data?.total ?? 0;
  const skip = filters.skip ?? 0;
  const take = filters.take ?? 50;

  const pageInfo = useMemo(() => {
    const start = total === 0 ? 0 : skip + 1;
    const end = Math.min(skip + take, total);
    return `${start}–${end} of ${total}`;
  }, [skip, take, total]);

  function update<K extends keyof AuditQuery>(key: K, value: AuditQuery[K]) {
    setFilters((f) => ({ ...f, [key]: value, skip: 0 }));
  }

  function reset() {
    setFilters({ skip: 0, take: 50 });
  }

  function exportCsv() {
    if (!data?.items.length) return;
    const header = ["Timestamp", "Entity", "Entity ID", "Action", "User", "IP", "Changes"];
    const rows = data.items.map((e: AuditEntry) => [
      new Date(e.occurredAt).toISOString(),
      e.entityName,
      e.entityId,
      e.action,
      e.userName ?? "",
      e.ipAddress ?? "",
      (e.changes ?? "").replace(/\s+/g, " "),
    ]);
    const csv = [header, ...rows]
      .map((r) => r.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(","))
      .join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success(`Exported ${rows.length} rows`);
  }

  return (
    <>
      <PageHeader
        title="Audit Log"
        description="Every change to entities in this CRM. Filter by entity, action, user, or date range."
        actions={
          <Button variant="outline" size="sm" onClick={exportCsv} disabled={!data?.items.length}>
            <Icon name="doc" size={14} className="mr-1.5" /> Export CSV
          </Button>
        }
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
        <Stat label="Total entries" value={total} icon={<Icon name="doc" size={16} />} />
        <Stat label="Entity types" value={filterOpts?.entityNames.length ?? 0} />
        <Stat label="Actions" value={filterOpts?.actions.length ?? 0} />
        <Stat label="Active users" value={filterOpts?.users.length ?? 0} />
      </div>

      <Card className="mb-4">
        <CardBody className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-3">
          <div className="lg:col-span-2">
            <Input
              leftIcon={<Icon name="search" size={16} />}
              placeholder="Search entity, user, changes, IP..."
              value={filters.search ?? ""}
              onChange={(e) => update("search", e.target.value || undefined)}
            />
          </div>
          <select
            className="border border-ink-200 rounded px-3 py-2 text-sm bg-white"
            value={filters.entityName ?? ""}
            onChange={(e) => update("entityName", e.target.value || undefined)}
          >
            <option value="">All entities</option>
            {filterOpts?.entityNames.map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
          <select
            className="border border-ink-200 rounded px-3 py-2 text-sm bg-white"
            value={filters.action ?? ""}
            onChange={(e) => update("action", e.target.value || undefined)}
          >
            <option value="">All actions</option>
            {filterOpts?.actions.map((a) => <option key={a} value={a}>{a}</option>)}
          </select>
          <Input
            type="date"
            value={filters.after?.slice(0, 10) ?? ""}
            onChange={(e) => update("after", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
            placeholder="From"
          />
          <Input
            type="date"
            value={filters.before?.slice(0, 10) ?? ""}
            onChange={(e) => update("before", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
            placeholder="To"
          />
          <div className="md:col-span-3 lg:col-span-6 flex items-center justify-between text-xs text-ink-500">
            <div>{pageInfo} {isFetching && <span className="ml-2 text-ink-400">refreshing…</span>}</div>
            <Button variant="ghost" size="sm" onClick={reset}>Reset filters</Button>
          </div>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>
          {[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-10 my-2" />)}
        </CardBody></Card>
      ) : !data || data.items.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="doc" size={20} />}
            title="No audit entries"
            description="Try a different filter or date range."
          />
        </CardBody></Card>
      ) : (
        <>
          <Card>
            <Table>
              <THead>
                <TR>
                  <TH>When</TH>
                  <TH>Entity</TH>
                  <TH>Action</TH>
                  <TH>User</TH>
                  <TH>IP</TH>
                  <TH></TH>
                </TR>
              </THead>
              <TBody>
                {data.items.map((e) => {
                  const tone = actionTone[e.action] ?? "neutral";
                  const icon = actionIcon[e.action];
                  const ip = formatIp(e.ipAddress);
                  const isOpen = expanded === e.id;
                  const changes = isOpen ? parseChanges(e.changes) : null;
                  const hasChanges = !!e.changes;
                  return (
                    <Fragment key={e.id}>
                      <TR
                        className={hasChanges ? "cursor-pointer hover:bg-ink-50/60" : ""}
                        onClick={hasChanges ? () => setExpanded(isOpen ? null : e.id) : undefined}
                      >
                        <TD className="whitespace-nowrap">
                          <Tooltip content={new Date(e.occurredAt).toLocaleString()}>
                            <div>
                              <div className="text-ink-900 text-sm">{relativeTime(e.occurredAt)}</div>
                              <div className="text-xs text-ink-500">
                                {new Date(e.occurredAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
                              </div>
                            </div>
                          </Tooltip>
                        </TD>
                        <TD>
                          <div className="font-medium text-ink-900">{e.entityName}</div>
                          <Tooltip content={e.entityId}>
                            <div className="text-xs text-ink-500 font-mono">{e.entityId.slice(0, 8)}…</div>
                          </Tooltip>
                        </TD>
                        <TD>
                          <Badge tone={tone} variant="soft">
                            {icon && <Icon name={icon} size={12} className="mr-1" />}
                            {e.action}
                          </Badge>
                        </TD>
                        <TD>
                          {e.userName ? (
                            <div className="flex items-center gap-2">
                              <Avatar name={e.userName} size={24} />
                              <span className="text-ink-700 text-sm">{e.userName}</span>
                            </div>
                          ) : (
                            <span className="text-ink-400">—</span>
                          )}
                        </TD>
                        <TD className="font-mono text-xs">
                          {ip ? (
                            <Tooltip content={ip.full}>
                              <span className="text-ink-600">{ip.display}</span>
                            </Tooltip>
                          ) : (
                            <span className="text-ink-400">—</span>
                          )}
                        </TD>
                        <TD className="text-right">
                          {hasChanges ? (
                            <span className="text-xs text-ink-500">{isOpen ? "Hide" : "Diff"}</span>
                          ) : null}
                        </TD>
                      </TR>
                      {isOpen && hasChanges && (
                        <TR>
                          <TD colSpan={6} className="bg-ink-50 p-0">
                            {changes ? (
                              <div className="p-4">
                                <table className="w-full text-xs">
                                  <thead>
                                    <tr className="text-ink-500">
                                      <th className="text-left font-medium pb-2 pr-4 w-1/4">Field</th>
                                      <th className="text-left font-medium pb-2 pr-4 w-3/8">Old</th>
                                      <th className="text-left font-medium pb-2 w-3/8">New</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {changes.map((c) => (
                                      <tr key={c.field} className="border-t border-ink-200/70 align-top">
                                        <td className="py-2 pr-4 font-mono text-ink-700">{c.field}</td>
                                        <td className="py-2 pr-4 text-rose-700 break-all">{fmtVal(c.old)}</td>
                                        <td className="py-2 text-emerald-700 break-all">{fmtVal(c.next)}</td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            ) : (
                              <pre className="text-xs whitespace-pre-wrap break-all p-4 text-ink-700">{e.changes}</pre>
                            )}
                          </TD>
                        </TR>
                      )}
                    </Fragment>
                  );
                })}
              </TBody>
            </Table>
          </Card>

          <div className="flex items-center justify-between mt-4">
            <div className="text-xs text-ink-500">{pageInfo}</div>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" disabled={skip === 0}
                onClick={() => setFilters((f) => ({ ...f, skip: Math.max(0, (f.skip ?? 0) - take) }))}>
                Previous
              </Button>
              <Button variant="outline" size="sm" disabled={skip + take >= total}
                onClick={() => setFilters((f) => ({ ...f, skip: (f.skip ?? 0) + take }))}>
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </>
  );
}
