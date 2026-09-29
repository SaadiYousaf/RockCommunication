/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

interface Window {
  /** Safari legacy prefix for AudioContext. */
  webkitAudioContext?: typeof AudioContext;
}

/** Build id injected by vite.config.ts — the commit SHA in CI, "dev" locally. */
declare const __BUILD_ID__: string;
