/** US 10-digit phone → "(AAA) BBB-CCCC"; any other value returned unchanged; empty/nullish → "". */
export function formatPhone(raw: string | null | undefined): string {
  if (!raw) return "";
  const d = raw.replace(/\D/g, "");
  if (d.length === 10) return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
  return raw;
}

/** USD currency with cents, e.g. "$1,234.56"; nullish → "—". */
export function formatUsd(n: number | null | undefined): string {
  return n == null ? "—" : n.toLocaleString(undefined, { style: "currency", currency: "USD" });
}
