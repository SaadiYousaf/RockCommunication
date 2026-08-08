/**
 * Client-side CSV export — turns an in-memory array of rows into a downloaded .csv file.
 * Used by the app-wide bulk "Export selected" action, so any list can export exactly the
 * rows a user ticked without needing a dedicated server endpoint.
 */

export interface CsvColumn<T> {
  /** Column header text. */
  header: string;
  /** Cell value for a row — anything; coerced to string and safely quoted. */
  value: (row: T) => string | number | null | undefined;
}

/** Escape a single field per RFC 4180: wrap in quotes when it contains a comma, quote, or newline. */
function escapeCell(input: string | number | null | undefined): string {
  const s = input == null ? "" : String(input);
  return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

/** Build a CSV string (with header row) from rows + column definitions. */
export function rowsToCsv<T>(rows: readonly T[], columns: readonly CsvColumn<T>[]): string {
  const head = columns.map((c) => escapeCell(c.header)).join(",");
  const body = rows.map((r) => columns.map((c) => escapeCell(c.value(r))).join(","));
  return [head, ...body].join("\r\n");
}

/** Trigger a browser download of `content` as a text file. Revokes the object URL afterwards. */
export function downloadTextFile(content: string, filename: string, mime = "text/csv;charset=utf-8"): void {
  // A BOM makes Excel read UTF-8 correctly (names with accents, ₨/€ symbols, etc.).
  const blob = new Blob(["﻿", content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/** Convenience: build CSV from rows/columns and download it in one call. */
export function exportRowsToCsv<T>(
  rows: readonly T[], columns: readonly CsvColumn<T>[], filename: string,
): void {
  downloadTextFile(rowsToCsv(rows, columns), filename);
}
