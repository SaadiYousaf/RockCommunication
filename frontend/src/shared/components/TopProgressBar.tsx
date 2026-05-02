import { useEffect, useRef, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";

/**
 * Slim animated progress bar at the top of the viewport, like GitHub / NProgress.
 *
 * Visibility is driven by RTK Query: if there's at least one query/mutation in flight,
 * the bar grows toward 90%. When all calls finish, it sprints to 100%, fades out, and resets.
 */
export function TopProgressBar() {
  const inFlight = useSelector((s: RootState) => {
    const api = (s as any).api;
    if (!api) return 0;
    let count = 0;
    // RTK Query exposes per-endpoint state; count subscriptions still pending.
    for (const q of Object.values(api.queries ?? {}) as any[]) {
      if (q?.status === "pending") count++;
    }
    for (const m of Object.values(api.mutations ?? {}) as any[]) {
      if (m?.status === "pending") count++;
    }
    return count;
  });

  const [pct, setPct] = useState(0);
  const [visible, setVisible] = useState(false);
  const tickRef = useRef<number | null>(null);

  useEffect(() => {
    if (inFlight > 0) {
      setVisible(true);
      // Crawl toward 90% while requests are pending.
      if (tickRef.current) window.clearInterval(tickRef.current);
      tickRef.current = window.setInterval(() => {
        setPct((p) => (p < 90 ? p + (90 - p) * 0.12 : p));
      }, 180);
    } else {
      // Finish + fade out.
      if (tickRef.current) { window.clearInterval(tickRef.current); tickRef.current = null; }
      setPct(100);
      const t = window.setTimeout(() => {
        setVisible(false);
        setPct(0);
      }, 280);
      return () => window.clearTimeout(t);
    }
    return () => {
      if (tickRef.current) window.clearInterval(tickRef.current);
    };
  }, [inFlight]);

  return (
    <div
      aria-hidden
      className="pointer-events-none fixed top-0 inset-x-0 z-[200] h-0.5 transition-opacity duration-200"
      style={{ opacity: visible ? 1 : 0 }}
    >
      <div
        className="h-full bg-gradient-to-r from-brand-400 via-brand-500 to-accent-500 shadow-[0_0_8px_rgba(31,126,255,0.65)]"
        style={{ width: `${pct}%`, transition: "width 200ms ease-out" }}
      />
    </div>
  );
}
