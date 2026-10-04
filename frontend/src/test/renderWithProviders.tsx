import type { ReactNode } from "react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { MemoryRouter } from "react-router-dom";
import { render } from "@testing-library/react";
import authReducer, { setAuth, setNetworkBlocked, type AuthState } from "../app/authSlice";
import { baseApi } from "../shared/api/baseApi";
import { ToastProvider } from "../shared/ui";
import { ConfirmProvider } from "../shared/components/ConfirmDialog";
import type { UserSummary } from "../shared/api/types";

/**
 * Mounts a component the way the application does — inside a store, a router and the toast
 * provider — so a test exercises the real thing rather than a stripped-down copy of it.
 *
 * A fresh store per test, deliberately: the module-level store carries the RTK Query cache, and
 * sharing it would let one test's data decide another test's result.
 */

/** A signed-in user. Override only what the test is about; the rest is a plausible agent. */
export function aUser(overrides: Partial<UserSummary> = {}): UserSummary {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    userName: "testagent",
    email: "testagent@example.com",
    agencyId: "22222222-2222-2222-2222-222222222222",
    roles: ["Closer"],
    modules: [],
    ...overrides,
  } as UserSummary;
}

export function anAuthState(overrides: Partial<AuthState> = {}): AuthState {
  return {
    accessToken: "test-token",
    refreshToken: "test-refresh",
    user: aUser(),
    contextChosen: true,
    ...overrides,
  };
}

export function renderWithProviders(
  ui: ReactNode,
  { auth = anAuthState(), route = "/" }: { auth?: AuthState; route?: string } = {},
) {
  // Built empty and then filled by dispatching the app's OWN actions, rather than handing
  // configureStore a preloaded slice. The reducer key is computed from baseApi.reducerPath, which
  // defeats the inference preloadedState needs — and this way the state a test runs against is
  // state the real reducers produced, not a hand-written imitation of it.
  const store = configureStore({
    reducer: {
      auth: authReducer,
      [baseApi.reducerPath]: baseApi.reducer,
    },
    middleware: (getDefault) => getDefault().concat(baseApi.middleware),
  });

  if (auth.accessToken && auth.user) {
    store.dispatch(setAuth({
      accessToken: auth.accessToken,
      refreshToken: auth.refreshToken,
      user: auth.user,
      contextChosen: auth.contextChosen,
    }));
  }
  if (auth.networkBlockedAddress != null) {
    store.dispatch(setNetworkBlocked(auth.networkBlockedAddress));
  }

  return {
    store,
    ...render(
      <Provider store={store}>
        <MemoryRouter initialEntries={[route]}>
          {/* The same providers main.tsx wraps the app in. A page that reaches for one of these
              throws on mount, so leaving any of them out would fail tests for a reason that has
              nothing to do with the page. */}
          <ToastProvider>
            <ConfirmProvider>{ui}</ConfirmProvider>
          </ToastProvider>
        </MemoryRouter>
      </Provider>,
    ),
  };
}
