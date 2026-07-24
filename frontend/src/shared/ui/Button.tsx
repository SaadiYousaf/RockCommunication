import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";
import { cn } from "./cn";
import { Spinner } from "./Spinner";

type Variant = "primary" | "secondary" | "ghost" | "danger" | "success" | "outline" | "accent";
/** Canonical button variant union — reuse instead of re-declaring. */
export type ButtonVariant = Variant;
type Size = "sm" | "md" | "lg" | "icon";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  loading?: boolean;
  leftIcon?: ReactNode;
  rightIcon?: ReactNode;
  fullWidth?: boolean;
}

/**
 * All variants share a common shape:
 *  - `transition-all` + a subtle scale on press for tactility
 *  - matching focus ring (brand by default; tone-tinted for danger/success/accent)
 *  - disabled fades opacity + removes shadow so it feels truly inert
 */
const variants: Record<Variant, string> = {
  primary:
    "text-white bg-gradient-to-b from-brand-500 to-brand-700 " +
    "hover:from-brand-500 hover:to-brand-800 hover:shadow-glow " +
    "active:from-brand-700 active:to-brand-800 " +
    "shadow-card focus-visible:ring-brand-500/40 " +
    "disabled:from-brand-300 disabled:to-brand-300 disabled:shadow-none disabled:opacity-70",
  secondary:
    "text-white bg-gradient-to-b from-ink-800 to-ink-950 " +
    "hover:from-ink-700 hover:to-ink-900 hover:shadow-pop " +
    "active:from-ink-900 active:to-ink-950 " +
    "shadow-card focus-visible:ring-ink-500/40 " +
    "disabled:from-ink-400 disabled:to-ink-400 disabled:shadow-none disabled:opacity-70",
  outline:
    "bg-white text-ink-800 border border-ink-200 shadow-sm " +
    "hover:bg-ink-50 hover:border-ink-300 hover:shadow-card " +
    "active:bg-ink-100 " +
    "focus-visible:ring-brand-500/40 " +
    "disabled:text-ink-400 disabled:bg-white disabled:opacity-70",
  ghost:
    "bg-transparent text-ink-700 " +
    "hover:bg-ink-100 active:bg-ink-200 " +
    "focus-visible:ring-brand-500/30 " +
    "disabled:text-ink-400 disabled:opacity-70",
  danger:
    "text-white bg-gradient-to-b from-rose-500 to-rose-700 " +
    "hover:from-rose-500 hover:to-rose-800 hover:shadow-[0_8px_24px_-8px_rgba(244,63,94,0.55)] " +
    "active:from-rose-700 active:to-rose-800 " +
    "shadow-card focus-visible:ring-rose-500/40 " +
    "disabled:from-rose-300 disabled:to-rose-300 disabled:shadow-none disabled:opacity-70",
  success:
    "text-white bg-gradient-to-b from-emerald-500 to-emerald-700 " +
    "hover:from-emerald-500 hover:to-emerald-800 hover:shadow-[0_8px_24px_-8px_rgba(16,185,129,0.55)] " +
    "active:from-emerald-700 active:to-emerald-800 " +
    "shadow-card focus-visible:ring-emerald-500/40 " +
    "disabled:from-emerald-300 disabled:to-emerald-300 disabled:shadow-none disabled:opacity-70",
  accent:
    "text-white bg-gradient-to-b from-accent-500 to-accent-700 " +
    "hover:from-accent-500 hover:to-accent-800 hover:shadow-glow-accent " +
    "active:from-accent-700 active:to-accent-800 " +
    "shadow-card focus-visible:ring-accent-500/40 " +
    "disabled:from-accent-300 disabled:to-accent-300 disabled:shadow-none disabled:opacity-70",
};

const sizes: Record<Size, string> = {
  sm:   "h-8 px-3 text-xs gap-1.5 rounded-md",
  md:   "h-9 px-3.5 text-sm gap-2 rounded-lg",
  lg:   "h-11 px-5 text-sm gap-2 rounded-xl tracking-tight",
  icon: "h-9 w-9 p-0 rounded-lg",
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = "primary", size = "md", loading, leftIcon, rightIcon, fullWidth, className, children, disabled, ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      disabled={disabled || loading}
      className={cn(
        "relative inline-flex items-center justify-center font-medium select-none whitespace-nowrap",
        // Smoother spring on press, slightly more responsive
        "transition-[transform,box-shadow,background-color,border-color,color,opacity] duration-150 ease-out-quint",
        "active:scale-[0.97] active:duration-75",
        "focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-offset-0",
        "disabled:cursor-not-allowed disabled:active:scale-100",
        variants[variant],
        sizes[size],
        fullWidth && "w-full",
        className,
      )}
      {...rest}
    >
      {loading ? <Spinner size={size === "lg" ? 18 : 14} /> : leftIcon}
      {size !== "icon" && <span className="leading-none">{children}</span>}
      {size === "icon" && !loading && children}
      {!loading && rightIcon}
    </button>
  );
});
