import { cn } from "./cn";

/**
 * Loading placeholder. Two variants:
 *   • default — solid muted block (use for short text lines, badges)
 *   • shimmer — animated gradient (use for hero blocks, cards)
 *
 * Both honour reduced-motion (CSS rule in index.css freezes animation).
 */
export function Skeleton({
  className,
  shimmer = true,
  rounded = "md",
}: {
  className?: string;
  shimmer?: boolean;
  rounded?: "sm" | "md" | "lg" | "xl" | "full";
}) {
  const radius = {
    sm:   "rounded-sm",
    md:   "rounded-md",
    lg:   "rounded-lg",
    xl:   "rounded-xl",
    full: "rounded-full",
  }[rounded];
  return (
    <div
      aria-hidden
      className={cn(
        radius,
        shimmer
          ? "bg-gradient-to-r from-ink-100 via-ink-50/80 to-ink-100 bg-[length:800px_100%] animate-shimmer"
          : "bg-ink-100",
        className,
      )}
    />
  );
}
