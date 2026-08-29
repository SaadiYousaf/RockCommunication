import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { MESSAGES } from "../../shared/constants/messages";
import {
  useListCommissionSalesQuery, useListAgenciesQuery, useAgencyCallCentersQuery,
} from "../../shared/api/baseApi";
import type { CommissionSale } from "../../shared/api/types";
import { formatUsd } from "../../shared/lib/format";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, ErrorState, Icon, InfoHint, Input, SearchInput,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { COMMISSION_MSG, commissionStatusLabel, commissionStatusTone, COMMISSION_SETTABLE_STATUSES } from "./messages";
import { StatusModal } from "./StatusModal";
import { AmountsModal } from "./AmountsModal";

const PAGE = 25;
const shortDate = (iso?: string | null) =>
  iso ? new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" }) : "—";

/**
 * The Commission Agent's landing screen: every submitted sale across all agencies and call centres,
 * with the desk's filters. Each row shows the customer, carrier/coverage/premium, where it came from,
 * its financial status, the commission ledger total, and the carrier's advancing terms (read-only —
 * those come from Carrier Rules). Row actions set the status or open the amounts for a charged-back sale.
 */
export function CommissionDeskPage() {
  const navigate = useNavigate();
  const toast = useToast();

  // ---- Filters -------------------------------------------------------------
  const [agencyId, setAgencyId] = useState("");
  const [callCenterId, setCallCenterId] = useState("");
  const [carrier, setCarrier] = useState("");
  const [status, setStatus] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [search, setSearch] = useState("");
  const [skip, setSkip] = useState(0);

  const { data: agencies } = useListAgenciesQuery();
  const { data: callCenters } = useAgencyCallCentersQuery(agencyId, { skip: !agencyId });

  const { data, isLoading, isFetching, isError, error, refetch } = useListCommissionSalesQuery({
    agencyId: agencyId || undefined,
    callCenterId: callCenterId || undefined,
    carrier: carrier || undefined,
    status: status || undefined,
    from: from || undefined,
    to: to || undefined,
    search: search.trim() || undefined,
    skip, take: PAGE,
  });

  const items = data?.items ?? [];
  const hasFilters = !!(agencyId || callCenterId || carrier || status || from || to || search);

  function clearFilters() {
    setAgencyId(""); setCallCenterId(""); setCarrier(""); setStatus("");
    setFrom(""); setTo(""); setSearch(""); setSkip(0);
  }

  // Carrier options come from what's actually in the list, so the filter never offers a dead option.
  const carrierOptions = useMemo(() => {
    const set = new Set<string>();
    for (const s of items) {
      const c = s.carrierApproved || s.carrier;
      if (c) set.add(c);
    }
    return Array.from(set).sort();
  }, [items]);

  const [statusFor, setStatusFor] = useState<CommissionSale | null>(null);
  const [amountsFor, setAmountsFor] = useState<CommissionSale | null>(null);

  return (
    <>
      <PageHeaderBlock />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-4">
        <Stat label={COMMISSION_MSG.statSales} value={data?.total ?? 0} icon={<Icon name="briefcase" size={18} />}
          hint={COMMISSION_MSG.statSalesHint} tone="brand" />
        <Stat label={COMMISSION_MSG.statPremium} value={formatUsd(data?.totalPremium)} icon={<Icon name="dollar" size={18} />}
          hint={COMMISSION_MSG.statPremiumHint} tone="accent" />
        <Stat label={COMMISSION_MSG.statFunded} value={formatUsd(data?.totalFunded)} icon={<Icon name="chart" size={18} />}
          hint={COMMISSION_MSG.statFundedHint} tone={(data?.totalFunded ?? 0) < 0 ? "danger" : "success"} />
      </div>

      <Card>
        <CardHeader
          title={COMMISSION_MSG.title}
          subtitle={data ? `${data.total} sale${data.total === 1 ? "" : "s"}` : undefined}
          action={<SearchInput value={search} onChange={(v) => { setSearch(v); setSkip(0); }}
            placeholder={COMMISSION_MSG.searchPlaceholder} className="w-72" />}
        />
        <CardBody>
          {/* Filters */}
          <div className="flex flex-wrap items-end gap-2 mb-4">
            <Select aria-label={COMMISSION_MSG.allAgencies} className="w-44" value={agencyId}
              onChange={(e) => { setAgencyId(e.target.value); setCallCenterId(""); setSkip(0); }}>
              <option value="">{COMMISSION_MSG.allAgencies}</option>
              {(agencies ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </Select>
            <Select aria-label={COMMISSION_MSG.allCallCenters} className="w-44" value={callCenterId}
              disabled={!agencyId} onChange={(e) => { setCallCenterId(e.target.value); setSkip(0); }}>
              <option value="">{COMMISSION_MSG.allCallCenters}</option>
              {(callCenters ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
            <Select aria-label={COMMISSION_MSG.allCarriers} className="w-40" value={carrier}
              onChange={(e) => { setCarrier(e.target.value); setSkip(0); }}>
              <option value="">{COMMISSION_MSG.allCarriers}</option>
              {carrierOptions.map((c) => <option key={c} value={c}>{c}</option>)}
            </Select>
            <Select aria-label={COMMISSION_MSG.allStatuses} className="w-44" value={status}
              onChange={(e) => { setStatus(e.target.value); setSkip(0); }}>
              <option value="">{COMMISSION_MSG.allStatuses}</option>
              {COMMISSION_SETTABLE_STATUSES.map((s) => <option key={s} value={s}>{commissionStatusLabel(s)}</option>)}
            </Select>
            <Input aria-label={COMMISSION_MSG.from} type="date" className="w-40" value={from}
              onChange={(e) => { setFrom(e.target.value); setSkip(0); }} />
            <Input aria-label={COMMISSION_MSG.to} type="date" className="w-40" value={to}
              onChange={(e) => { setTo(e.target.value); setSkip(0); }} />
            {hasFilters && (
              <Button variant="ghost" size="sm" leftIcon={<Icon name="x" size={14} />} onClick={clearFilters}>
                {COMMISSION_MSG.clearFilters}
              </Button>
            )}
          </div>

          {isLoading ? (
            <Skeleton className="h-64" />
          ) : isError ? (
            // A failed request used to land on "no sales match these filters", which sent the desk
            // clearing filters that were never the problem.
            <ErrorState error={error} resource={COMMISSION_MSG.resourceName} onRetry={refetch} />
          ) : items.length === 0 ? (
            <EmptyState icon={<Icon name="dollar" size={20} />}
              title={hasFilters ? COMMISSION_MSG.noMatchTitle : COMMISSION_MSG.emptyTitle}
              description={hasFilters ? COMMISSION_MSG.noMatchDesc : COMMISSION_MSG.emptyDesc} />
          ) : (
            <>
              <div className="overflow-x-auto">
                <Table>
                  <THead>
                    <TR>
                      <TH>{COMMISSION_MSG.colCustomer}</TH>
                      <TH>{COMMISSION_MSG.colCarrier}</TH>
                      <TH numeric>{COMMISSION_MSG.colCoverage}</TH>
                      <TH numeric>{COMMISSION_MSG.colPremium}</TH>
                      <TH>{COMMISSION_MSG.colAgency}</TH>
                      <TH>{COMMISSION_MSG.colCallCenter}</TH>
                      <TH>{COMMISSION_MSG.colStatus}</TH>
                      <TH numeric>{COMMISSION_MSG.colFunded}</TH>
                      <TH>
                        <span className="inline-flex items-center gap-1">
                          {COMMISSION_MSG.colAdvancing}
                          <InfoHint title={COMMISSION_MSG.colAdvancing} side="top">{COMMISSION_MSG.advancingHint}</InfoHint>
                        </span>
                      </TH>
                      <TH>{COMMISSION_MSG.colSold}</TH>
                      <TH></TH>
                    </TR>
                  </THead>
                  <TBody>
                    {items.map((s) => (
                      <TR key={s.saleId} className="hover:bg-ink-50/60 transition-colors">
                        <TD>
                          <button type="button" onClick={() => navigate(`/leads/${s.leadId}`)}
                            className="text-left font-medium text-brand-600 hover:text-brand-700 hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/30 rounded">
                            {s.customerName || `#${s.saleNumber}`}
                          </button>
                          <div className="text-xs text-ink-500">{s.phoneNumber || "—"}</div>
                        </TD>
                        <TD className="text-sm text-ink-700">
                          {s.carrierApproved || s.carrier}
                          {s.planApproved && <div className="text-xs text-ink-500">{s.planApproved}</div>}
                        </TD>
                        <TD numeric className="tabular-nums text-sm">{s.coverageApproved != null ? formatUsd(s.coverageApproved) : "—"}</TD>
                        <TD numeric className="tabular-nums text-sm">{formatUsd(s.premiumApproved ?? s.monthlyPremium)}</TD>
                        <TD className="text-sm text-ink-600 truncate max-w-[10rem]">{s.agencyName || "—"}</TD>
                        <TD className="text-sm text-ink-600 truncate max-w-[10rem]">{s.callCenterName || "—"}</TD>
                        <TD><Badge tone={commissionStatusTone(s.status)} variant="soft">{commissionStatusLabel(s.status)}</Badge></TD>
                        <TD numeric className={"tabular-nums text-sm font-medium " + (s.fundedAmount < 0 ? "text-rose-600" : "text-ink-800")}>
                          {formatUsd(s.fundedAmount)}
                        </TD>
                        <TD className="text-sm">
                          {s.advanceRate != null ? (
                            <div className="text-ink-700">
                              {s.advanceRate}% · {s.advancedMonths} mo
                              {s.expectedAdvance != null && (
                                <div className="text-xs text-ink-500 tabular-nums">{formatUsd(s.expectedAdvance)}</div>
                              )}
                            </div>
                          ) : <span className="text-ink-400">{COMMISSION_MSG.noRule}</span>}
                        </TD>
                        <TD className="text-sm text-ink-500 whitespace-nowrap">{shortDate(s.soldAt)}</TD>
                        <TD className="text-right whitespace-nowrap">
                          <div className="inline-flex gap-1.5">
                            <Button size="sm" variant="ghost" onClick={() => setAmountsFor(s)}>{COMMISSION_MSG.viewAmounts}</Button>
                            <Button size="sm" variant="outline" leftIcon={<Icon name="edit" size={13} />}
                              onClick={() => setStatusFor(s)}>{COMMISSION_MSG.updateStatus}</Button>
                          </div>
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              </div>

              {/* Server-side paging: the desk spans every agency, so the list can be large. */}
              <ServerPager total={data?.total ?? 0} skip={skip} take={PAGE} busy={isFetching} onSkip={setSkip} />
            </>
          )}
        </CardBody>
      </Card>

      {statusFor && (
        <StatusModal sale={statusFor} onClose={() => setStatusFor(null)}
          onDone={(name, label) => {
            toast.success(COMMISSION_MSG.statusUpdated, COMMISSION_MSG.statusUpdatedDesc(name, label));
            setStatusFor(null);
          }}
          onError={(detail) => toast.error(COMMISSION_MSG.statusFailed, detail ?? MESSAGES.tryAgain)} />
      )}
      {amountsFor && (
        <AmountsModal sale={amountsFor} onClose={() => setAmountsFor(null)}
          onSaved={() => toast.success(COMMISSION_MSG.amountSaved)}
          onError={(detail) => toast.error(COMMISSION_MSG.amountFailed, detail ?? MESSAGES.tryAgain)} />
      )}
    </>
  );
}

function PageHeaderBlock() {
  return (
    <div className="mb-5">
      <div className="section-title mb-1.5 flex items-center gap-1.5">
        <span aria-hidden className="inline-block h-1 w-1 rounded-full bg-brand-500" /> {COMMISSION_MSG.eyebrow}
      </div>
      <h1 className="text-2xl font-semibold tracking-tight text-ink-900">{COMMISSION_MSG.title}</h1>
      <p className="text-sm text-ink-500 mt-1 max-w-3xl">{COMMISSION_MSG.description}</p>
    </div>
  );
}

/** Simple server-side pager (the shared Pager works on an in-memory slice). */
function ServerPager({ total, skip, take, busy, onSkip }: {
  total: number; skip: number; take: number; busy: boolean; onSkip: (n: number) => void;
}) {
  if (total <= take) return null;
  const page = Math.floor(skip / take) + 1;
  const pages = Math.max(1, Math.ceil(total / take));
  return (
    <div className="flex items-center justify-between gap-3 mt-3 text-sm">
      <span className="text-ink-500">
        {skip + 1}–{Math.min(skip + take, total)} of {total}
      </span>
      <div className="flex gap-2">
        <Button size="sm" variant="outline" disabled={skip === 0 || busy}
          onClick={() => onSkip(Math.max(0, skip - take))} leftIcon={<Icon name="chevronLeft" size={14} />}>Previous</Button>
        <span className="text-ink-500 self-center tabular-nums">{page} / {pages}</span>
        <Button size="sm" variant="outline" disabled={skip + take >= total || busy}
          onClick={() => onSkip(skip + take)} rightIcon={<Icon name="chevronRight" size={14} />}>Next</Button>
      </div>
    </div>
  );
}
