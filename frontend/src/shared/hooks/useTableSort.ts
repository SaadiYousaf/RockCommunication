import { useMemo, useState } from "react";

export type SortDir = "asc" | "desc";

/**
 * Client-side table sorting that pairs with the kit's <TH sortDir> caret.
 *
 *   const { sorted, dirFor, toggle } = useTableSort(rows, { key: "name" });
 *   <TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Name</TH>
 *   {sorted.map(...)}
 *
 * By default a column sorts by `row[key]`; pass `accessors` for computed columns
 * (e.g. a derived total or a joined name). Strings compare naturally (numeric-aware),
 * numbers numerically, nulls sink to the bottom.
 */
export function useTableSort<T>(
  rows: T[] | undefined,
  opts?: { key?: string; dir?: SortDir; accessors?: Record<string, (row: T) => string | number | null | undefined> },
) {
  const [key, setKey] = useState<string | undefined>(opts?.key);
  const [dir, setDir] = useState<SortDir>(opts?.dir ?? "asc");
  const accessors = opts?.accessors;

  const sorted = useMemo(() => {
    const data = [...(rows ?? [])];
    if (!key) return data;
    const get = accessors?.[key] ?? ((r: T) => (r as Record<string, unknown>)[key] as string | number | null | undefined);
    data.sort((a, b) => {
      const av = get(a), bv = get(b);
      let cmp: number;
      if (av == null && bv == null) cmp = 0;
      else if (av == null) cmp = 1;          // nulls last
      else if (bv == null) cmp = -1;
      else if (typeof av === "number" && typeof bv === "number") cmp = av - bv;
      else cmp = String(av).localeCompare(String(bv), undefined, { numeric: true, sensitivity: "base" });
      return dir === "asc" ? cmp : -cmp;
    });
    return data;
  }, [rows, key, dir, accessors]);

  function toggle(k: string) {
    if (key === k) setDir((d) => (d === "asc" ? "desc" : "asc"));
    else { setKey(k); setDir("asc"); }
  }
  const dirFor = (k: string): SortDir | null => (key === k ? dir : null);

  return { sorted, toggle, dirFor, sortKey: key, sortDir: dir };
}
