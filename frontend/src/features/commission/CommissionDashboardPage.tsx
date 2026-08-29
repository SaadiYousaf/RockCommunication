import { useState } from "react";
import { useCommissionDeskDashboardQuery } from "../../shared/api/baseApi";
import type { CommissionDeskBreakdownRow } from "../../shared/api/types";
import { formatUsd } from "../../shared/lib/format";
import {
  Card, CardBody, CardHeader, EmptyState, ErrorState, Icon, Input, PageHeader, Skeleton, Stat,
  Table, TBody, TD, TH, THead, TR, Pager, usePagination,
} from "../../shared/ui";
import { COMMISSION_DASH_MSG } from "./messages";

/**
 * Commission-desk dashboard: for a chosen month, what the carrier advancing rules say should be
 * advanced versus what's actually on the commission ledger, broken down by agency and call centre.
 */
export function CommissionDashboardPage() {
  const now = new Date();
  const [month, setMonth] = useState(`${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`);
  const [year, mon] = month.split("-").map(Number);

  const { data, isLoading, isError, error, refetch } = useCommissionDeskDashboardQuery({ year, month: mon });

  return (
    <>
      <PageHeader
        eyebrow={COMMISSION_DASH_MSG.eyebrow}
        title={COMMISSION_DASH_MSG.title}
        description={COMMISSION_DASH_MSG.description}
        actions={
          <Input aria-label={COMMISSION_DASH_MSG.month} type="month" value={month}
            onChange={(e) => setMonth(e.target.value)} className="w-44" />
        }
      />

      <div className="grid grid-cols-2 lg:grid-cols-5 gap-3 mb-5">
        <Stat label={COMMISSION_DASH_MSG.totalSales} value={data?.totalSales ?? 0}
          icon={<Icon name="briefcase" size={18} />} tone="brand" />
        <Stat label={COMMISSION_DASH_MSG.totalPremium} value={formatUsd(data?.totalPremium)}
          icon={<Icon name="dollar" size={18} />} tone="accent" />
        <Stat label={COMMISSION_DASH_MSG.expectedAdvance} value={formatUsd(data?.totalExpectedAdvance)}
          icon={<Icon name="chart" size={18} />} hint={COMMISSION_DASH_MSG.expectedAdvanceHint} tone="success" />
        <Stat label={COMMISSION_DASH_MSG.funded} value={formatUsd(data?.totalFunded)}
          icon={<Icon name="check" size={18} />} hint={COMMISSION_DASH_MSG.fundedHint}
          tone={(data?.totalFunded ?? 0) < 0 ? "danger" : "success"} />
        <Stat label={COMMISSION_DASH_MSG.chargedBack} value={data?.chargedBackCount ?? 0}
          icon={<Icon name="alert" size={18} />} hint={formatUsd(data?.chargedBackAmount)} tone="danger" />
      </div>

      {isLoading ? (
        <Skeleton className="h-64" />
      ) : isError ? (
        // A failed request must never read as "nothing was sold this month" — the stats above
        // already show zeros, so an empty state here would confirm a month of missing sales.
        <Card><CardBody>
          <ErrorState error={error} resource={COMMISSION_DASH_MSG.resourceName} onRetry={refetch} />
        </CardBody></Card>
      ) : !data || data.totalSales === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="chart" size={20} />} title={COMMISSION_DASH_MSG.emptyTitle}
            description={COMMISSION_DASH_MSG.emptyDesc} />
        </CardBody></Card>
      ) : (
        <div className="grid grid-cols-1 xl:grid-cols-2 gap-5 items-start">
          <BreakdownCard title={COMMISSION_DASH_MSG.byAgency} rows={data.byAgency} />
          <BreakdownCard title={COMMISSION_DASH_MSG.byCallCenter} rows={data.byCallCenter} />
        </div>
      )}
    </>
  );
}

function BreakdownCard({ title, rows }: { title: string; rows: CommissionDeskBreakdownRow[] }) {
  const max = Math.max(1, ...rows.map((r) => r.expectedAdvance));
  const pg = usePagination(rows);
  return (
    <Card>
      <CardHeader title={title} subtitle={`${rows.length} ${rows.length === 1 ? "entry" : "entries"}`} />
      <CardBody>
        {rows.length === 0 ? (
          <p className="text-sm text-ink-400">{COMMISSION_DASH_MSG.emptyDesc}</p>
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <THead>
                <TR>
                  <TH>{COMMISSION_DASH_MSG.colName}</TH>
                  <TH numeric>{COMMISSION_DASH_MSG.colSales}</TH>
                  <TH numeric>{COMMISSION_DASH_MSG.colPremium}</TH>
                  <TH numeric>{COMMISSION_DASH_MSG.colExpected}</TH>
                  <TH numeric>{COMMISSION_DASH_MSG.colFunded}</TH>
                  <TH numeric>{COMMISSION_DASH_MSG.colChargedBack}</TH>
                </TR>
              </THead>
              <TBody>
                {pg.pageItems.map((r) => (
                  <TR key={r.id} className="hover:bg-ink-50/60 transition-colors">
                    <TD>
                      <div className="font-medium text-ink-900 truncate max-w-[12rem]">{r.name || "—"}</div>
                      {/* A quiet bar makes the relative size scannable without a chart library. */}
                      <div className="mt-1 h-1 w-full max-w-[12rem] rounded-full bg-ink-100 overflow-hidden">
                        <div className="h-full rounded-full bg-brand-400"
                          style={{ width: `${Math.max(2, (r.expectedAdvance / max) * 100)}%` }} />
                      </div>
                    </TD>
                    <TD numeric className="tabular-nums text-sm">{r.saleCount}</TD>
                    <TD numeric className="tabular-nums text-sm">{formatUsd(r.premium)}</TD>
                    <TD numeric className="tabular-nums text-sm font-medium text-ink-900">{formatUsd(r.expectedAdvance)}</TD>
                    <TD numeric className={"tabular-nums text-sm " + (r.funded < 0 ? "text-rose-600" : "text-ink-700")}>
                      {formatUsd(r.funded)}
                    </TD>
                    <TD numeric className="tabular-nums text-sm">
                      {r.chargedBackCount > 0
                        ? <span className="text-rose-600">{r.chargedBackCount} · {formatUsd(r.chargedBackAmount)}</span>
                        : <span className="text-ink-400">—</span>}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
            <Pager {...pg} onPage={pg.setPage} unit="entries" />
          </div>
        )}
      </CardBody>
    </Card>
  );
}
