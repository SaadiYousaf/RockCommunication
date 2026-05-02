import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useListCallsQuery, useListUsersQuery, type CallsQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, Input, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR,
} from "../../shared/ui";

const statusTone: Record<string, "brand" | "info" | "success" | "warning" | "danger" | "neutral"> = {
  answered: "success", completed: "success",
  voicemail: "info", abandoned: "warning",
  failed: "danger", busy: "danger", noanswer: "warning",
};

export function CallsHistoryPage() {
  const [filters, setFilters] = useState<CallsQuery>({ skip: 0, take: 50, sort: "initiatedAt-desc" });
  const { data, isLoading, isFetching } = useListCallsQuery(filters);
  const { data: users } = useListUsersQuery();

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
        <Stat label="Avg talk"    value={`${formatSec(data?.avgTalkSeconds ?? 0)}`} icon={<Icon name="clock" size={16} />} tone="warning" />
      </div>

      <Card className="mb-4">
        <CardBody className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-3">
          <Select
            value={filters.agentUserId ?? ""}
            onChange={(e) => update("agentUserId", e.target.value || undefined)}
          >
            <option value="">All agents</option>
            {users?.map((u) => <option key={u.id} value={u.id}>{u.userName}</option>)}
          </Select>
          <Select
            value={filters.direction ?? ""}
            onChange={(e) => update("direction", e.target.value || undefined)}
          >
            <option value="">Any direction</option>
            <option value="Inbound">Inbound</option>
            <option value="Outbound">Outbound</option>
          </Select>
          <Select
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
            value={filters.from?.slice(0, 10) ?? ""}
            onChange={(e) => update("from", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
          />
          <Input type="date" leftIcon={<Icon name="calendar" size={14} />}
            value={filters.to?.slice(0, 10) ?? ""}
            onChange={(e) => update("to", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
          />
          <Select
            value={filters.sort ?? "initiatedAt-desc"}
            onChange={(e) => update("sort", e.target.value)}
          >
            <option value="initiatedAt-desc">Newest first</option>
            <option value="initiatedAt-asc">Oldest first</option>
            <option value="talkTime-desc">Longest talk time</option>
          </Select>
          <div className="md:col-span-3 lg:col-span-6 flex items-center justify-between text-xs text-ink-500 pt-1">
            <div>{pageInfo} {isFetching && <span className="ml-2 text-ink-400">refreshing…</span>}</div>
            <Button variant="ghost" size="sm" leftIcon={<Icon name="refresh" size={13} />}
              onClick={() => setFilters({ skip: 0, take: 50, sort: "initiatedAt-desc" })}>
              Reset filters
            </Button>
          </div>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0,1,2,3,4].map((i) => <Skeleton key={i} className="h-12 my-2" />)}</CardBody></Card>
      ) : !data || data.items.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="phone" size={20} />}
            title="No calls match"
            description="Try removing a filter or expanding the date range."
          />
        </CardBody></Card>
      ) : (
        <>
          <Table>
            <THead>
              <TR>
                <TH>When</TH>
                <TH>Lead</TH>
                <TH>Agent</TH>
                <TH>Direction</TH>
                <TH>Status</TH>
                <TH>Talk</TH>
                <TH>Wrap-up</TH>
                <TH></TH>
              </TR>
            </THead>
            <TBody>
              {data.items.map((c) => (
                <TR key={c.id}>
                  <TD className="text-ink-600 whitespace-nowrap text-xs">
                    {new Date(c.initiatedAt).toLocaleString()}
                  </TD>
                  <TD>
                    <Link to={`/leads/${c.leadId}`} className="block hover:underline">
                      <div className="font-medium text-ink-900">{c.leadName}</div>
                      <div className="text-xs text-ink-500">{c.leadPhone}</div>
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
                  <TD className="text-ink-600">{c.talkSeconds != null ? formatSec(c.talkSeconds) : "—"}</TD>
                  <TD className="text-ink-500 text-xs">{c.wrapUpCode ?? "—"}</TD>
                  <TD>
                    {c.recordingUrl && (
                      <a href={c.recordingUrl} target="_blank" rel="noreferrer"
                        className="text-brand-600 hover:underline text-xs inline-flex items-center gap-1">
                        ▶ Play
                      </a>
                    )}
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>

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

function formatSec(s: number): string {
  if (!s || s < 0) return "0s";
  const m = Math.floor(s / 60);
  const r = Math.round(s % 60);
  return m > 0 ? `${m}m ${r}s` : `${r}s`;
}
