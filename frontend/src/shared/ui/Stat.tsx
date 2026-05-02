import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { cn } from "./cn";
import { Icon } from "./Icon";

export type StatTone = "brand" | "accent" | "success" | "warning" | "danger" | "neutral";

const toneIcon: Record<StatTone, string> = {
  brand:   "bg-brand-50 text-brand-600",
  accent:  "bg-accent-50 text-accent-600",
  success: "bg-emerald-50 text-emerald-700",
  warning: "bg-amber-50 text-amber-700",
  danger:  "bg-rose-50 text-rose-700",
  neutral: "bg-ink-100 text-ink-600",
};

export function Stat({
  label, value, delta, trend, icon, hint, sparkline, tone = "brand", className, to, onClick,
}: {
  label: string;
  value: ReactNode;
  delta?: string;
  trend?: "up" | "down" | "flat";
  icon?: ReactNode;
  /** Small explanatory text under the big value. */
  hint?: ReactNode;
  /** Array of numbers rendered as a tiny inline area-chart. Domain is [min,max] of the values. */
  sparkline?: number[];
  /** Color tone for the icon tile + sparkline accent. Defaults to brand. */
  tone?: StatTone;
  className?: string;
  /** When provided, the whole tile becomes a Link to this route. */
  to?: string;
  /** When provided (and `to` is not), the whole tile becomes a button. */
  onClick?: () => void;
}) {
  const trendColor = trend === "up" ? "text-emerald-700" : trend === "down" ? "text-rose-700" : "text-ink-600";
  const trendBg   = trend === "up" ? "bg-emerald-50"  : trend === "down" ? "bg-rose-50"  : "bg-ink-100";

  const interactive = Boolean(to || onClick);
  const Wrapper: any = to ? Link : onClick ? "button" : "div";
  const wrapperProps: Record<string, unknown> = to
    ? { to }
    : onClick
      ? { type: "button", onClick }
      : {};

  return (
    <Wrapper
      {...wrapperProps}
      className={cn(
        "surface relative overflow-hidden p-5 sm:p-6 block text-left w-full",
        interactive && "cursor-pointer hover:shadow-card-hover hover:border-ink-300 transition-shadow focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400",
        className,
      )}
    >
      {/* Subtle noise on the surface for a tactile, hi-res look. */}
      <div aria-hidden className="absolute inset-0 bg-noise pointer-events-none opacity-50" />
      <div className="relative">
        <div className="flex items-start justify-between gap-3">
          <div className="section-title">{label}</div>
          {icon && (
            <div
              className={cn(
                "h-10 w-10 rounded-xl grid place-items-center shrink-0 ring-1 ring-inset ring-white/60",
                toneIcon[tone],
              )}
            >
              {icon}
            </div>
          )}
        </div>
        <div className="mt-3 flex items-baseline gap-3">
          <div className="text-[32px] sm:text-[34px] leading-none font-semibold tracking-tight text-ink-900 num">
            {value}
          </div>
          {delta && (
            <div className={cn(
              "inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs font-semibold",
              trendBg, trendColor,
            )}>
              {trend === "up" && <Icon name="trendUp" size={12} />}
              {trend === "down" && <Icon name="trendDown" size={12} />}
              {trend === "flat" && <span aria-hidden className="px-0.5">—</span>}
              <span>{delta}</span>
            </div>
          )}
        </div>
        {hint && <div className="mt-2 text-xs text-ink-500">{hint}</div>}
        {sparkline && sparkline.length > 1 && (
          <div className="mt-3">
            <Sparkline values={sparkline} tone={tone} />
          </div>
        )}
      </div>
    </Wrapper>
  );
}

function Sparkline({ values, tone }: { values: number[]; tone: StatTone }) {
  const w = 100;
  const h = 28;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;
  const step = w / (values.length - 1);
  const points = values.map((v, i) => `${i * step},${h - ((v - min) / range) * h}`);
  const path = `M ${points.join(" L ")}`;
  const area = `${path} L ${w},${h} L 0,${h} Z`;

  const stroke = {
    brand: "#1f7eff", accent: "#7c3aed", success: "#10b981",
    warning: "#f59e0b", danger: "#f43f5e", neutral: "#646d7e",
  }[tone];

  return (
    <svg viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" className="w-full h-7">
      <defs>
        <linearGradient id={`spark-${tone}`} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor={stroke} stopOpacity="0.25" />
          <stop offset="100%" stopColor={stroke} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={area} fill={`url(#spark-${tone})`} />
      <path d={path} stroke={stroke} strokeWidth="1.5" fill="none" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
