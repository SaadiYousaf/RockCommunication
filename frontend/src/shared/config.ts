/**
 * Runtime configuration derived from Vite env vars. Centralised so the API base URL
 * (and its dev fallback) lives in exactly one place instead of being re-derived,
 * with an `import.meta as any` cast, in every file that needs it.
 */
export const API_URL: string = import.meta.env.VITE_API_URL ?? "http://localhost:5050";
