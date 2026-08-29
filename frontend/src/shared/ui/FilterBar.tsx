import { useState, type ReactNode } from "react";
import { Badge } from "./Badge";
import { Button } from "./Button";
import { Card, CardBody } from "./Card";
import { Icon } from "./Icon";
import { FILTERS } from "../constants/messages";

/**
 * Search plus a small number of visible filters, with the rest folded away.
 *
 * WHY: screens grew a row of five or six dropdowns, all equally prominent, all always on. That reads
 * as a control panel rather than a list, and it hides the one control most people want — search —
 * among things they will use once a month. The common filters stay out; the rest live behind "More
 * filters", which announces how many are hidden.
 *
 * It also answers "why am I seeing so few rows?", which an always-on filter row never does: when
 * anything is active, the count is shown next to a Clear button.
 */
export function FilterBar({
  search, onSearchChange, searchPlaceholder,
  primary, advanced, activeCount, onClear, trailing,
}: {
  search: string;
  onSearchChange: (v: string) => void;
  searchPlaceholder?: string;
  /** Always visible — the two or three filters people reach for daily. */
  primary?: ReactNode;
  /** Folded behind "More filters". Omit entirely if there are none. */
  advanced?: ReactNode;
  /** How many filters are currently narrowing the list (search excluded). */
  activeCount?: number;
  onClear?: () => void;
  /** Right-aligned extras, e.g. a result count or a secondary button. */
  trailing?: ReactNode;
}) {
  const [showAdvanced, setShowAdvanced] = useState(false);
  const active = activeCount ?? 0;

  return (
    <Card className="mb-4">
      <CardBody className="flex items-center gap-3 flex-wrap">
        <div className="flex-1 min-w-[240px]">
          <label className="sr-only" htmlFor="filterbar-search">{FILTERS.searchLabel}</label>
          <div className="relative">
            <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-400">
              <Icon name="search" size={15} />
            </span>
            <input
              id="filterbar-search"
              value={search}
              onChange={(e) => onSearchChange(e.target.value)}
              placeholder={searchPlaceholder ?? FILTERS.searchPlaceholder}
              className="h-10 w-full rounded-xl border border-ink-200 bg-white pl-9 pr-9 text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none focus:ring-2 focus:ring-brand-500/40 focus:border-brand-400"
            />
            {search && (
              <button
                type="button"
                onClick={() => onSearchChange("")}
                aria-label={FILTERS.clearSearch}
                className="absolute right-2 top-1/2 -translate-y-1/2 h-6 w-6 grid place-items-center rounded-md text-ink-400 hover:bg-ink-100 hover:text-ink-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
              >
                <Icon name="x" size={13} />
              </button>
            )}
          </div>
        </div>

        {primary}

        {advanced && (
          <Button
            variant={showAdvanced ? "primary" : "outline"}
            leftIcon={<Icon name="filter" size={14} />}
            onClick={() => setShowAdvanced((v) => !v)}
            aria-expanded={showAdvanced}
          >
            {FILTERS.moreFilters}
          </Button>
        )}

        {active > 0 && onClear && (
          <span className="inline-flex items-center gap-2">
            <Badge tone="brand" variant="soft">{FILTERS.activeCount(active)}</Badge>
            <Button variant="ghost" size="sm" onClick={onClear}>{FILTERS.clearAll}</Button>
          </span>
        )}

        {trailing && <span className="ml-auto">{trailing}</span>}
      </CardBody>

      {advanced && showAdvanced && (
        <div className="px-5 pb-4 -mt-1 flex items-center gap-3 flex-wrap border-t hairline pt-4">
          {advanced}
        </div>
      )}
    </Card>
  );
}
