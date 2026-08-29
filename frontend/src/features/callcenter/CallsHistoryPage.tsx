import { useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useListCallsQuery, useListUsersQuery, type CallsQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, ErrorState, Icon, InfoHint, Input, PageHeader,
  SearchInput, Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { CALLCENTER_MSG } from "./messages";

const statusTone: Record<string, "brand" | "info" | "success" | "warning" | "danger" | "neutral"> = {
  answered: "success", completed: "success",
  voicemail: "info", abandoned: "warning",
  failed: "danger", busy: "danger", noanswer: "warning",
};

export function CallsHistoryPage() {
  // Seed from the URL so a wallboard tile ("Calls answered today") opens the same rows it counted.
  const [searchParams] = useSearchParams();
  const [filters, setFilters] = useState<CallsQuery>(() => ({
    skip: 0, take: 50, sort: "initiatedAt-desc",
    from: searchParams.get("from") ?? undefined,
    to: searchParams.get("to") ?? undefined,
    status: searchParams.get("status") ?? undefined,
    direction: searchParams.get("direction") ?? undefined,
  }));
  const { data, isLoading, isFetching, isError, error, refetch } = useListCallsQuery(filters);
  const { data: users } = useListUsersQuery();
  const toast = useToast();

  // Client-side search across the page of calls already fetched — never touches the query above.
  const [search, setSearch] = useState("");
  const rows = useMemo(() => {
    const all = data?.items ?? [];
    const q = search.trim().toLowerCase();
    if (!q) return all;
    return all.filter((c) =>
      [c.leadName, c.leadPhone, c.agentName, c.wrapUpCode, c.status, c.direction]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [data, search]);

  const sel = useRowSelection(rows.map((c) => c.id));

  function exportSelected() {
    const chosen = rows.filter((c) => sel.isSelected(c.id));
    exportRowsToCsv(chosen, [
      { header: "Time", value: (c) => new Date(c.initiatedAt).toLocaleString() },
      { header: "Direction", value: (c) => c.direction },
      { header: "From/To", value: (c) => c.leadPhone },
      { header: "Duration", value: (c) => (c.talkSeconds != null ? formatSec(c.talkSeconds) : "") },
      { header: "Disposition", value: (c) => c.wrapUpCode ?? "" },
      { header: "Agent", value: (c) => c.agentName ?? "" },
    ], `calls-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(CALLCENTER_MSG.exportReadyTitle, CALLCENTER_MSG.rowsDownloaded(chosen.length));
  }

  const total = data?.total ?? 0;
  const skip = filters.skip ?? 0;
  const take = filters.take ?? 50;

  const pageInfo = useMemo(() => {
    const start = total === 0 ? 0 : skip + 1;
    const end = Math.min(skip + take, total);
    return `${start}–${end} of ${total}`;
  }, [skip, take, total]);

  function update<K extends keyof CallsQuery>(key: K, value: CallsQuery[K]) {
    setFilters((f) => ({ ...f, [key]: value, skip: 0 }));
  }

  return (
    <>
      <PageHeader
        title="Call History"
        description="Every call placed and received. Filter, sort, and play back recordings."
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Total calls" value={total}                                icon={<Icon name="phoneCall" size={16} />} tone="brand" />
        <Stat label="Answered"    value={data?.answeredCount ?? 0}             icon={<Icon name="phoneIn" size={16} />}   tone="success" />
        <Stat label="Voicemail"   value={data?.voicemailCount ?? 0}            icon={<Icon name="mic" size={16} />}       tone="accent" />
        <Stat label="Avg talk"    value={`${formatSec(data?.avgTalkSeconds ?? 0)}`} icon={<Icon name="clock" size={16} />} tone="warning"
          hint="Average connected talk time across the calls in this view — excludes ring and voicemail time." />
      </div>

      <Card className="mb-4">
        <CardBody className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-3">
          <SearchInput
            value={search} onChange={setSearch}
            placeholder={CALLCENTER_MSG.callsSearchPlaceholder}
            containerClassName="md:col-span-3 lg:col-span-2"
          />
          <Select
            aria-label="Filter by agent"
            value={filters.agentUserId ?? ""}
            onChange={(e) => update("agentUserId", e.target.value || undefined)}
          >
            <option value="">All agents</option>
            {users?.map((u) => <option key={u.id} value={u.id}>{u.userName}</option>)}
          </Select>
          <Select
            aria-label="Filter by direction"
            value={filters.direction ?? ""}
            onChange={(e) => update("direction", e.target.value || undefined)}
          >
            <option value="">Any direction</option>
            <option value="Inbound">Inbound</option>
            <option value="Outbound">Outbound</option>
          </Select>
          <Select
            aria-label="Filter by call status"
            value={filters.status ?? ""}
            onChange={(e) => update("status", e.target.value || undefined)}
          >
            <option value="">Any status</option>
            <option value="answered">Answered</option>
            <option value="completed">Completed</option>
            <option value="voicemail">Voicemail</option>
            <option value="abandoned">Abandoned</option>
            <option value="failed">Failed</option>
          </Select>
          <Input type="date" leftIcon={<Icon name="calendar" size={14} />}
            aria-label="From date" title="Show calls on or after this date"
            value={filters.from?.slice(0, 10) ?? ""}
            onChange={(e) => update("from", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
          />
          <Input type="date" leftIcon={<Icon name="calendar" size={14} />}
            aria-label="To date" title="Show calls on or before this date"
            value={filters.to?.slice(0, 10) ?? ""}
            onChange={(e) => update("to", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
          />
          <Select
            aria-label="Sort order"
            value={filters.sort ?? "initiatedAt-desc"}
            onChange={(e) => update("sort", e.target.value)}
          >
            <option value="initiatedAt-desc">Newest first</option>
            <option value="initiatedAt-asc">Oldest first</option>
            <option value="talkTime-desc">Longest talk time</option>
          </Select>
          <div className="md:col-span-3 lg:col-span-6 flex flex-wrap items-center justify-between gap-2 text-xs text-ink-500 pt-1">
            <div className="tabular-nums">{pageInfo} {isFetching && <span className="ml-2 text-ink-400">refreshing…</span>}</div>
            <Button variant="ghost" size="sm" leftIcon={<Icon name="refresh" size={13} />}
              onClick={() => { setSearch(""); setFilters({ skip: 0, take: 50, sort: "initiatedAt-desc" }); }}>
              Reset filters
            </Button>
          </div>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0,1,2,3,4].map((i) => <Skeleton key={i} className="h-12 my-2" />)}</CardBody></Card>
      ) : isError ? (
        // A failed request must NEVER fall through to "no calls found" — that reads as data loss.
        <Card><CardBody>
          <ErrorState error={error} resource={CALLCENTER_MSG.callsResourceName} onRetry={refetch} />
        </CardBody></Card>
      ) : !data || data.items.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="phone" size={20} />}
            title={CALLCENTER_MSG.noCallsMatchTitle}
            description={CALLCENTER_MSG.noCallsMatchBody}
          />
        </CardBody></Card>
      ) : rows.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="search" size={20} />}
            title={CALLCENTER_MSG.noCallsSearchMatchTitle}
            description={CALLCENTER_MSG.tryDifferentSearch}
          />
        </CardBody></Card>
      ) : (
        <>
          <Table>
            <THead>
              <TR>
                <TH className="w-10"><Checkbox aria-label="Select all calls" {...sel.allCheckboxProps} /></TH>
                <TH>When</TH>
                <TH>Lead</TH>
                <TH>Agent</TH>
                <TH>Direction</TH>
                <TH>
                  <span className="inline-flex items-center gap-1">
                    Status
                    <InfoHint title="Call status" side="top">
                      Answered / Completed = connected to an agent. Voicemail = went to voicemail. Abandoned = caller hung up before an agent answered. Failed / Busy = the call couldn't connect.
                    </InfoHint>
                  </span>
                </TH>
                <TH>
                  <span className="inline-flex items-center gap-1">
                    Talk
                    <InfoHint title="Talk time" side="top">
                      The connected talk duration of the call, not counting ring or voicemail time.
                    </InfoHint>
                  </span>
                </TH>
                <TH>
                  <span className="inline-flex items-center gap-1">
                    Wrap-up
                    <InfoHint title="Wrap-up" side="top">
                      The disposition code recording the call's outcome, set by the agent after the call.
                    </InfoHint>
                  </span>
                </TH>
                <TH></TH>
              </TR>
            </THead>
            <TBody>
              {rows.map((c) => (
                <TR key={c.id} className={sel.isSelected(c.id) ? "bg-brand-50/40" : undefined}>
                  <TD>
                    <Checkbox aria-label={`Select call ${c.leadName}`} {...sel.checkboxProps(c.id)} />
                  </TD>
                  <TD className="text-ink-600 whitespace-nowrap text-xs tabular-nums">
                    {new Date(c.initiatedAt).toLocaleString()}
                  </TD>
                  <TD>
                    <Link to={`/leads/${c.leadId}`} className="block hover:underline">
                      <div className="font-medium text-ink-900 truncate">{c.leadName}</div>
                      <div className="text-xs text-ink-500 tabular-nums whitespace-nowrap">{c.leadPhone}</div>
                    </Link>
                  </TD>
                  <TD>
                    {c.agentName ? (
                      <div className="flex items-center gap-2">
                        <Avatar name={c.agentName} size={24} />
                        <span className="text-ink-700 text-sm">{c.agentName}</span>
                      </div>
                    ) : <span className="text-ink-400">—</span>}
                  </TD>
                  <TD>
                    <Badge tone={c.direction === "Inbound" ? "info" : "brand"} variant="soft">
                      {c.direction}
                    </Badge>
                  </TD>
                  <TD><Badge tone={statusTone[c.status] ?? "neutral"} variant="soft" dot>{c.status}</Badge></TD>
                  <TD className="text-ink-600 tabular-nums whitespace-nowrap">{c.talkSeconds != null ? formatSec(c.talkSeconds) : "—"}</TD>
                  <TD className="text-ink-500 text-xs">{c.wrapUpCode ?? "—"}</TD>
                  <TD>
                    {c.recordingUrl && (
                      <a href={c.recordingUrl} target="_blank" rel="noreferrer"
                        aria-label="Play call recording"
                        title="Play recording (opens in a new tab)"
                        className="text-brand-600 hover:underline text-xs inline-flex items-center gap-1">
                        <Icon name="play" size={12} /> Play
                      </a>
                    )}
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
          <BulkActionBar
            count={sel.selectedCount} itemNoun="call" onClear={sel.clear}
            actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
          />

          <div className="flex flex-wrap items-center justify-between gap-3 mt-4">
            <div className="text-xs text-ink-500 tabular-nums">{pageInfo}</div>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" disabled={skip === 0}
                title={skip === 0 ? "You're on the first page" : "Go to the previous page"}
                leftIcon={<Icon name="chevronLeft" size={14} />}
                onClick={() => setFilters((f) => ({ ...f, skip: Math.max(0, (f.skip ?? 0) - take) }))}>
                Previous
              </Button>
              <Button variant="outline" size="sm" disabled={skip + take >= total}
                title={skip + take >= total ? "You're on the last page" : "Go to the next page"}
                rightIcon={<Icon name="chevronRight" size={14} />}
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

function formatSec(s: number): string {
  if (!s || s < 0) return "0s";
  const m = Math.floor(s / 60);
  const r = Math.round(s % 60);
  return m > 0 ? `${m}m ${r}s` : `${r}s`;
}
