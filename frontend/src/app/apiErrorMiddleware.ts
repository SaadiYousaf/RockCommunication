import { isRejectedWithValue, type Middleware } from "@reduxjs/toolkit";
import { emitToast } from "../shared/ui/Toast";
import { LOAD_ERROR } from "../shared/constants/messages";

/**
 * Raises a visible toast whenever an API call FAILS.
 *
 * WHY THIS EXISTS: RTK Query returns errors instead of throwing them, so a failed query left `data`
 * undefined and every list page rendered its `!data → <EmptyState "No results">` branch. A failure
 * was therefore indistinguishable from an empty table — a SuperAdmin hit a 403 on the Sales list and
 * was told "no sales found" while the dashboard showed real totals from the same rows. It read as
 * data loss.
 *
 * Inline <ErrorState> on each page is the better per-page answer, but this middleware is the
 * backstop that makes a SILENT failure impossible ANYWHERE — including on pages nobody has revisited
 * and on any page added later. One change, whole app.
 */

/** Statuses that are handled elsewhere and must not raise a toast. */
function isHandledElsewhere(status: unknown): boolean {
  // 401 → the baseQuery already refreshes the token or signs the user out. Toasting here would
  // flash a scary error during a perfectly routine silent token rotation.
  // 429 → the baseQuery retries it with backoff. It is throttling, not failure; by the time the
  //       user could read a toast the request has usually already succeeded.
  return status === 401 || status === 429;
}

function describe(status: unknown): { title: string; description: string } {
  if (status === 403) return { title: LOAD_ERROR.noAccessTitle, description: LOAD_ERROR.noAccessBody };
  if (status === 404) return { title: LOAD_ERROR.couldNotLoadGeneric, description: LOAD_ERROR.notFoundBody };
  if (status === "FETCH_ERROR" || status === "TIMEOUT_ERROR")
    return { title: LOAD_ERROR.couldNotLoadGeneric, description: LOAD_ERROR.offlineBody };
  if (typeof status === "number" && status >= 500)
    return { title: LOAD_ERROR.couldNotLoadGeneric, description: LOAD_ERROR.serverBody };
  return { title: LOAD_ERROR.couldNotLoadGeneric, description: LOAD_ERROR.genericBody };
}

/**
 * At most ONE error toast on screen at a time, whatever is failing.
 *
 * The first version of this keyed the cooldown by endpoint, which was wrong: a dashboard mounts a
 * dozen different queries, so when the server was throttling, twelve DIFFERENT keys each earned
 * their own toast and a stack of them covered the page. Nobody needs to be told twelve times that
 * the network is unhappy — they need to be told once, and to still be able to see the app.
 *
 * The window is deliberately long. A broken backend keeps failing for as long as it is broken; the
 * toast is a notification, not a running log.
 */
const QUIET_WINDOW_MS = 15_000;
let lastReportedAt = 0;

function shouldReport(now: number): boolean {
  if (now - lastReportedAt < QUIET_WINDOW_MS) return false;
  lastReportedAt = now;
  return true;
}

export const apiErrorMiddleware: Middleware = () => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    try {
      const status = (action.payload as { status?: unknown } | undefined)?.status;

      // A MUTATION failing is already reported by the component that fired it (every call site
      // toasts in its catch), so only report QUERY failures here — the silent ones.
      const isQuery = (action.meta as { arg?: { type?: string } } | undefined)?.arg?.type === "query";

      if (isQuery && !isHandledElsewhere(status) && shouldReport(Date.now())) {
        const { title, description } = describe(status);
        emitToast({ title, description, tone: "danger", duration: 6000 });
      }
    } catch {
      // Reporting an error must never itself break the dispatch chain.
    }
  }
  return next(action);
};
