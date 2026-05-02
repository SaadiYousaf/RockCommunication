import { cn } from "./cn";

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join("");
}

/**
 * Eight-color hash → background. Picks a deterministic color per name so the same
 * person always gets the same avatar tint without us storing it.
 */
function colorFor(seed: string) {
  const palette = [
    "from-brand-500 to-brand-700",
    "from-emerald-500 to-emerald-700",
    "from-amber-400 to-amber-600",
    "from-rose-500 to-rose-700",
    "from-sky-500 to-sky-700",
    "from-violet-500 to-violet-700",
    "from-fuchsia-500 to-fuchsia-700",
    "from-teal-500 to-teal-700",
  ];
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) >>> 0;
  return palette[h % palette.length];
}

const presenceColor: Record<string, string> = {
  online:  "bg-emerald-500",
  busy:    "bg-rose-500",
  away:    "bg-amber-500",
  offline: "bg-ink-400",
};

export function Avatar({
  name, size = 36, className, presence,
}: {
  name: string;
  size?: number;
  className?: string;
  /** Optional online/away/busy/offline indicator dot. */
  presence?: "online" | "busy" | "away" | "offline";
}) {
  const grad = colorFor(name || "?");
  const fontSize = Math.round(size * 0.38);
  const dotSize = Math.max(8, Math.round(size * 0.26));

  return (
    <div className={cn("relative shrink-0 inline-block", className)} style={{ width: size, height: size }}>
      <div
        className={cn(
          "rounded-full text-white font-semibold grid place-items-center w-full h-full",
          "bg-gradient-to-br shadow-sm ring-2 ring-white",
          grad,
        )}
        style={{ fontSize }}
        aria-label={name}
      >
        {initials(name || "?")}
      </div>
      {presence && (
        <span
          aria-label={`Presence: ${presence}`}
          className={cn(
            "absolute -bottom-0.5 -right-0.5 rounded-full ring-2 ring-white",
            presenceColor[presence] ?? "bg-ink-400",
          )}
          style={{ width: dotSize, height: dotSize }}
        />
      )}
    </div>
  );
}
