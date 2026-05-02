import { useCallback, useEffect, useState } from "react";

/**
 * Like `useState` but reads/writes a JSON-encoded value under a localStorage key.
 *
 * Used for tiny preferences that should survive a page refresh — e.g. sidebar
 * collapse state, last-viewed dashboard tab, etc.
 *
 * Falls back gracefully (in-memory only) if localStorage is unavailable
 * (private mode, server-side render, etc.).
 */
export function usePersistentState<T>(key: string, initial: T): [T, (v: T | ((prev: T) => T)) => void] {
  const read = (): T => {
    try {
      const raw = window.localStorage.getItem(key);
      return raw == null ? initial : (JSON.parse(raw) as T);
    } catch {
      return initial;
    }
  };

  const [value, setValue] = useState<T>(read);

  useEffect(() => {
    try {
      window.localStorage.setItem(key, JSON.stringify(value));
    } catch { /* storage full / unavailable — no-op */ }
  }, [key, value]);

  const set = useCallback((v: T | ((prev: T) => T)) => setValue(v), []);
  return [value, set];
}
