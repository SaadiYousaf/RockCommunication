import type { ReactNode } from "react";
import { EmptyState } from "./EmptyState";
import { Icon } from "./Icon";
import { Button } from "./Button";
import { getErrorStatus } from "../api/apiError";
import { LOAD_ERROR } from "../constants/messages";

/**
 * Shown when a list or panel FAILED to load.
 *
 * WHY THIS EXISTS: every list page used to render `!data → <EmptyState "No results">`, which made a
 * failed request look identical to a genuinely empty table. A SuperAdmin hit a 403 on the Sales list
 * and was told "no sales found" while the dashboard reported real totals off the same rows — it read
 * as data loss, not as a permissions problem. A request that failed must never be reported as "there
 * is nothing here".
 *
 * The message is derived from the HTTP status only. The server's raw message is deliberately NOT
 * surfaced: it leaks internals (role names, entity ids) that mean nothing to the person reading it.
 */
export function ErrorState({
  error, onRetry, resource, compact,
}: {
  error: unknown;
  /** Wire this to the query's `refetch` — a retry is the one action that usually works. */
  onRetry?: () => void;
  /** What failed to load, lowercase, for the message. e.g. "sales" → "Couldn't load sales". */
  resource?: string;
  compact?: boolean;
}) {
  const status = getErrorStatus(error);

  const { title, description } = describe(status, resource);

  return (
    <EmptyState
      tone="danger"
      compact={compact}
      icon={<Icon name="warning" size={20} />}
      title={title}
      description={description}
      action={onRetry ? (
        <Button variant="secondary" onClick={onRetry}>{LOAD_ERROR.retry}</Button>
      ) : undefined}
    />
  );
}

function describe(status: number | undefined, resource?: string): { title: string; description: string } {
  const what = resource ? `${LOAD_ERROR.couldNotLoad} ${resource}.` : LOAD_ERROR.couldNotLoadGeneric;

  // 403 is the one worth distinguishing: it is not a fault the user can retry away, and telling
  // them "try again" would send them in circles.
  if (status === 403) return { title: LOAD_ERROR.noAccessTitle, description: LOAD_ERROR.noAccessBody };
  if (status === 404) return { title: what, description: LOAD_ERROR.notFoundBody };
  if (status === 0 || status === undefined) return { title: what, description: LOAD_ERROR.offlineBody };
  if (status >= 500) return { title: what, description: LOAD_ERROR.serverBody };
  return { title: what, description: LOAD_ERROR.genericBody };
}

/**
 * Renders `children` only once a query has actually succeeded; otherwise shows the loading, error,
 * or empty state. Keeps the "failed ≠ empty" rule in ONE place instead of re-deciding it per page.
 */
export function QueryState({
  isLoading, isError, error, isEmpty, resource, onRetry, loading, empty, children,
}: {
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  isEmpty: boolean;
  resource?: string;
  onRetry?: () => void;
  loading: ReactNode;
  empty: ReactNode;
  children: ReactNode;
}) {
  if (isLoading) return <>{loading}</>;
  if (isError) return <ErrorState error={error} resource={resource} onRetry={onRetry} />;
  if (isEmpty) return <>{empty}</>;
  return <>{children}</>;
}
