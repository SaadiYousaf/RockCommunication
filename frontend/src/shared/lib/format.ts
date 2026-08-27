/** US 10-digit phone → "(AAA) BBB-CCCC"; any other value returned unchanged; empty/nullish → "". */
export function formatPhone(raw: string | null | undefined): string {
  if (!raw) return "";
  const d = raw.replace(/\D/g, "");
  if (d.length === 10) return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
  return raw;
}

/**
 * Money display settings for the signed-in user's agency. Sale/commission figures are STORED in
 * USD; an agency can choose to display them in another currency at a set rate. Kept at module level
 * (rather than threaded through every call site) and refreshed from the auth state on sign-in and
 * on any context switch — see the auth listener in app/store.ts.
 *
 * Payroll is PKR-native and must NEVER be run through this — it would double-convert.
 */
let displayCurrency = "USD";
let exchangeRate = 1;

export function setMoneyDisplay(currency: string | null | undefined, rate: number | null | undefined): void {
  displayCurrency = currency && currency.trim() ? currency.trim().toUpperCase() : "USD";
  // A zero/negative/absent rate would blank out or invert every figure — fall back to 1:1.
  exchangeRate = typeof rate === "number" && rate > 0 ? rate : 1;
}

export function getMoneyDisplay(): { currency: string; rate: number } {
  return { currency: displayCurrency, rate: exchangeRate };
}

/**
 * A USD-denominated figure rendered in the agency's display currency, e.g. "$1,234.56" or
 * "PKR 345,660.80"; nullish → "—". Converts at the configured rate (1 = unchanged).
 */
export function formatUsd(n: number | null | undefined): string {
  if (n == null) return "—";
  const converted = n * exchangeRate;
  try {
    return converted.toLocaleString(undefined, { style: "currency", currency: displayCurrency });
  } catch {
    // An unknown ISO code would throw — never let a settings typo break the page.
    return `${displayCurrency} ${converted.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }
}
