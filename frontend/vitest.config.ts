import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

/**
 * Test setup for the front end.
 *
 * Kept separate from vite.config.ts because that file stamps every asset filename with the build id
 * and splits vendor chunks — build concerns that have nothing to do with running a test, and that
 * would only be noise here.
 */
export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    // Only our own tests. Without this, a watch run walks node_modules.
    include: ["src/**/*.test.{ts,tsx}"],
    css: false,
  },
  define: {
    // The app reads this at runtime to tell whether a newer build has been deployed.
    __BUILD_ID__: JSON.stringify("test"),
  },
});
