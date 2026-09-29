import { useEffect, useRef, useState } from "react";

/**
 * Notices when a new version of the app has been deployed, so a tab that has been open for hours
 * stops running code we fixed days ago.
 *
 * WHY THIS EXISTS: this is a single-page app that agents leave open across a whole shift. The
 * browser only fetches index.html — and therefore only learns about new asset URLs — when the page
 * is actually loaded. Nothing in a normal day causes that: clicking around the app is client-side
 * routing, not a page load. So a deploy would land, the server would serve the new build to anyone
 * who arrived fresh, and every agent already signed in would keep running the old one indefinitely.
 *
 * That is not a theoretical problem. A fix for the Closer "Add Lead" 403 shipped and was verified
 * live, and the people it was for kept hitting the 403 for two more days, because their tabs had
 * been open since before it deployed. Telling people to hard-refresh is not a fix: it relies on
 * every user being told, believing it, and doing it correctly.
 *
 * HOW: index.html is served `no-store`, and every asset filename carries the build id. Re-fetching
 * the document and reading the id back is therefore an exact, cache-proof answer to "what is the
 * current build?" — no extra endpoint to deploy, and nothing that can itself go stale.
 */

/** Stamped into the bundle at build time; "dev" locally. Mirrors vite.config.ts. */
export const BUILD_ID: string = __BUILD_ID__;

/** Quiet enough not to matter, frequent enough that a shift never runs a whole day behind. */
const POLL_MS = 3 * 60 * 1000;

/** Pulls the running build id out of the served index.html by reading an asset URL. */
async function fetchDeployedBuildId(signal: AbortSignal): Promise<string | null> {
  // `cache: "no-store"` as well as the server header: a proxy or an over-eager browser heuristic
  // would otherwise hand back the very document whose staleness we are trying to detect.
  const res = await fetch(`/index.html?_=${Date.now()}`, { cache: "no-store", signal });
  if (!res.ok) return null;

  const html = await res.text();
  // assets/index-<hash>.<buildId>.js — the build id is the segment before the extension.
  const match = html.match(/assets\/[A-Za-z0-9._-]+?\.([A-Za-z0-9]+)\.js/);
  return match?.[1] ?? null;
}

/**
 * True once the server is serving a build newer than the one running here.
 *
 * Never flips back to false: once a tab is known to be out of date it stays out of date until it
 * reloads, and a flapping banner would be worse than none.
 */
export function useNewVersionAvailable(): boolean {
  const [stale, setStale] = useState(false);
  // Held in a ref so the polling effect does not restart every time the flag changes.
  const staleRef = useRef(false);

  useEffect(() => {
    // Nothing to compare against on a dev build, and no deploys to notice either.
    if (BUILD_ID === "dev") return;

    const controller = new AbortController();
    let timer: number | undefined;

    const check = async () => {
      if (staleRef.current) return;
      try {
        const deployed = await fetchDeployedBuildId(controller.signal);
        if (deployed && deployed !== BUILD_ID) {
          staleRef.current = true;
          setStale(true);
        }
      } catch {
        // Offline, or the deploy is mid-flight. Not worth reporting — the next tick retries, and a
        // failed version check must never interrupt someone's work.
      }
    };

    // Coming back to a tab left open overnight is the single most likely moment to be behind, so
    // check then as well as on the timer.
    const onFocus = () => { if (document.visibilityState === "visible") void check(); };

    void check();
    timer = window.setInterval(() => void check(), POLL_MS);
    document.addEventListener("visibilitychange", onFocus);

    return () => {
      controller.abort();
      if (timer) window.clearInterval(timer);
      document.removeEventListener("visibilitychange", onFocus);
    };
  }, []);

  return stale;
}
