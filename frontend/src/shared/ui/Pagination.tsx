import { useEffect, useMemo, useState } from "react";
import { Icon } from "./Icon";
import { cn } from "./cn";

/**
 * Client-side pagination for a list already in memory. Returns the current page's slice plus the
 * metadata a <Pager> needs. Default page size is 10 (the app-wide "max 10 rows at a time" rule).
 * The page auto-clamps when the underlying list shrinks (filter/refresh) so it never strands the
 * user on an empty page.
 */
export function usePagination<T>(items: readonly T[] | undefined, pageSize = 10) {
  const total = items?.length ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const [page, setPage] = useState(0);

  useEffect(() => {
    if (page > pageCount - 1) setPage(pageCount - 1);
  }, [pageCount, page]);

  const pageItems = useMemo(
    () => (items ?? []).slice(page * pageSize, page * pageSize + pageSize),
    [items, page, pageSize],
  );

  const from = total === 0 ? 0 : page * pageSize + 1;
  const to = Math.min(total, (page + 1) * pageSize);
  return { page, setPage, pageCount, pageItems, total, from, to, pageSize };
}

/** Compact "Showing X–Y of Z" + prev/next control. Renders nothing when everything fits on one page. */
export function Pager({
  page, pageCount, total, from, to, onPage, unit = "rows", className,
}: {
  page: number;
  pageCount: number;
  total: number;
  from: number;
  to: number;
  onPage: (p: number) => void;
  unit?: string;
  className?: string;
}) {
  if (pageCount <= 1) return null;
  const btn =
    "inline-flex items-center justify-center h-7 w-7 rounded-md border hairline text-ink-600 " +
    "hover:bg-ink-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors " +
    "focus-visible:outline focus-visible:outline-2 focus-visible:outline-brand-500";
  return (
    <div className={cn("flex items-center justify-between gap-3 pt-3 mt-1 border-t hairline text-xs text-ink-500", className)}>
      <span className="tabular-nums">Showing {from}–{to} of {total} {unit}</span>
      <div className="flex items-center gap-1">
        <button type="button" className={btn} onClick={() => onPage(page - 1)} disabled={page === 0}
          aria-label="Previous page" title="Previous page">
          <Icon name="chevronLeft" size={14} />
        </button>
        <span className="tabular-nums px-1 select-none">{page + 1} / {pageCount}</span>
        <button type="button" className={btn} onClick={() => onPage(page + 1)} disabled={page >= pageCount - 1}
          aria-label="Next page" title="Next page">
          <Icon name="chevronRight" size={14} />
        </button>
      </div>
    </div>
  );
}
