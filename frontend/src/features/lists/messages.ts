/**
 * Centralized, user-facing copy for the Lead Lists feature (list management + CSV import).
 * One home for every toast / confirm / empty-state sentence so the same wording isn't duplicated.
 * Never surface internal identifiers here — speak in plain language.
 */
export const LISTS_MSG = {
  /** What failed to load, for the shared ErrorState ("Couldn't load lead lists"). */
  resourceName: "lead lists",

  // Search placeholders (no user-facing copy inline).
  searchPlaceholder: "Search lists by name…",
  // ---- Export ----
  exportReadyTitle: "Export ready",
  exportRows: (rows: number) => `${rows} rows downloaded.`,

  // ---- Shared fallbacks ----
  retry: "Try again.",

  // ---- Create list ----
  listCreatedTitle: "List created",
  createFailedTitle: "Couldn't create list",

  // ---- Enable / disable ----
  disableConfirmTitle: (name: string) => `Disable "${name}"?`,
  disableConfirmDesc:
    "Disabling hides this list from new campaign setup and stops it being used until you turn it back on. Nothing is deleted.",
  disableConfirmLabel: "Disable",
  listDisabled: "List disabled",
  listEnabled: "List enabled",
  updateFailedTitle: "Couldn't update",

  // ---- CSV import ----
  noFileTitle: "No file selected",
  noFileDesc: "Pick a CSV first.",
  importFinishedTitle: "Import finished",
  importSummary: (imported: number, dups: number, scrubbed: number) =>
    `${imported} imported · ${dups} dups · ${scrubbed} scrubbed`,
  importFailedTitle: "Import failed",

  // ---- Empty states ----
  listsEmptyMatchTitle: "No lists match",
  listsEmptyTitle: "No lead lists yet",
  listsEmptyMatchDesc: "Try a different search.",
  listsEmptyDesc: "Create a list to organize leads and import contacts in bulk.",
  noImportsTitle: "No imports yet",
  noImportsDesc: "Once you upload a CSV, the run summary will appear here.",
} as const;
