import { useMemo, useState } from "react";
import {
  Badge, Card, CardBody, EmptyState, ErrorState, Icon, InfoHint, Input, PageHeader, Pager,
  SearchInput, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, usePagination,
} from "../../shared/ui";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useListReflectionsQuery, type ShiftReflection } from "./reflectionsApi";
import { HR_MSG } from "./messages";

/** YYYY-MM-DD, which is both what the date inputs want and what the API accepts. */
const isoDay = (d: Date) => d.toISOString().slice(0, 10);

/**
 * HR → End-of-day check-ins.
 *
 * The questions are asked at sign-out and answering is optional, so this page is designed around
 * sparse data: gaps are the normal case and are never dressed up as a problem. The one thing it
 * does surface is a "No" — somebody saying they don't feel they did justice with their day — which
 * is why that is both a filter and a figure at the top, rather than a row you have to go looking for.
 */
export function ReflectionsPage() {
  const [search, setSearch] = useState("");
  const [from, setFrom] = useState(() => isoDay(new Date(Date.now() - 13 * 86_400_000)));
  const [to, setTo] = useState(() => isoDay(new Date()));
  const [onlyConcerns, setOnlyConcerns] = useState(false);

  const { data, isLoading, isError, error, refetch } = useListReflectionsQuery({ from, to });

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (data ?? []).filter((r) => {
      if (onlyConcerns && r.didJustice !== false) return false;
      if (term && !r.userName.toLowerCase().includes(term)) return false;
      return true;
    });
  }, [data, search, onlyConcerns]);

  const { sorted, toggle, dirFor } = useTableSort<ShiftReflection>(filtered, {
    key: "shiftDate",
    dir: "desc",
    accessors: {
      shiftDate: (r) => r.shiftDate,
      userName: (r) => r.userName,
      // Unanswered sorts last rather than between yes and no: it is an absence, not a middle value.
      didJustice: (r) => (r.didJustice === null ? 2 : r.didJustice ? 1 : 0),
    },
  });

  const { page, setPage, pageCount, pageItems, total, from: shownFrom, to: shownTo } = usePagination(sorted, 25);

  const concerns = (data ?? []).filter((r) => r.didJustice === false).length;
  const people = new Set((data ?? []).map((r) => r.userId)).size;

  return (
    <div className="space-y-5">
      <PageHeader title={HR_MSG.reflectionsTitle} description={HR_MSG.reflectionsSubtitle} />

      <div className="grid gap-3 sm:grid-cols-3">
        <Stat label={HR_MSG.reflectionsStatTotal} value={(data ?? []).length} />
        <Stat label={HR_MSG.reflectionsStatPeople} value={people} />
        <Stat label={HR_MSG.reflectionsStatConcerns} value={concerns} tone="warning" />
      </div>

      <Card>
        <CardBody className="space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <SearchInput
              value={search}
              onChange={setSearch}
              placeholder={HR_MSG.reflectionsSearch}
              containerClassName="w-full sm:w-64"
            />
            <Input
              type="date"
              label={HR_MSG.reflectionsFrom}
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              containerClassName="w-40"
            />
            <Input
              type="date"
              label={HR_MSG.reflectionsTo}
              value={to}
              onChange={(e) => setTo(e.target.value)}
              containerClassName="w-40"
            />
            <button
              type="button"
              aria-pressed={onlyConcerns}
              onClick={() => setOnlyConcerns((v) => !v)}
              className={
                onlyConcerns
                  ? "inline-flex items-center gap-2 rounded-full border border-amber-300 bg-amber-50 px-3 py-1.5 text-xs font-medium text-amber-800"
                  : "inline-flex items-center gap-2 rounded-full border border-ink-200 px-3 py-1.5 text-xs font-medium text-ink-600 hover:border-ink-300 hover:bg-ink-50"
              }
            >
              <Icon name="shield" size={13} />
              {HR_MSG.reflectionsOnlyConcerns}
              <span className="tabular-nums">{concerns}</span>
            </button>
            <InfoHint>{HR_MSG.reflectionsOnlyConcernsHint}</InfoHint>
          </div>

          {isLoading && <Skeleton className="h-64 w-full" />}

          {isError && !isLoading && <ErrorState error={error} onRetry={refetch} resource="check-ins" />}

          {!isLoading && !isError && total === 0 && (
            <EmptyState
              icon={<Icon name="chat" size={20} />}
              title={HR_MSG.reflectionsEmptyTitle}
              description={(data ?? []).length ? HR_MSG.reflectionsNoMatchDesc : HR_MSG.reflectionsEmptyDesc}
            />
          )}

          {!isLoading && !isError && total > 0 && (
            <>
              <Table>
                <THead>
                  <TR>
                    <TH sortDir={dirFor("shiftDate")} onClick={() => toggle("shiftDate")}>
                      {HR_MSG.reflectionsColDate}
                    </TH>
                    <TH sortDir={dirFor("userName")} onClick={() => toggle("userName")}>
                      {HR_MSG.reflectionsColPerson}
                    </TH>
                    <TH sortDir={dirFor("didJustice")} onClick={() => toggle("didJustice")}>
                      {HR_MSG.reflectionsColJustice}
                    </TH>
                    <TH>{HR_MSG.reflectionsColLearned}</TH>
                    <TH>{HR_MSG.reflectionsColNote}</TH>
                  </TR>
                </THead>
                <TBody>
                  {pageItems.map((r) => (
                    <TR key={r.id}>
                      <TD className="whitespace-nowrap tabular-nums">{r.shiftDate.slice(0, 10)}</TD>
                      <TD className="font-medium text-ink-900">{r.userName}</TD>
                      <TD><JusticeBadge value={r.didJustice} /></TD>
                      {/* Full text, wrapped. These are a few sentences at most, and truncating the
                          one thing somebody took the trouble to write defeats the point of asking. */}
                      <TD className="max-w-md whitespace-pre-wrap">
                        {r.learned || HR_MSG.reflectionsNoAnswer}
                      </TD>
                      <TD className="max-w-sm whitespace-pre-wrap" muted>
                        {r.justiceNote || HR_MSG.reflectionsNoAnswer}
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
              <Pager
                page={page}
                pageCount={pageCount}
                total={total}
                from={shownFrom}
                to={shownTo}
                onPage={setPage}
                unit="check-ins"
              />
            </>
          )}
        </CardBody>
      </Card>
    </div>
  );
}

function JusticeBadge({ value }: { value: boolean | null }) {
  if (value === null) return <span className="text-xs text-ink-400">{HR_MSG.reflectionsJusticeUnanswered}</span>;
  return value
    ? <Badge tone="success">{HR_MSG.reflectionsJusticeYes}</Badge>
    : <Badge tone="warning">{HR_MSG.reflectionsJusticeNo}</Badge>;
}
