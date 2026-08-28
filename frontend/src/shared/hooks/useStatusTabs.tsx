import { useMemo, useState } from "react";
import { STATUS_TABS } from "../constants/messages";

export type StatusTab = "active" | "inactive" | "all";

/**
 * Splits a list into Active / Disabled tabs.
 *
 * WHY: disabled records were interleaved with live ones in every admin list. On the Agencies screen
 * six of seven rows were inactive, so the one agency actually being worked was buried among tenants
 * nobody touches — and the same read applies to a deactivated user or a closed call centre. Disabled
 * records still matter (you re-enable them), they just aren't the working set.
 *
 * Defaults to Active for that reason. The counts sit on the tabs so nothing looks lost: an admin can
 * see at a glance that the missing rows are disabled, not gone.
 */
export function useStatusTabs<T>(rows: readonly T[], isActive: (row: T) => boolean) {
  const [tab, setTab] = useState<StatusTab>("active");

  const { active, inactive } = useMemo(() => {
    const a: T[] = [];
    const i: T[] = [];
    for (const r of rows) (isActive(r) ? a : i).push(r);
    return { active: a, inactive: i };
    // isActive is a predicate defined inline at each call site; depending on it would recompute
    // every render. The rows array is the real input.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows]);

  const visible = tab === "active" ? active : tab === "inactive" ? inactive : (rows as T[]);

  const items = [
    { value: "active" as const, label: STATUS_TABS.active, count: active.length },
    { value: "inactive" as const, label: STATUS_TABS.disabled, count: inactive.length },
    { value: "all" as const, label: STATUS_TABS.all, count: rows.length },
  ];

  return { tab, setTab, visible, items, activeCount: active.length, inactiveCount: inactive.length };
}
