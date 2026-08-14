import { useCallback, useMemo, useRef, useState } from "react";

/**
 * The public surface of a row-selection controller. Generic over any list whose
 * rows carry a stable string id. Designed to back a header "select all" checkbox,
 * per-row checkboxes (with shift-click range extend), and a bulk-action bar.
 */
export interface RowSelection {
  /** Ids currently selected AND still present in the visible list (never stale). */
  selectedIds: string[];
  selectedCount: number;
  isSelected: (id: string) => boolean;
  /**
   * Toggle a single row. Pass the click event (or `{ shiftKey }`) to extend the
   * selection from the last-clicked anchor to this row — the standard shift-click range.
   */
  toggle: (id: string, e?: { shiftKey?: boolean }) => void;
  /** Select every visible row when not all are selected; otherwise clear them. */
  toggleAll: () => void;
  clear: () => void;
  /** Reduce the selection to just these ids — e.g. after a partial bulk failure, keep the ones that failed. */
  keepOnly: (ids: string[]) => void;
  /** All visible rows are selected (and there is at least one). */
  allSelected: boolean;
  /** Some — but not all — visible rows are selected (drives the indeterminate box). */
  someSelected: boolean;
  /** At least one row is selected. */
  anySelected: boolean;
  /**
   * Props to spread onto a row's <Checkbox>. Wires plain toggle via onChange and
   * shift-click range via onClick (preventDefault stops the double-toggle). Every page
   * uses this so the interaction is identical and correct everywhere.
   */
  checkboxProps: (id: string) => {
    checked: boolean;
    onChange: () => void;
    onClick: (e: { shiftKey?: boolean; preventDefault: () => void }) => void;
  };
  /** Props to spread onto the header "select all" <Checkbox>. */
  allCheckboxProps: { checked: boolean; indeterminate: boolean; onChange: () => void };
}

/**
 * Row-selection state for a table.
 *
 * `ids` is the ordered list of the CURRENTLY VISIBLE rows (already filtered/sorted).
 * Selection is always intersected with this list, so a bulk action can never target
 * a row that has scrolled out of the current filter or been removed by a refetch.
 */
export function useRowSelection(ids: string[]): RowSelection {
  const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set());
  const anchorRef = useRef<string | null>(null);

  // Only ids that are still visible count — keeps bulk actions honest across refetches/filters.
  const visible = useMemo(() => new Set(ids), [ids]);
  const selectedIds = useMemo(() => ids.filter((id) => selected.has(id)), [ids, selected]);
  const selectedCount = selectedIds.length;
  const allSelected = ids.length > 0 && selectedCount === ids.length;
  const someSelected = selectedCount > 0 && selectedCount < ids.length;

  const isSelected = useCallback((id: string) => selected.has(id), [selected]);

  const toggle = useCallback((id: string, e?: { shiftKey?: boolean }) => {
    setSelected((prev) => {
      const next = new Set(prev);
      // Shift-click extends a contiguous range from the anchor to the clicked row,
      // matching the just-clicked row's resulting state (Gmail/Finder behaviour).
      if (e?.shiftKey && anchorRef.current && visible.has(anchorRef.current)) {
        const from = ids.indexOf(anchorRef.current);
        const to = ids.indexOf(id);
        if (from !== -1 && to !== -1) {
          const [lo, hi] = from < to ? [from, to] : [to, from];
          const select = !prev.has(id);
          for (let i = lo; i <= hi; i++) {
            if (select) next.add(ids[i]); else next.delete(ids[i]);
          }
          anchorRef.current = id;
          return next;
        }
      }
      if (next.has(id)) next.delete(id); else next.add(id);
      anchorRef.current = id;
      return next;
    });
  }, [ids, visible]);

  const toggleAll = useCallback(() => {
    setSelected((prev) => {
      // If every visible row is already selected, clear the visible ones; else select them all.
      const allOn = ids.length > 0 && ids.every((id) => prev.has(id));
      const next = new Set(prev);
      if (allOn) ids.forEach((id) => next.delete(id));
      else ids.forEach((id) => next.add(id));
      return next;
    });
    anchorRef.current = null;
  }, [ids]);

  const clear = useCallback(() => { setSelected(new Set()); anchorRef.current = null; }, []);

  const keepOnly = useCallback((ids: string[]) => {
    const keep = new Set(ids);
    setSelected((prev) => new Set([...prev].filter((id) => keep.has(id))));
    anchorRef.current = null;
  }, []);

  const checkboxProps = useCallback((id: string) => ({
    checked: selected.has(id),
    onChange: () => toggle(id),
    onClick: (e: { shiftKey?: boolean; preventDefault: () => void }) => {
      // Shift-click extends the range; preventDefault stops the checkbox's own change
      // event from firing a second (plain) toggle on top of the range.
      if (e.shiftKey) { e.preventDefault(); toggle(id, { shiftKey: true }); }
    },
  }), [selected, toggle]);

  return {
    selectedIds, selectedCount, isSelected, toggle, toggleAll, clear, keepOnly,
    allSelected, someSelected, anySelected: selectedCount > 0,
    checkboxProps,
    allCheckboxProps: { checked: allSelected, indeterminate: someSelected, onChange: toggleAll },
  };
}
