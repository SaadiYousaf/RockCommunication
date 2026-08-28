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
  return status === 401;
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
 * Collapses repeats. A page mounting eight queries against a down server would otherwise stack eight
 * identical toasts over the UI; the user needs to be told once.
 */
const recent = new Map<string, number>();
const REPEAT_WINDOW_MS = 6000;

function shouldReport(key: string, now: number): boolean {
  const last = recent.get(key);
  if (last !== undefined && now - last < REPEAT_WINDOW_MS) return false;
  recent.set(key, now);
  // Keep the map from growing without bound over a long session.
  if (recent.size > 50) {
    for (const [k, t] of recent) if (now - t > REPEAT_WINDOW_MS) recent.delete(k);
  }
  return true;
}

export const apiErrorMiddleware: Middleware = () => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    try {
      const status = (action.payload as { status?: unknown } | undefined)?.status;

      // A MUTATION failing is already reported by the component that fired it (every call site
      // toasts in its catch), so only report QUERY failures here — the silent ones.
      const isQuery = (action.meta as { arg?: { type?: string } } | undefined)?.arg?.type === "query";

      if (isQuery && !isHandledElsewhere(status)) {
        const endpoint = (action.meta as { arg?: { endpointName?: string } } | undefined)?.arg?.endpointName ?? "?";
        if (shouldReport(`${endpoint}:${String(status)}`, Date.now())) {
          const { title, description } = describe(status);
          emitToast({ title, description, tone: "danger", duration: 7000 });
        }
      }
    } catch {
      // Reporting an error must never itself break the dispatch chain.
    }
  }
  return next(action);
};
