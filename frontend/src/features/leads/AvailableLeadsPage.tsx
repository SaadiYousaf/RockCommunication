import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAvailableLeadsQuery, useClaimLeadMutation } from "../../shared/api/baseApi";
import type { AvailableLeadItem } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, EmptyState, ErrorState, Icon, PageHeader,
  Pager, SearchInput, Skeleton, Table, Tabs, TBody, TD, TH, THead, TR, useToast, usePagination,
} from "../../shared/ui";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { formatPhone } from "../../shared/lib/format";
import { getErrorStatus } from "../../shared/api/apiError";
import { LEADS_MSG } from "./messages";

/** "3h ago" / "2d ago" — how long this lead has been sitting unclaimed. */
function waitedFor(iso: string): string {
  const mins = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
  if (mins < 60) return `${mins}m`;
  const hours = Math.round(mins / 60);
  if (hours < 48) return `${hours}h`;
  return `${Math.round(hours / 24)}d`;
}

/**
 * Available Leads — the shared pool.
 *
 * Replaces the per-role "Verifier Queue" and "Closer Queue", which forced people to know which
 * internal queue their job mapped to. One screen shows whatever this user's roles can pick up; when
 * they work more than one pool it splits into tabs.
 *
 * Exactly ONE action per row: Claim. There is deliberately no "Open" here — opening without taking
 * ownership is how two closers ended up filling in the same application.
 */
export function AvailableLeadsPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const { data, isLoading, isError, error, refetch } = useAvailableLeadsQuery(undefined, {
    pollingInterval: 30_000,
  });
  const [claim, { isLoading: claiming }] = useClaimLeadMutation();
  const [claimingId, setClaimingId] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const rows = useMemo(() => data ?? [], [data]);

  // Tabs only when this user actually works more than one pool — a closer sees a plain list.
  const stages = useMemo(
    () => Array.from(new Map(rows.map((r) => [r.stageLabel, r.stage])).entries()),
    [rows],
  );
  const [tab, setTab] = useState<string>("");
  const activeTab = tab || stages[0]?.[0] || "";

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return rows.filter((r) => {
      if (stages.length > 1 && r.stageLabel !== activeTab) return false;
      if (!q) return true;
      const l = r.lead;
      return [l.firstName, l.lastName, l.phoneNumber, l.city, l.state]
        .some((v) => (v ?? "").toLowerCase().includes(q));
    });
  }, [rows, search, stages.length, activeTab]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    key: "createdAt",
    accessors: {
      name: (r: AvailableLeadItem) => `${r.lead.firstName} ${r.lead.lastName}`,
      createdAt: (r: AvailableLeadItem) => r.lead.createdAt,
      score: (r: AvailableLeadItem) => r.lead.score,
    },
  });
  const pg = usePagination(sorted);

  async function claimLead(item: AvailableLeadItem) {
    const name = `${item.lead.firstName} ${item.lead.lastName}`;
    setClaimingId(item.lead.id);
    try {
      await claim(item.lead.id).unwrap();
      toast.success(LEADS_MSG.claimedTitle, LEADS_MSG.claimedBody(name));
      // Straight into the work — claiming is only ever a prelude to working the lead.
      navigate(`/leads/${item.lead.id}`);
    } catch (err: unknown) {
      // 409 means someone else got there first. The list has already refreshed, so stay put.
      if (getErrorStatus(err) === 409) toast.warning(LEADS_MSG.claimLostTitle, LEADS_MSG.claimLostBody(name));
      else toast.error(LEADS_MSG.claimFailedTitle, LEADS_MSG.claimFailedBody);
    } finally {
      setClaimingId(null);
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Workspace"
        title={LEADS_MSG.availableTitle}
        description={LEADS_MSG.availableDescription}
        breadcrumbs={[{ label: "Workspace" }, { label: LEADS_MSG.availableTitle }]}
      />

      {stages.length > 1 && (
        <div className="mb-3">
          <Tabs
            value={activeTab}
            onChange={setTab}
            items={stages.map(([label]) => ({
              value: label,
              label,
              count: rows.filter((r) => r.stageLabel === label).length,
            }))}
          />
        </div>
      )}

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[260px]">
            <SearchInput value={search} onChange={setSearch} placeholder={LEADS_MSG.searchPlaceholder} />
          </div>
          <Badge tone="neutral" variant="soft" className="tabular-nums whitespace-nowrap">
            {LEADS_MSG.waitingCount(filtered.length)}
          </Badge>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-12 my-2" />)}</CardBody></Card>
      ) : isError ? (
        <Card><CardBody>
          <ErrorState error={error} resource={LEADS_MSG.availableResource} onRetry={refetch} />
        </CardBody></Card>
      ) : rows.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="check" size={20} />}
            title={LEADS_MSG.availableEmptyTitle}
            description={LEADS_MSG.availableEmptyBody}
          />
        </CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="search" size={20} />}
            title={LEADS_MSG.noSearchMatchTitle}
            description={LEADS_MSG.noSearchMatchBody}
          />
        </CardBody></Card>
      ) : (
        <>
          <Table>
            <THead>
              <TR>
                <TH sortDir={dirFor("name")} onClick={() => toggle("name")}>{LEADS_MSG.colLead}</TH>
                <TH sortDir={dirFor("createdAt")} onClick={() => toggle("createdAt")}>{LEADS_MSG.colWaiting}</TH>
                <TH numeric sortDir={dirFor("score")} onClick={() => toggle("score")}>{LEADS_MSG.colPriority}</TH>
                <TH>{LEADS_MSG.colLocation}</TH>
                <TH className="sticky right-0 bg-ink-50 border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
                  {LEADS_MSG.colAction}
                </TH>
              </TR>
            </THead>
            <TBody>
              {pg.pageItems.map((r) => (
                <TR key={r.lead.id}>
                  <TD>
                    <div className="font-medium text-ink-900 truncate">
                      {r.lead.firstName} {r.lead.lastName}
                    </div>
                    <div className="text-xs text-ink-500 tabular-nums whitespace-nowrap">
                      {formatPhone(r.lead.phoneNumber)}
                    </div>
                  </TD>
                  <TD className="text-ink-600 tabular-nums whitespace-nowrap text-sm">
                    {waitedFor(r.lead.createdAt)}
                  </TD>
                  <TD numeric className="tabular-nums text-ink-700">{Math.round(r.lead.score)}</TD>
                  <TD className="text-ink-600 text-sm truncate">
                    {[r.lead.city, r.lead.state].filter(Boolean).join(", ") || "—"}
                  </TD>
                  <TD className="sticky right-0 bg-white border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
                    <Button
                      size="sm"
                      loading={claiming && claimingId === r.lead.id}
                      disabled={claiming}
                      onClick={() => claimLead(r)}
                    >
                      {LEADS_MSG.claimAction}
                    </Button>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
          <Pager {...pg} onPage={pg.setPage} unit="leads" />
        </>
      )}
    </>
  );
}
