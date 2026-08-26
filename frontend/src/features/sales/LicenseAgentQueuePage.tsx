import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useListSalesQuery } from "../../shared/api/baseApi";
import type { SaleListItem } from "../../shared/api/baseApi";
import type { BadgeTone } from "../../shared/ui";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, PageHeader,
  SearchInput, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { timeAgoShort, waitTone } from "../../shared/lib/time";
import { formatUsd } from "../../shared/lib/format";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { SALES_MSG } from "./messages";

function statusOf(s: SaleListItem): { label: string; tone: BadgeTone } {
  if (s.fundedAt) return { label: "Funded", tone: "success" };
  if (s.validatedAt) return { label: "Approved", tone: "warning" };
  return { label: "Pending", tone: "neutral" };
}

/**
 * Dedicated License-Agent work queue — the sales assigned to the signed-in agent as the agent
 * of record, with the status and the commission they earn. (The backend scopes ListSales to
 * LicenseAgentUserId for a License Agent, so this simply presents their own sales as a queue.)
 */
export function LicenseAgentQueuePage() {
  const { data, isLoading } = useListSalesQuery({ take: 200, sort: "soldAt-desc" });
  const items = data?.items ?? [];
  const totalCommission = items.reduce((sum, s) => sum + (s.commissionEarned ?? 0), 0);
  const fundedCount = items.filter((s) => s.fundedAt).length;
  const inReviewCount = items.length - fundedCount;

  // Free-text search over the rows already loaded (the stats above stay on the full queue).
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((s) =>
      [s.leadName, s.leadPhone, s.carrier, s.policyNumber, s.closerName]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [items, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: { status: (s) => statusOf(s).label },
  });
  const toast = useToast();
  const sel = useRowSelection(sorted.map((s) => s.id));

  function exportSelected() {
    const chosen = sorted.filter((s) => sel.isSelected(s.id));
    exportRowsToCsv(chosen, [
      { header: "Customer", value: (s) => s.leadName },
      { header: "Carrier", value: (s) => s.carrier },
      { header: "Premium", value: (s) => s.monthlyPremium },
      { header: "Status", value: (s) => statusOf(s).label },
    ], `my-sales-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(SALES_MSG.exportReadyTitle, SALES_MSG.rowsDownloaded(chosen.length));
  }

  return (
    <>
      <PageHeader
        eyebrow="License agent"
        title="My Sales"
        description="Sales assigned to you as the licensed agent of record — review each and track the commission you've earned."
      />

      {isLoading ? (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
          {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-20" />)}
        </div>
      ) : (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
          <Stat label="Assigned"          value={items.length}                 icon={<Icon name="briefcase" size={16} />} tone="brand"
                hint="Sales where you're the licensed agent of record." />
          <Stat label="Funded"            value={fundedCount}                  icon={<Icon name="success" size={16} />}   tone="success"
                hint="First premium draft cleared — commission payable." />
          <Stat label="In review"         value={inReviewCount}                icon={<Icon name="clock" size={16} />}     tone="warning"
                hint="Pending or approved, not yet funded." />
          <Stat label="Commission earned" value={formatUsd(totalCommission)}   icon={<Icon name="card" size={16} />}      tone="accent"
                hint="Total approval commission across your assigned sales." />
        </div>
      )}

      <Card>
        <CardHeader
          title="Assigned to me"
          action={
            <SearchInput
              value={search} onChange={setSearch}
              placeholder={SALES_MSG.assignedSalesSearchPlaceholder}
              className="w-64"
            />
          }
        />
        <CardBody>
          {isLoading ? (
            <div className="space-y-2">{[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-12" />)}</div>
          ) : items.length === 0 ? (
            <EmptyState
              icon={<Icon name="briefcase" size={20} />}
              title={SALES_MSG.noSalesAssignedTitle}
              description={SALES_MSG.noSalesAssignedBody}
            />
          ) : sorted.length === 0 ? (
            <EmptyState
              icon={<Icon name="search" size={20} />}
              title={SALES_MSG.noAssignedSalesMatchTitle}
              description={SALES_MSG.noAssignedSalesMatchBody}
            />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH className="w-10"><Checkbox aria-label="Select all sales" {...sel.allCheckboxProps} /></TH><TH sortDir={dirFor("saleNumber")} onClick={() => toggle("saleNumber")}>#</TH><TH sortDir={dirFor("leadName")} onClick={() => toggle("leadName")}>Customer</TH><TH sortDir={dirFor("carrier")} onClick={() => toggle("carrier")}>Carrier</TH><TH sortDir={dirFor("monthlyPremium")} onClick={() => toggle("monthlyPremium")}>Premium</TH>
                  <TH sortDir={dirFor("status")} onClick={() => toggle("status")}>
                    <span className="inline-flex items-center gap-1">Status
                      <InfoHint title="Sale status" side="bottom">Pending (awaiting approval), Approved (validated by the carrier), or Funded (first draft cleared — commission payable).</InfoHint>
                    </span>
                  </TH>
                  <TH sortDir={dirFor("commissionEarned")} onClick={() => toggle("commissionEarned")}>
                    <span className="inline-flex items-center gap-1">Commission
                      <InfoHint title="Your commission" side="bottom">The approval commission you earn on this sale.</InfoHint>
                    </span>
                  </TH>
                  <TH sortDir={dirFor("soldAt")} onClick={() => toggle("soldAt")}>
                    <span className="inline-flex items-center gap-1">Sold
                      <InfoHint title="When it was sold" side="bottom">How long ago the sale was recorded.</InfoHint>
                    </span>
                  </TH>
                  <TH className="sticky right-0 bg-ink-50 border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">Actions</TH>
                </TR>
              </THead>
              <TBody>
                {sorted.map((s) => {
                  const st = statusOf(s);
                  return (
                    <TR key={s.id} className={sel.isSelected(s.id) ? "bg-brand-50/40" : undefined}>
                      <TD>
                        <Checkbox aria-label={`Select ${s.leadName}`} {...sel.checkboxProps(s.id)} />
                      </TD>
                      <TD className="tabular-nums text-ink-500">#{s.saleNumber}</TD>
                      <TD>
                        <div className="font-medium text-ink-900 whitespace-nowrap">{s.leadName}</div>
                        <div className="font-mono text-xs text-ink-500 tabular-nums whitespace-nowrap">{s.leadPhone}</div>
                      </TD>
                      <TD className="text-sm whitespace-nowrap">{s.carrier}</TD>
                      <TD className="text-sm tabular-nums whitespace-nowrap">{formatUsd(s.monthlyPremium)}/mo</TD>
                      <TD><Badge tone={st.tone} variant="soft">{st.label}</Badge></TD>
                      <TD className="text-sm tabular-nums font-medium text-ink-800">{formatUsd(s.commissionEarned ?? 0)}</TD>
                      <TD className="whitespace-nowrap">
                        <span title={new Date(s.soldAt).toLocaleString()}>
                          <Badge tone={waitTone(s.soldAt)} variant="soft">{timeAgoShort(s.soldAt)}</Badge>
                        </span>
                      </TD>
                      <TD className="text-right whitespace-nowrap sticky right-0 bg-white border-l hairline shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
                        <Link to={`/sales/${s.id}`}>
                          <Button size="sm" leftIcon={<Icon name="eye" size={14} />}>Open</Button>
                        </Link>
                      </TD>
                    </TR>
                  );
                })}
              </TBody>
            </Table>
          )}
          <BulkActionBar
            count={sel.selectedCount} itemNoun="sale" onClear={sel.clear}
            actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
          />
        </CardBody>
      </Card>
    </>
  );
}
