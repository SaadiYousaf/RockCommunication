import { useEffect, useRef, type InputHTMLAttributes } from "react";
import { cn } from "./cn";
import { Icon } from "./Icon";

export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "onChange"> {
  checked: boolean;
  /** Renders the "partial" dash — used by a select-all box when only some rows are selected. */
  indeterminate?: boolean;
  onChange?: (checked: boolean, e: React.ChangeEvent<HTMLInputElement>) => void;
  /** Forwarded so a row checkbox can honour shift-click range selection. */
  onClick?: (e: React.MouseEvent<HTMLInputElement>) => void;
  size?: number;
}

/**
 * Accessible, theme-consistent checkbox. The native input stays in the DOM (keyboard,
 * focus, form semantics) but is visually hidden; the adjacent box is the painted control.
 * Supports the `indeterminate` visual state that only exists imperatively on the DOM node.
 */
export function Checkbox({
  checked, indeterminate = false, onChange, onClick, size = 18, className, disabled, ...rest
}: CheckboxProps) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => { if (ref.current) ref.current.indeterminate = indeterminate && !checked; }, [indeterminate, checked]);

  const on = checked || indeterminate;
  return (
    <label className={cn("relative inline-flex shrink-0 items-center justify-center align-middle",
      disabled ? "cursor-not-allowed opacity-50" : "cursor-pointer", className)}
      style={{ width: size, height: size }}
      // Stop the row's own click (row-navigation) from firing when you tick the box.
      onClick={(e) => e.stopPropagation()}>
      <input
        ref={ref} type="checkbox" checked={checked} disabled={disabled}
        onChange={(e) => onChange?.(e.target.checked, e)}
        onClick={onClick}
        className="peer absolute inset-0 h-full w-full cursor-[inherit] opacity-0"
        {...rest}
      />
      <span aria-hidden
        className={cn(
          "grid h-full w-full place-items-center rounded-[6px] border transition-all duration-150",
          "peer-focus-visible:ring-2 peer-focus-visible:ring-brand-500/40 peer-focus-visible:ring-offset-1",
          on
            ? "border-brand-500 bg-brand-500 text-white shadow-sm"
            : "border-ink-300 bg-white peer-hover:border-brand-400",
        )}>
        {checked
          ? <Icon name="check" size={Math.round(size * 0.66)} className="animate-pop" />
          : indeterminate
            ? <span className="h-[2px] w-1/2 rounded-full bg-white" />
            : null}
      </span>
    </label>
  );
}
