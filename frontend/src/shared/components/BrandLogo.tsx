import { useState } from "react";
import { cn } from "../ui/cn";

/**
 * Rock Communication brand logo.
 *
 * Always renders — falls back to an inline SVG if the PNG asset is missing,
 * so layouts never break with alt-text placeholders.
 *
 * To upgrade to the real artwork, drop:
 *   - frontend/public/logo.png       (full logo + wordmark, used on login hero)
 *   - frontend/public/logo-mark.png  (square mountain mark, used in sidebar)
 *
 * The component will pick those up automatically.
 */
export function BrandLogo({
  variant = "mark",
  className,
  size,
}: {
  variant?: "mark" | "full";
  className?: string;
  /** Pixel size for the mark variant. Ignored for `full`. */
  size?: number;
}) {
  const src = variant === "full" ? "/logo.png" : "/logo-mark.png";
  const [failed, setFailed] = useState(false);

  if (failed) {
    return variant === "full" ? (
      <FullLogoSvg className={className} />
    ) : (
      <MarkSvg size={size ?? 36} className={className} />
    );
  }

  if (variant === "full") {
    return (
      <img
        src={src}
        alt="Rock Communication Insurance Agency"
        onError={() => setFailed(true)}
        className={cn("max-w-full h-auto select-none", className)}
        draggable={false}
      />
    );
  }

  const px = size ?? 36;
  return (
    <img
      src={src}
      alt="Rock Communication"
      width={px}
      height={px}
      onError={() => setFailed(true)}
      className={cn("select-none object-contain", className)}
      draggable={false}
    />
  );
}

/* ---------------- inline-SVG fallbacks ---------------- */

/** Mountains-with-wave mark, square. Mirrors the silhouette of the supplied logo. */
function MarkSvg({ size, className }: { size: number; className?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 64 64"
      className={cn("shrink-0", className)}
      aria-label="Rock Communication"
    >
      <defs>
        <linearGradient id="rcSilver" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#f4f6fa" />
          <stop offset="55%" stopColor="#c8cfdb" />
          <stop offset="100%" stopColor="#7e8693" />
        </linearGradient>
        <linearGradient id="rcBlue" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#3e98ff" />
          <stop offset="60%" stopColor="#1f7eff" />
          <stop offset="100%" stopColor="#0a4eba" />
        </linearGradient>
      </defs>

      {/* back peak (blue) */}
      <path d="M3 46 L18 22 L33 46 Z" fill="url(#rcBlue)" />
      {/* main peak (silver) */}
      <path d="M14 46 L32 12 L50 46 Z" fill="url(#rcSilver)" />
      {/* right peak (blue) */}
      <path d="M40 46 L50 26 L61 46 Z" fill="url(#rcBlue)" />

      {/* highlight stroke on the main peak */}
      <path d="M32 12 L24 32" stroke="#ffffff" strokeOpacity="0.6" strokeWidth="1.2" fill="none" />

      {/* signature blue wave */}
      <path
        d="M2 48 C 14 54, 26 42, 32 46 C 38 50, 50 42, 62 48 L 62 52 C 50 46, 38 54, 32 50 C 26 46, 14 58, 2 52 Z"
        fill="url(#rcBlue)"
      />
    </svg>
  );
}

/** Full logo: mark on top, wordmark below. Used as the login hero. */
function FullLogoSvg({ className }: { className?: string }) {
  return (
    <div className={cn("flex flex-col items-center gap-3 text-white", className)}>
      <MarkSvg size={120} />
      <div className="text-center leading-tight">
        <div className="text-[28px] font-extrabold tracking-[0.06em] bg-gradient-to-b from-white to-ink-300 bg-clip-text text-transparent">
          ROCK
        </div>
        <div className="text-[15px] font-bold tracking-[0.18em] text-brand-400 mt-0.5">
          COMMUNICATION
        </div>
        <div className="text-[10px] uppercase tracking-[0.30em] text-ink-400 mt-1">
          Insurance Agency
        </div>
      </div>
    </div>
  );
}
