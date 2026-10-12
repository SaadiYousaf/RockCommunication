import { describe, expect, it, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { anAuthState, renderWithProviders } from "../../test/renderWithProviders";
import { SIGN_OUT_MSG } from "../constants/messages";

/**
 * A stand-in for the reflection endpoint, so these tests are about the dialog's behaviour rather
 * than about the network. `submitted` records what was sent; `shouldFail` makes the save reject.
 */
const submitted: unknown[] = [];
let shouldFail = false;

const submit = vi.fn((body: unknown) => {
  submitted.push(body);
  return { unwrap: () => (shouldFail ? Promise.reject(new Error("no")) : Promise.resolve({})) };
});

vi.mock("../../features/hr/reflectionsApi", () => ({
  useSubmitReflectionMutation: () => [submit, { isLoading: false }],
}));

const { SignOutProvider, useSignOut } = await import("./SignOutProvider");

/** A page with nothing on it but the sign-out trigger, the way the user menu offers it. */
function Harness() {
  const signOut = useSignOut();
  return <button onClick={signOut}>trigger</button>;
}

function render() {
  return renderWithProviders(
    <SignOutProvider><Harness /></SignOutProvider>,
    { auth: anAuthState() },
  );
}

beforeEach(() => {
  submitted.length = 0;
  shouldFail = false;
  submit.mockClear();
});

describe("the end-of-day dialog", () => {
  it("asks the two questions when somebody signs out", async () => {
    const user = userEvent.setup();
    render();

    await user.click(screen.getByText("trigger"));

    expect(screen.getByText(SIGN_OUT_MSG.learnedLabel)).toBeInTheDocument();
    expect(screen.getByText(SIGN_OUT_MSG.justiceLabel)).toBeInTheDocument();
  });

  /**
   * THE property this whole feature rests on. The questions are optional, so Skip has to end the
   * session on its own — without an answer, without a round trip, and without waiting for one.
   *
   * Note that it types an answer FIRST and then presses Skip. Skipping an empty form proves
   * nothing: with no answer to send, a Skip button wrongly wired to the submit handler behaves
   * identically, and an earlier version of this test passed with exactly that bug in place.
   * Changing your mind after writing something is the case that tells the two apart — and it is
   * also the one a real person hits, having started to type and then thought better of it.
   */
  it("sends nothing on Skip, even after an answer was typed", async () => {
    const user = userEvent.setup();
    const { store } = render();

    await user.click(screen.getByText("trigger"));
    await user.type(screen.getByPlaceholderText(SIGN_OUT_MSG.learnedPlaceholder), "Typed, then reconsidered.");
    await user.click(screen.getByText(SIGN_OUT_MSG.justiceYes));
    await user.click(screen.getByText(SIGN_OUT_MSG.skip));

    expect(submit).not.toHaveBeenCalled();
    expect(store.getState().auth.accessToken).toBeNull();
  });

  it("sends both answers and then signs out", async () => {
    const user = userEvent.setup();
    const { store } = render();

    await user.click(screen.getByText("trigger"));
    await user.type(screen.getByPlaceholderText(SIGN_OUT_MSG.learnedPlaceholder), "A new rebuttal.");
    await user.click(screen.getByText(SIGN_OUT_MSG.justiceNo));
    await user.click(screen.getByText(SIGN_OUT_MSG.submit));

    await waitFor(() => expect(store.getState().auth.accessToken).toBeNull());
    expect(submitted).toEqual([{ learned: "A new rebuttal.", didJustice: false, justiceNote: null }]);
  });

  /**
   * A failed save must still sign the person out. They asked to leave; holding them inside the
   * application because a form did not post would be the worst version of this feature.
   */
  it("still signs out when the answer cannot be saved", async () => {
    shouldFail = true;
    const user = userEvent.setup();
    const { store } = render();

    await user.click(screen.getByText("trigger"));
    await user.type(screen.getByPlaceholderText(SIGN_OUT_MSG.learnedPlaceholder), "Something.");
    await user.click(screen.getByText(SIGN_OUT_MSG.submit));

    await waitFor(() => expect(store.getState().auth.accessToken).toBeNull());
  });

  /**
   * Pressing Submit having typed nothing is the same intent as Skip. Sending an empty body would
   * be rejected by the server's validator and show the person an error for doing nothing wrong.
   */
  it("treats an empty submission as a skip", async () => {
    const user = userEvent.setup();
    const { store } = render();

    await user.click(screen.getByText("trigger"));
    await user.click(screen.getByText(SIGN_OUT_MSG.submit));

    expect(submit).not.toHaveBeenCalled();
    expect(store.getState().auth.accessToken).toBeNull();
  });

  /** An involuntary sign-out never raises this dialog — it only appears when asked for. */
  it("stays out of the way until somebody chooses to sign out", () => {
    render();
    expect(screen.queryByText(SIGN_OUT_MSG.learnedLabel)).not.toBeInTheDocument();
  });
});
