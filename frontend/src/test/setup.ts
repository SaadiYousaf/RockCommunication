import "@testing-library/jest-dom/vitest";
import { afterEach, vi } from "vitest";
import { cleanup } from "@testing-library/react";

/**
 * Shared test setup.
 *
 * jsdom implements the DOM but not the browser APIs around it. Each stub below exists because a
 * component this app actually renders would otherwise throw on mount — which would make every test
 * fail for reasons that have nothing to do with the thing being tested.
 */

afterEach(() => cleanup());

// Used by the sidebar and several responsive components.
if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

// jsdom has neither; charts, virtualised lists and sticky headers all reach for them.
for (const name of ["ResizeObserver", "IntersectionObserver"] as const) {
  if (!(name in window)) {
    (window as unknown as Record<string, unknown>)[name] = class {
      observe() {}
      unobserve() {}
      disconnect() {}
      takeRecords() { return []; }
    };
  }
}

if (!window.scrollTo) window.scrollTo = () => {};
if (!Element.prototype.scrollIntoView) Element.prototype.scrollIntoView = () => {};

// The notification sound builds an AudioContext. jsdom has no audio stack at all.
if (!("AudioContext" in window)) {
  (window as unknown as Record<string, unknown>).AudioContext = class {
    state = "running";
    currentTime = 0;
    destination = {};
    createGain() { return { gain: { setValueAtTime() {}, exponentialRampToValueAtTime() {} }, connect() {} }; }
    createOscillator() { return { type: "", frequency: { setValueAtTime() {} }, connect() {}, start() {}, stop() {} }; }
    createBiquadFilter() { return { type: "", frequency: { setValueAtTime() {} }, connect() {} }; }
    resume() { return Promise.resolve(); }
    close() { return Promise.resolve(); }
  };
}

// Nothing in a test reaches the network.
//
// Answered with a real Response rather than a rejection: RTK Query treats a 503 as an ordinary
// failed request and renders its error state, which is what a page would do offline. A rejection
// instead surfaced as a torrent of unhandled-error noise that buried the actual test output.
vi.stubGlobal("fetch", vi.fn(async () =>
  new Response(JSON.stringify({ detail: "network disabled in tests" }), {
    status: 503,
    headers: { "content-type": "application/json" },
  }),
));
