import type { ReactNode, ThHTMLAttributes, TdHTMLAttributes, HTMLAttributes } from "react";
import { cn } from "./cn";
import { Icon } from "./Icon";

export interface TableProps extends HTMLAttributes<HTMLTableElement> {
  /** Compact density — useful for long admin tables. */
  dense?: boolean;
  /** Constrains height and applies a sticky header. Pair with dense for log views. */
  maxHeight?: string;
  /** Adds soft alternating zebra striping — easier to scan in long tables. */
  zebra?: boolean;
}

/**
 * Tabular layout primitive. Wraps the <table> in a `surface` so it inherits the
 * card chrome (border, radius, sheen) without the caller having to add a Card.
 *
 * Density / sticky / zebra are surfaced as data-attributes so child rows can
 * react via descendant selectors without prop drilling.
 */
export function Table({ className, children, dense, maxHeight, zebra, ...rest }: TableProps) {
  const containerStyle = maxHeight ? { maxHeight } : undefined;
  return (
    <div
      className={cn(
        "surface overflow-auto",
        maxHeight && "overflow-y-auto",
      )}
      style={containerStyle}
      data-dense={dense ? "true" : undefined}
      data-zebra={zebra ? "true" : undefined}
    >
      <table className={cn("w-full text-sm", className)} {...rest}>{children}</table>
    </div>
  );
}

export function THead({ children, sticky }: { children: ReactNode; sticky?: boolean }) {
  return (
    <thead
      className={cn(
        // Subtle gradient on the header row so it reads as "chrome" not "data"
        "bg-gradient-to-b from-ink-50/80 to-ink-50/40",
        "text-ink-600 text-[11px] uppercase tracking-[0.08em]",
        "border-b hairline",
        sticky && "thead-sticky",
      )}
    >
      {children}
    </thead>
  );
}

export interface ThProps extends ThHTMLAttributes<HTMLTableCellElement> {
  /** Render a sort affordance arrow next to the label. */
  sortDir?: "asc" | "desc" | null;
  /** Right-align numeric columns. */
  numeric?: boolean;
}

export function TH({ className, children, sortDir, numeric, ...rest }: ThProps) {
  return (
    <th
      className={cn(
        "font-semibold px-4 py-2.5 select-none whitespace-nowrap",
        numeric ? "text-right" : "text-left",
        sortDir !== undefined && "cursor-pointer hover:text-ink-800 transition-colors",
        className,
      )}
      {...rest}
    >
      <span className="inline-flex items-center gap-1.5 align-middle">
        {children}
        {sortDir === "asc" && <Icon name="chevronUp" size={12} className="text-brand-600" />}
        {sortDir === "desc" && <Icon name="chevronDown" size={12} className="text-brand-600" />}
      </span>
    </th>
  );
}

export function TBody({ children }: { children: ReactNode }) {
  return (
    <tbody
      className={cn(
        "divide-y divide-ink-100/80",
        // Zebra striping via descendant attribute selector
        "[[data-zebra=true]_&>tr:nth-child(even)>td]:bg-ink-50/40",
      )}
    >
      {children}
    </tbody>
  );
}

export function TR({ className, children, ...rest }: HTMLAttributes<HTMLTableRowElement>) {
  return (
    <tr
      className={cn(
        // Slow, subtle background fade on hover — feels deliberate, not jumpy
        "transition-colors duration-150 hover:bg-brand-50/40",
        className,
      )}
      {...rest}
    >
      {children}
    </tr>
  );
}

export interface TdProps extends TdHTMLAttributes<HTMLTableCellElement> {
  numeric?: boolean;
  muted?: boolean;
}

export function TD({ className, children, numeric, muted, ...rest }: TdProps) {
  return (
    <td
      className={cn(
        "px-4 py-3 align-middle",
        numeric ? "text-right num" : "text-left",
        muted ? "text-ink-500" : "text-ink-800",
        // Dense mode pulled in via the parent [data-dense] wrapper
        "[[data-dense=true]_&]:py-1.5 [[data-dense=true]_&]:px-3",
        className,
      )}
      {...rest}
    >
      {children}
    </td>
  );
}
