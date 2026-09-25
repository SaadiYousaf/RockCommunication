import { useState } from "react";
import { cn } from "../ui/cn";
import { BRAND } from "../constants/brand";

/**
 * SMH Achievers Life Group brand logo.
 *
 * The mark is a shield carrying two ascending chevrons. The shield is the plain visual language of
 * this industry — what the company sells is protection — and the chevrons are the "Achievers" half:
 * a climb, with the smaller stroke reading as the ground already covered. The pair also resolves
 * into an implied "A" at a glance, which is what carries it at favicon size where fine detail is
 * gone entirely.
 *
 * Drawn in the app's own teal and ochre rather than a separate logo palette, so the mark belongs to
 * the product it sits inside instead of looking pasted on.
 *
 * Renders vector by default. If real artwork is dropped in later —
 *   - frontend/public/logo.png       (full lockup, login hero)
 *   - frontend/public/logo-mark.png  (square mark, sidebar)
 * — set `preferAsset`, and the component falls back to the vector if the file is missing, so a
 * broken path can never leave an alt-text placeholder in the sidebar.
 */
export function BrandLogo({
  variant = "mark",
  className,
  size,
  preferAsset = false,
}: {
  variant?: "mark" | "full";
  className?: string;
  /** Pixel size for the mark variant. Ignored for `full`. */
  size?: number;
  /** Try the PNG asset first, falling back to the vector. Off by default — the vector IS the logo. */
  preferAsset?: boolean;
}) {
  const [failed, setFailed] = useState(false);

  if (preferAsset && !failed) {
    const src = variant === "full" ? "/logo.png" : "/logo-mark.png";
    return (
      <img
        src={src}
        alt={BRAND.fullName}
        onError={() => setFailed(true)}
        {...(variant === "full"
          ? { className: cn("max-w-full h-auto select-none", className) }
          : { width: size ?? 36, height: size ?? 36, className: cn("select-none object-contain", className) })}
        draggable={false}
      />
    );
  }

  return variant === "full"
    ? <FullLogoSvg className={className} />
    : <MarkSvg size={size ?? 36} className={className} />;
}

/* ---------------- the mark ---------------- */

/**
 * Square shield mark.
 *
 * `idSuffix` exists because gradient ids are document-global: two of these on one page (sidebar and
 * a modal, say) would both define `smhTeal`, and every browser resolves the duplicate to whichever
 * came first. Usually harmless, occasionally the second mark renders with the first one's colours.
 */
export function MarkSvg({ size, className, idSuffix = "" }: { size: number; className?: string; idSuffix?: string }) {
  const teal = `smhTeal${idSuffix}`;
  const gold = `smhGold${idSuffix}`;
  const gloss = `smhGloss${idSuffix}`;

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 64 64"
      className={cn("shrink-0", className)}
      role="img"
      aria-label={BRAND.fullName}
    >
      <defs>
        <linearGradient id={teal} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#5d9b90" />
          <stop offset="48%" stopColor="#3c7269" />
          <stop offset="100%" stopColor="#1d3a35" />
        </linearGradient>
        <linearGradient id={gold} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#f0cd82" />
          <stop offset="55%" stopColor="#d9a943" />
          <stop offset="100%" stopColor="#b28535" />
        </linearGradient>
        {/* A soft sheen across the shield's upper half — stops the fill reading as flat plastic. */}
        <linearGradient id={gloss} x1="0" y1="0" x2="0.3" y2="1">
          <stop offset="0%" stopColor="#ffffff" stopOpacity="0.22" />
          <stop offset="60%" stopColor="#ffffff" stopOpacity="0" />
        </linearGradient>
      </defs>

      {/* Shield. Straight shoulders into a soft point, so it stays legible when scaled right down. */}
      <path
        d="M32 4.5 L55.5 12.4 V31.2 C55.5 43.9 45.2 53.6 32 59.5 C18.8 53.6 8.5 43.9 8.5 31.2 V12.4 Z"
        fill={`url(#${teal})`}
      />
      <path
        d="M32 4.5 L55.5 12.4 V31.2 C55.5 43.9 45.2 53.6 32 59.5 C18.8 53.6 8.5 43.9 8.5 31.2 V12.4 Z"
        fill={`url(#${gloss})`}
      />
      {/* Inner keyline — the detail that reads as "crest" rather than "clip-art shield". */}
      <path
        d="M32 9.2 L51.2 15.6 V30.9 C51.2 41.2 43 49.2 32 54.3 C21 49.2 12.8 41.2 12.8 30.9 V15.6 Z"
        fill="none"
        stroke="#ffffff"
        strokeOpacity="0.16"
        strokeWidth="1.1"
      />

      {/* The climb: the leading chevron in gold, the covered ground behind it in white. */}
      <path
        d="M20.5 32.5 L32 21.5 L43.5 32.5"
        fill="none"
        stroke={`url(#${gold})`}
        strokeWidth="6"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M22.5 43.5 L32 34.4 L41.5 43.5"
        fill="none"
        stroke="#ffffff"
        strokeOpacity="0.55"
        strokeWidth="4.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

/**
 * Full lockup: mark above the wordmark. Used on the login hero, which sits on a dark ground —
 * hence the light type.
 */
function FullLogoSvg({ className }: { className?: string }) {
  return (
    <div className={cn("flex flex-col items-center gap-4 text-white", className)}>
      <MarkSvg size={112} idSuffix="Full" />
      <div className="text-center leading-tight">
        <div className="text-[30px] font-extrabold tracking-[0.14em] bg-gradient-to-b from-white to-ink-300 bg-clip-text text-transparent">
          {BRAND.wordmarkLead}
        </div>
        <div className="text-[14px] font-bold tracking-[0.26em] text-accent-400 mt-1">
          {BRAND.wordmarkTail}
        </div>
        {/* Hairline rule, because the third line needs separating from the wordmark without a gap
            big enough to break the lockup apart. */}
        <div className="mx-auto mt-2.5 h-px w-12 bg-white/25" />
        <div className="text-[10px] uppercase tracking-[0.34em] text-ink-400 mt-2">
          {BRAND.tagline}
        </div>
      </div>
    </div>
  );
}
