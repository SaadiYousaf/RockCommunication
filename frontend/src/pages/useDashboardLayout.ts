import { useCallback, useMemo } from "react";
import { usePersistentState } from "../shared/hooks/usePersistentState";

/**
 * Persistent, self-healing layout state for the customizable landing dashboard.
 *
 * Stores only `{ order, hidden }` (widget ids) under a single localStorage key and
 * reconciles it against the live widget registry on every read, so the saved value
 * can never crash or drift out of sync with the code:
 *   - unknown / stale ids (widgets that no longer exist) are dropped,
 *   - brand-new widget ids are appended in their default position,
 *   - a malformed / legacy shape falls back to the default order, all shown.
 *
 * All mutators reconcile `prev` before applying, so what we persist is always a
 * complete, valid layout after the first interaction.
 */
export interface DashboardLayout {
  order: string[];
  hidden: string[];
}

const STORAGE_KEY = "dashboard.layout";

/** Merge a possibly-stale saved layout with the current default order. */
function reconcile(raw: unknown, defaultOrder: readonly string[]): DashboardLayout {
  const valid = new Set(defaultOrder);
  const obj = (raw && typeof raw === "object" ? raw : {}) as Partial<DashboardLayout>;

  const savedOrder = Array.isArray(obj.order) ? obj.order.filter((id) => valid.has(id)) : [];
  const seen = new Set(savedOrder);
  const order = [...savedOrder];
  // Append any registry widget not already present (new widgets appear automatically).
  for (const id of defaultOrder) {
    if (!seen.has(id)) {
      order.push(id);
      seen.add(id);
    }
  }

  const hidden = Array.isArray(obj.hidden)
    ? obj.hidden.filter((id) => valid.has(id) && order.includes(id))
    : [];

  return { order, hidden };
}

export interface DashboardLayoutApi {
  /** Full, reconciled widget order (includes hidden widgets). */
  order: string[];
  /** Ids that are currently hidden. */
  hidden: Set<string>;
  /** Order minus hidden — what the dashboard grid actually renders. */
  visibleOrder: string[];
  isHidden: (id: string) => boolean;
  /** Show ⇄ hide a single widget. */
  toggle: (id: string) => void;
  /** Move a widget one slot up (-1) or down (+1) within the full order. */
  move: (id: string, dir: -1 | 1) => void;
  /** Drag-and-drop: move `fromId` to the slot currently held by `toId`. */
  reorder: (fromId: string, toId: string) => void;
  /** Restore the seeded default (default order, nothing hidden). */
  reset: () => void;
}

export function useDashboardLayout(defaultOrder: readonly string[]): DashboardLayoutApi {
  const [raw, setRaw] = usePersistentState<DashboardLayout>(STORAGE_KEY, {
    order: [...defaultOrder],
    hidden: [],
  });

  const layout = useMemo(() => reconcile(raw, defaultOrder), [raw, defaultOrder]);

  // Every mutator reconciles `prev` first, so a legacy/partial stored shape can't leak through.
  const mutate = useCallback(
    (fn: (l: DashboardLayout) => DashboardLayout) => {
      setRaw((prev) => fn(reconcile(prev, defaultOrder)));
    },
    [setRaw, defaultOrder],
  );

  const toggle = useCallback(
    (id: string) =>
      mutate((l) => ({
        order: l.order,
        hidden: l.hidden.includes(id) ? l.hidden.filter((x) => x !== id) : [...l.hidden, id],
      })),
    [mutate],
  );

  const move = useCallback(
    (id: string, dir: -1 | 1) =>
      mutate((l) => {
        const from = l.order.indexOf(id);
        const to = from + dir;
        if (from < 0 || to < 0 || to >= l.order.length) return l;
        const order = [...l.order];
        [order[from], order[to]] = [order[to], order[from]];
        return { order, hidden: l.hidden };
      }),
    [mutate],
  );

  const reorder = useCallback(
    (fromId: string, toId: string) =>
      mutate((l) => {
        const from = l.order.indexOf(fromId);
        const to = l.order.indexOf(toId);
        if (from < 0 || to < 0 || from === to) return l;
        const order = [...l.order];
        order.splice(from, 1);
        order.splice(to, 0, fromId);
        return { order, hidden: l.hidden };
      }),
    [mutate],
  );

  const reset = useCallback(
    () => setRaw({ order: [...defaultOrder], hidden: [] }),
    [setRaw, defaultOrder],
  );

  const hidden = useMemo(() => new Set(layout.hidden), [layout.hidden]);
  const visibleOrder = useMemo(
    () => layout.order.filter((id) => !hidden.has(id)),
    [layout.order, hidden],
  );

  return {
    order: layout.order,
    hidden,
    visibleOrder,
    isHidden: useCallback((id: string) => hidden.has(id), [hidden]),
    toggle,
    move,
    reorder,
    reset,
  };
}
