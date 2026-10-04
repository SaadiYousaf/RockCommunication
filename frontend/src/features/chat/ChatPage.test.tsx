import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { anAuthState, aUser, renderWithProviders } from "../../test/renderWithProviders";

/**
 * A stand-in for the live chat connection.
 *
 * Without this the test is worthless for its own purpose: no network means the real connection
 * never establishes, the handler-attaching effect returns early, and the very line that crashed
 * production never runs. Verified by putting the bug back — the test passed anyway until this
 * stub existed.
 */
const fakeConnection = {
  on: vi.fn(),
  off: vi.fn(),
  invoke: vi.fn(() => Promise.resolve()),
  state: "Connected",
};

vi.mock("../../shared/components/ChatLiveProvider", () => ({
  useChatLive: () => ({
    connection: fakeConnection,
    state: "connected" as const,
    setActiveRoom: vi.fn(),
  }),
  ChatLiveProvider: ({ children }: { children: React.ReactNode }) => children,
}));

// Imported AFTER the mock, so the page picks up the stub rather than the real provider.
const { ChatPage } = await import("./ChatPage");

/**
 * That the chat page mounts at all.
 *
 * It did not, for a day. The messages query is skipped while no conversation is selected, and RTK
 * Query throws when you refetch a query it never started — so opening Chat with nothing picked,
 * which is how everyone arrives, threw during an effect and the error boundary replaced the page.
 *
 * No assertion about chat FEATURES here on purpose. The bug was not in any of them; it was that the
 * component could not be rendered. A test that simply mounts the page is the one that would have
 * caught it, and it is worth having for every page that people live in all day.
 */
describe("ChatPage", () => {
  it("mounts with no conversation selected", () => {
    renderWithProviders(<ChatPage />, { route: "/chat" });

    // The page got far enough to draw its own furniture rather than throwing on the way up.
    expect(screen.getByText(/conversations/i)).toBeInTheDocument();
  });

  it("mounts with a conversation in the url", () => {
    renderWithProviders(<ChatPage />, {
      route: "/chat?room=33333333-3333-3333-3333-333333333333",
    });
    expect(screen.getByText(/conversations/i)).toBeInTheDocument();
  });

  it("mounts for a user holding no roles at all", () => {
    renderWithProviders(<ChatPage />, {
      route: "/chat",
      auth: anAuthState({ user: aUser({ roles: [] }) }),
    });
    expect(screen.getByText(/conversations/i)).toBeInTheDocument();
  });
});
