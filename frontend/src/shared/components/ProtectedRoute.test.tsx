import { describe, expect, it } from "vitest";
import { Route, Routes } from "react-router-dom";
import { screen } from "@testing-library/react";
import { ProtectedRoute } from "./ProtectedRoute";
import { anAuthState, aUser, renderWithProviders } from "../../test/renderWithProviders";

/**
 * The route guard.
 *
 * Every bug this file covers reached a live call floor, and none of them could have been caught by
 * a backend test: the API was right each time and the browser refused to let anyone reach it. The
 * guard decides who sees what, and it is the single highest-traffic piece of logic in the client.
 */

/** Renders the guard around a page, with a landmark for each outcome a test needs to tell apart. */
function renderGuard(
  { roles, modules, route = "/target", auth = anAuthState() }:
  { roles?: string[]; modules?: string[]; route?: string; auth?: ReturnType<typeof anAuthState> },
) {
  return renderWithProviders(
    <Routes>
      <Route element={<ProtectedRoute roles={roles} modules={modules} />}>
        <Route path="/target" element={<div>the page</div>} />
        <Route path="/change-password" element={<div>change password</div>} />
        <Route path="/2fa" element={<div>two factor</div>} />
        <Route path="/select-context" element={<div>pick a context</div>} />
      </Route>
      <Route path="/login" element={<div>sign in</div>} />
    </Routes>,
    { auth, route },
  );
}

describe("ProtectedRoute", () => {
  it("lets a user with a matching role through", () => {
    renderGuard({ roles: ["Fronter", "Closer"] });
    expect(screen.getByText("the page")).toBeInTheDocument();
  });

  /**
   * The Add Lead bug. The sidebar offered the link to Closers while the route allowed only
   * Fronters, so every Closer who clicked it filled in a form and was refused — three times, over
   * three days, in front of the client.
   */
  it("lets a Closer reach a page shared with Fronters", () => {
    renderGuard({ roles: ["Fronter", "Closer"], auth: anAuthState({ user: aUser({ roles: ["Closer"] }) }) });
    expect(screen.getByText("the page")).toBeInTheDocument();
  });

  it("refuses a user whose role is not listed", () => {
    renderGuard({ roles: ["Validator"], auth: anAuthState({ user: aUser({ roles: ["Closer"] }) }) });
    expect(screen.queryByText("the page")).not.toBeInTheDocument();
    expect(screen.getByText(/don't have access/i)).toBeInTheDocument();
  });

  it("sends a signed-out visitor to sign in", () => {
    renderGuard({ auth: anAuthState({ accessToken: null, user: null }) });
    expect(screen.getByText("sign in")).toBeInTheDocument();
  });

  it("lets an Admin past a role gate they are not named in", () => {
    renderGuard({ roles: ["Validator"], auth: anAuthState({ user: aUser({ roles: ["Admin"] }) }) });
    expect(screen.getByText("the page")).toBeInTheDocument();
  });

  it("accepts a module grant in place of a role", () => {
    renderGuard({
      modules: ["queue"],
      auth: anAuthState({ user: aUser({ roles: ["Fronter"], modules: ["queue"] }) }),
    });
    expect(screen.getByText("the page")).toBeInTheDocument();
  });

  describe("onboarding", () => {
    it("holds a user at the password screen until they set one", () => {
      renderGuard({ auth: anAuthState({ user: aUser({ mustChangePassword: true }) }) });
      expect(screen.getByText("change password")).toBeInTheDocument();
    });

    /**
     * The blank-page bug. An invited SuperAdmin sat at /change-password with a password change
     * pending; the context picker sent them to /select-context, the password gate sent them
     * straight back, and the two bounced forever — which renders as a WHITE PAGE, not an error.
     * The product's first invited SuperAdmin hit it on their first ever sign-in.
     */
    it("does not bounce an invited SuperAdmin between the password and context screens", () => {
      renderGuard({
        route: "/change-password",
        auth: anAuthState({
          contextChosen: false,
          user: aUser({ roles: ["SuperAdmin"], mustChangePassword: true, twoFactorSetupRequired: true }),
        }),
      });
      expect(screen.getByText("change password")).toBeInTheDocument();
      expect(screen.queryByText("pick a context")).not.toBeInTheDocument();
    });

    it("asks for 2FA once the password is set, and not before", () => {
      renderGuard({
        auth: anAuthState({ user: aUser({ mustChangePassword: false, twoFactorSetupRequired: true }) }),
      });
      expect(screen.getByText("two factor")).toBeInTheDocument();
    });

    it("leaves the context picker alone while 2FA enrolment is still pending", () => {
      renderGuard({
        route: "/2fa",
        auth: anAuthState({
          contextChosen: false,
          user: aUser({ roles: ["Admin"], twoFactorSetupRequired: true }),
        }),
      });
      expect(screen.getByText("two factor")).toBeInTheDocument();
      expect(screen.queryByText("pick a context")).not.toBeInTheDocument();
    });

    it("asks an admin to pick a context once onboarding is done", () => {
      renderGuard({
        auth: anAuthState({ contextChosen: false, user: aUser({ roles: ["Admin"] }) }),
      });
      expect(screen.getByText("pick a context")).toBeInTheDocument();
    });
  });

  it("shows the blocked-network screen instead of the page", () => {
    renderGuard({ auth: anAuthState({ networkBlockedAddress: "203.0.113.50" }) });
    expect(screen.queryByText("the page")).not.toBeInTheDocument();
    expect(screen.getByText("203.0.113.50")).toBeInTheDocument();
  });
});
