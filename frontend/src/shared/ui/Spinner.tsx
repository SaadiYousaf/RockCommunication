import { cn } from "./cn";

const sizeMap = { xs: 12, sm: 14, md: 16, lg: 20, xl: 28 } as const;

export function Spinner({
  size = "md",
  className,
}: {
  /** Either a token (xs..xl) or an explicit pixel size. */
  size?: keyof typeof sizeMap | number;
  className?: string;
}) {
  const px = typeof size === "number" ? size : sizeMap[size];
  return (
    <svg
      className={cn("animate-spin shrink-0", className)}
      width={px}
      height={px}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-label="Loading"
      role="status"
    >
      {/* Track */}
      <circle cx="12" cy="12" r="10" stroke="currentColor" strokeOpacity="0.18" strokeWidth="2.6" />
      {/* Sweep — the visible quarter-arc */}
      <path
        d="M22 12a10 10 0 0 1-10 10"
        stroke="currentColor"
        strokeWidth="2.6"
        strokeLinecap="round"
      />
    </svg>
  );
}
