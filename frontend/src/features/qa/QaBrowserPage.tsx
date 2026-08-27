import { useMemo, useState } from "react";
import { useListQaReviewsQuery, useQaScorecardsQuery, useUserDirectoryQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, BulkActionBar, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, PageHeader,
  Pager, Skeleton, Table, TBody, TD, TH, THead, TR, useToast, usePagination,
} from "../../shared/ui";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { QA_MSG } from "./messages";

function scoreTone(pct: number): "success" | "warning" | "danger" {
  if (pct >= 85) return "success";
  if (pct >= 70) return "warning";
  return "danger";
}

export function QaBrowserPage() {
  const [from, setFrom] = useState(() => new Date(Date.now() - 30 * 86400 * 1000).toISOString().slice(0, 10));
  const [to, setTo]     = useState(() => new Date(Date.now() + 86400 * 1000).toISOString().slice(0, 10));
  const { data: reviews, isLoading: reviewsLoading } = useListQaReviewsQuery({ from, to });
  const { data: scorecards, isLoading: cardsLoading } = useQaScorecardsQuery({ from, to });
  // The QA DTOs carry only user ids; resolve display names from the directory.
  const { data: directory } = useUserDirectoryQuery();
  const nameOf = useMemo(() => {
    const m = new Map((directory ?? []).map((u) => [u.id, u.userName]));
    return (id: string) => m.get(id) ?? id.slice(0, 8);
  }, [directory]);

  const { sorted: sortedScorecards, dirFor: scorecardDir, toggle: sortScorecard } = useTableSort(scorecards, {
    accessors: { agent: (s) => nameOf(s.agentUserId) },
  });
  const { sorted: sortedReviews, dirFor: reviewDir, toggle: sortReview } = useTableSort(reviews, {
    accessors: {
      agent: (r) => nameOf(r.agentUserId),
      reviewer: (r) => nameOf(r.reviewerUserId),
      when: (r) => Date.parse(r.reviewedAt),
    },
  });

  // Each table pages on its own (10 rows a page), over the sorted list so paging follows the sort.
  const cardsPg = usePagination(sortedScorecards);
  const reviewsPg = usePagination(sortedReviews);

  const toast = useToast();
  // Selection and CSV export stay on the full sorted review list, not just the visible page.
  const sel = useRowSelection(sortedReviews.map((r) => r.id));

  function exportSelected() {
    const chosen = sortedReviews.filter((r) => sel.isSelected(r.id));
    exportRowsToCsv(chosen, [
      { header: "Agent", value: (r) => nameOf(r.agentUserId) },
      { header: "Reviewer", value: (r) => nameOf(r.reviewerUserId) },
      { header: "Score", value: (r) => `${r.totalScore} / ${r.maxScore}` },
      { header: "Date", value: (r) => new Date(r.reviewedAt).toLocaleString() },
    ], `qa-reviews-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(QA_MSG.exportReadyTitle, QA_MSG.exportReadyDesc(chosen.length));
  }

  return (
    <>
      <PageHeader
        title="QA Browser"
        description="Inspect agent scorecards and individual call reviews across a period."
      />

      <Card className="mb-6">
        <CardBody className="flex flex-wrap items-end gap-4">
          <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} containerClassName="w-44" />
          <Input label="To"   type="date" value={to}   onChange={(e) => setTo(e.target.value)}   containerClassName="w-44" />
          <div className="ml-auto flex gap-3 text-xs text-ink-500">
            <span className="tabular-nums whitespace-nowrap">{scorecards?.length ?? 0} agents</span>
            <span>·</span>
            <span className="tabular-nums whitespace-nowrap">{reviews?.length ?? 0} reviews</span>
          </div>
        </CardBody>
      </Card>

      {/* Agent scorecards */}
      <Card className="mb-6">
        <CardHeader
          title={<span className="inline-flex items-center gap-2"><Icon name="users" size={16} className="text-ink-400" />Agent scorecards</span>}
          subtitle="Performance summary in selected period"
        />
        <CardBody className="pt-0 px-0">
          {cardsLoading ? (
            <div className="px-5 pb-5 space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)}</div>
          ) : !scorecards || scorecards.length === 0 ? (
            <div className="px-5 pb-5">
              <EmptyState
                icon={<Icon name="star" size={20} />}
                title={QA_MSG.noScorecardsTitle}
                description={QA_MSG.noScorecardsDesc}
              />
            </div>
          ) : (
            <Table className="border-0 shadow-none rounded-none">
              <THead>
                <TR>
                  <TH sortDir={scorecardDir("agent")} onClick={() => sortScorecard("agent")}>Agent</TH>
                  <TH sortDir={scorecardDir("reviewCount")} onClick={() => sortScorecard("reviewCount")}>Reviews</TH>
                  <TH sortDir={scorecardDir("avgPercentage")} onClick={() => sortScorecard("avgPercentage")}><span className="inline-flex items-center gap-1">Avg %<InfoHint title="Avg %" side="top">The agent's average call-quality score as a percentage of the maximum across their reviews in this period.</InfoHint></span></TH>
                  <TH sortDir={scorecardDir("avgScore")} onClick={() => sortScorecard("avgScore")}><span className="inline-flex items-center gap-1">Avg score<InfoHint title="Avg score" side="top">The agent's average raw points scored across their reviews in this period.</InfoHint></span></TH>
                  <TH><span className="inline-flex items-center gap-1">Performance<InfoHint title="Performance" side="left">Colour band for the average score: green at or above 85%, amber at or above 70%, red below.</InfoHint></span></TH>
                </TR>
              </THead>
              <TBody>
                {cardsPg.pageItems.map((s) => (
                  <TR key={s.agentUserId}>
                    <TD>
                      <div className="flex items-center gap-3 min-w-0">
                        <Avatar name={nameOf(s.agentUserId)} size={32} />
                        <div className="text-xs text-ink-700 truncate">{nameOf(s.agentUserId)}</div>
                      </div>
                    </TD>
                    <TD className="font-semibold text-ink-900 tabular-nums">{s.reviewCount}</TD>
                    <TD>
                      <Badge tone={scoreTone(s.avgPercentage)} variant="soft" className="tabular-nums">{s.avgPercentage}%</Badge>
                    </TD>
                    <TD className="text-ink-700 tabular-nums">{s.avgScore}</TD>
                    <TD>
                      <div className="h-2 w-32 bg-ink-100 rounded-full overflow-hidden">
                        <div
                          className={`h-full ${
                            scoreTone(s.avgPercentage) === "success" ? "bg-emerald-500" :
                            scoreTone(s.avgPercentage) === "warning" ? "bg-amber-500" : "bg-rose-500"
                          }`}
                          style={{ width: `${Math.min(100, s.avgPercentage)}%` }}
                        />
                      </div>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
          <Pager {...cardsPg} onPage={cardsPg.setPage} unit="agents" className="px-5" />
        </CardBody>
      </Card>

      {/* Individual reviews */}
      <Card>
        <CardHeader
          title={<span className="inline-flex items-center gap-2"><Icon name="star" size={16} className="text-ink-400" />Reviews</span>}
          subtitle="Every QA review submitted in this range"
        />
        <CardBody className="pt-0 px-0">
          {reviewsLoading ? (
            <div className="px-5 pb-5 space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)}</div>
          ) : !reviews || reviews.length === 0 ? (
            <div className="px-5 pb-5">
              <EmptyState
                icon={<Icon name="doc" size={20} />}
                title={QA_MSG.noReviewsTitle}
                description={QA_MSG.noReviewsDesc}
              />
            </div>
          ) : (
            <Table className="border-0 shadow-none rounded-none">
              <THead>
                <TR>
                  <TH className="w-10"><Checkbox aria-label="Select all reviews" {...sel.allCheckboxProps} /></TH>
                  <TH sortDir={reviewDir("when")} onClick={() => sortReview("when")}>When</TH>
                  <TH sortDir={reviewDir("agent")} onClick={() => sortReview("agent")}>Agent</TH>
                  <TH sortDir={reviewDir("reviewer")} onClick={() => sortReview("reviewer")}>Reviewer</TH>
                  <TH sortDir={reviewDir("totalScore")} onClick={() => sortReview("totalScore")}>Score</TH>
                  <TH sortDir={reviewDir("percentage")} onClick={() => sortReview("percentage")}>%</TH>
                  <TH sortDir={reviewDir("notes")} onClick={() => sortReview("notes")}>Notes</TH>
                </TR>
              </THead>
              <TBody>
                {reviewsPg.pageItems.map((r) => (
                  <TR key={r.id} className={sel.isSelected(r.id) ? "bg-brand-50/40" : undefined}>
                    <TD>
                      <Checkbox aria-label={`Select review by ${nameOf(r.agentUserId)}`} {...sel.checkboxProps(r.id)} />
                    </TD>
                    <TD className="text-ink-600 text-xs whitespace-nowrap tabular-nums">{new Date(r.reviewedAt).toLocaleString()}</TD>
                    <TD className="text-xs text-ink-700 truncate">{nameOf(r.agentUserId)}</TD>
                    <TD className="text-xs text-ink-500 truncate">{nameOf(r.reviewerUserId)}</TD>
                    <TD className="text-ink-800 tabular-nums whitespace-nowrap">{r.totalScore} <span className="text-ink-400">/ {r.maxScore}</span></TD>
                    <TD>
                      <Badge tone={scoreTone(r.percentage)} variant="soft" className="tabular-nums">{r.percentage}%</Badge>
                    </TD>
                    <TD className="text-ink-600 text-sm max-w-xs truncate">{r.notes ?? <span className="text-ink-400">—</span>}</TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
          <Pager {...reviewsPg} onPage={reviewsPg.setPage} unit="reviews" className="px-5" />
        </CardBody>
      </Card>

      <BulkActionBar
        count={sel.selectedCount} itemNoun="review" onClear={sel.clear}
        actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
      />
    </>
  );
}
