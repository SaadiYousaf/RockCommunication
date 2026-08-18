import { useState } from "react";
import { ALL_ROLES, roleLabel } from "../constants/roles";
import { Button, Icon } from "../ui";

/**
 * A toggle grid of every role, with Save / Cancel. Shared between User Management and the profile's
 * "Manage access" card so role editing looks and behaves identically everywhere. The backend still
 * enforces the rank guard (you can't grant a role above your own), so an over-grant is rejected there.
 */
export function RolePicker({
  initial, onSave, onCancel, saving,
}: { initial: string[]; onSave: (roles: string[]) => void; onCancel: () => void; saving?: boolean }) {
  const [picked, setPicked] = useState(new Set(initial));
  const toggle = (r: string) =>
    setPicked((prev) => {
      const next = new Set(prev);
      next.has(r) ? next.delete(r) : next.add(r);
      return next;
    });

  return (
    <>
      <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
        {ALL_ROLES.map((r) => {
          const active = picked.has(r);
          return (
            <button
              key={r} type="button" onClick={() => toggle(r)}
              className={
                "flex items-center gap-2 px-3 py-2 rounded-lg border text-sm transition-all focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40 " +
                (active
                  ? "border-brand-500 bg-brand-50 text-brand-700"
                  : "border-ink-200 hover:border-ink-300 hover:bg-ink-50/60 text-ink-700")
              }
            >
              <span className={"h-4 w-4 rounded border grid place-items-center " +
                (active ? "border-brand-600 bg-brand-600 text-white" : "border-ink-300 bg-white")}>
                {active && <Icon name="check" size={12} />}
              </span>
              <span className="truncate">{roleLabel(r)}</span>
            </button>
          );
        })}
      </div>
      <div className="flex justify-end gap-2 mt-5">
        <Button variant="ghost" onClick={onCancel}>Cancel</Button>
        <Button loading={saving} onClick={() => onSave(Array.from(picked))}>
          Save {picked.size > 0 ? `(${picked.size})` : ""}
        </Button>
      </div>
    </>
  );
}
