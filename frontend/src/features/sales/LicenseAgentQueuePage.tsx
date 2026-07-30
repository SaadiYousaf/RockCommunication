import { Link } from "react-router-dom";
import { useListSalesQuery } from "../../shared/api/baseApi";
import type { SaleListItem } from "../../shared/api/baseApi";
import type { BadgeTone } from "../../shared/ui";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR,
} from "../../shared/ui";
import { timeAgoShort, waitTone } from "../../shared/lib/time";
import { useTableSort } from "../../shared/hooks/useTableSort";

const money = (n: number | null | undefined) =>
  n == null ? "—" : n.toLocaleString(undefined, { style: "currency", currency: "USD" });

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
  const { sorted, dirFor, toggle } = useTableSort(items, {
    accessors: { status: (s) => statusOf(s).label },
  });

  return (
    <>
      <PageHeader
        title="My Sales"
        description="Sales assigned to you as the licensed agent of record — review each and track the commission you've earned."
      />
      <Card>
        <CardHeader
          title="Assigned to me"
          subtitle={data ? `${items.length === 1 ? "1 sale" : `${items.length} sales`} · ${money(totalCommission)} commission` : undefined}
        />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : items.length === 0 ? (
            <EmptyState
              icon={<Icon name="briefcase" size={20} />}
              title="No sales assigned yet"
              description="When a submission agent assigns a sale to you, it'll appear here — and you'll get a notification."
            />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH sortDir={dirFor("saleNumber")} onClick={() => toggle("saleNumber")}>#</TH><TH sortDir={dirFor("leadName")} onClick={() => toggle("leadName")}>Customer</TH><TH sortDir={dirFor("carrier")} onClick={() => toggle("carrier")}>Carrier</TH><TH sortDir={dirFor("monthlyPremium")} onClick={() => toggle("monthlyPremium")}>Premium</TH>
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
                    <TR key={s.id}>
                      <TD className="tabular-nums text-ink-500">#{s.saleNumber}</TD>
                      <TD>
                        <div className="font-medium text-ink-900 whitespace-nowrap">{s.leadName}</div>
                        <div className="font-mono text-xs text-ink-500 tabular-nums whitespace-nowrap">{s.leadPhone}</div>
                      </TD>
                      <TD className="text-sm whitespace-nowrap">{s.carrier}</TD>
                      <TD className="text-sm tabular-nums whitespace-nowrap">{money(s.monthlyPremium)}/mo</TD>
                      <TD><Badge tone={st.tone} variant="soft">{st.label}</Badge></TD>
                      <TD className="text-sm tabular-nums font-medium text-ink-800">{money(s.commissionEarned ?? 0)}</TD>
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
        </CardBody>
      </Card>
    </>
  );
}
