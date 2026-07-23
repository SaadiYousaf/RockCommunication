import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { SerializedError } from "@reduxjs/toolkit";

/** Problem-details body the API returns on errors (ASP.NET ProblemDetails subset). */
export interface ApiErrorBody {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

type UnknownError = FetchBaseQueryError | SerializedError | { data?: unknown; message?: string } | undefined | null;

function bodyOf(err: unknown): ApiErrorBody | undefined {
  if (err && typeof err === "object" && "data" in err) {
    const data = (err as { data?: unknown }).data;
    if (data && typeof data === "object") return data as ApiErrorBody;
  }
  return undefined;
}

/**
 * Extracts a human-readable message from any error thrown by an RTK Query
 * `unwrap()` (or a thrown value), preferring the API's `detail`, then `title`,
 * then a serialized `message`. Replaces the ubiquitous `err?.data?.detail` on
 * `catch (err: any)` with a typed, single-source helper.
 */
export function getErrorDetail(err: unknown, fallback?: string): string | undefined {
  const body = bodyOf(err);
  if (body?.detail) return body.detail;
  if (body?.title) return body.title;
  const msg = (err as { message?: string })?.message;
  if (typeof msg === "string" && msg) return msg;
  return fallback;
}

/** HTTP status code of an RTK Query / fetch error, if present. */
export function getErrorStatus(err: unknown): number | undefined {
  const status = (err as { status?: unknown })?.status ?? bodyOf(err)?.status;
  return typeof status === "number" ? status : undefined;
}

/** Field-level validation errors (from FluentValidation), flattened to a single string. */
export function getValidationSummary(err: UnknownError): string | undefined {
  const errors = bodyOf(err)?.errors;
  if (!errors) return undefined;
  const parts = Object.values(errors).flat();
  return parts.length ? parts.join(" ") : undefined;
}
