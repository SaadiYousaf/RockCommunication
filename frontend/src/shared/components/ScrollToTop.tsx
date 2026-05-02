import { useEffect } from "react";
import { useLocation } from "react-router-dom";

/**
 * Scrolls the main content area to the top whenever the pathname changes.
 *
 * The CRM has long pages (lead detail, KB articles, dashboards). Without this,
 * users navigating from page to page kept landing scrolled wherever the
 * previous page left off, which felt broken.
 *
 * We don't reset scroll on `?query` changes (e.g. switching tabs on
 * `/search?q=…`) — only on real path navigation.
 */
export function ScrollToTop() {
  const { pathname } = useLocation();
  useEffect(() => {
    // Find the scrollable main content; fall back to window.
    const main = document.querySelector("main");
    if (main) main.scrollTo({ top: 0, behavior: "instant" as ScrollBehavior });
    window.scrollTo({ top: 0, behavior: "instant" as ScrollBehavior });
  }, [pathname]);
  return null;
}
