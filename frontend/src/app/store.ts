import { configureStore, createListenerMiddleware, isAnyOf } from "@reduxjs/toolkit";
import { baseApi } from "../shared/api/baseApi";
import authReducer, { setAuth, clearAuth } from "./authSlice";

export { setAuth, clearAuth } from "./authSlice";
export type { AuthState } from "./authSlice";

// When the active IDENTITY changes (login, user-switch, or logout), wipe the RTK Query
// cache. Otherwise the next user keeps seeing the previous user's cached responses —
// including /api/permissions/mine, which would let the UI render write-buttons the new
// user isn't allowed to use.
const authListener = createListenerMiddleware();
authListener.startListening({
  matcher: isAnyOf(setAuth, clearAuth),
  effect: async (_action, api) => {
    const prev = (api.getOriginalState() as RootState).auth.user;
    const next = (api.getState() as RootState).auth.user;
    // A silent /api/auth/refresh re-dispatches setAuth for the SAME user+scope just to rotate
    // tokens — don't nuke the cache then (it drops every live query, e.g. the softphone's
    // active-call, into a blank/loading state and triggers a refetch stampede). Wipe on an actual
    // identity change OR a context switch (same user, different agency/call-center scope), so the
    // new scope's data replaces the old scope's stale cache.
    if (prev?.id === next?.id
        && prev?.agencyId === next?.agencyId
        && prev?.callCenterId === next?.callCenterId) return;
    api.dispatch(baseApi.util.resetApiState());
  },
});

export const store = configureStore({
  reducer: {
    auth: authReducer,
    [baseApi.reducerPath]: baseApi.reducer,
  },
  middleware: (getDefault) =>
    getDefault().prepend(authListener.middleware).concat(baseApi.middleware),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
