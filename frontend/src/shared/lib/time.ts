import type { BadgeTone } from "../ui";

/** Compact "how long ago" for queue waiting-time / relative timestamps: "now", "5m", "3h", "2d". */
export function timeAgoShort(iso: string): string {
  const s = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000));
  if (s < 60) return "now";
  const m = Math.floor(s / 60); if (m < 60) return `${m}m`;
  const h = Math.floor(m / 60); if (h < 24) return `${h}h`;
  return `${Math.floor(h / 24)}d`;
}

/** Relative "time ago" with suffix: "just now", "5m ago", "3h ago", "2d ago"; nullish → "never". */
export function timeAgo(iso: string | null | undefined): string {
  if (!iso) return "never";
  const s = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000));
  if (s < 60) return "just now";
  const m = Math.floor(s / 60); if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60); if (h < 24) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

/** Badge tone for a waiting-time chip — fresh (green) → aging (amber) → stale (red). */
export function waitTone(iso: string): BadgeTone {
  const hours = (Date.now() - new Date(iso).getTime()) / 3_600_000;
  return hours < 1 ? "success" : hours < 24 ? "warning" : "danger";
}
