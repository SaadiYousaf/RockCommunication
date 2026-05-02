import { configureStore, createListenerMiddleware, isAnyOf } from "@reduxjs/toolkit";
import { baseApi } from "../shared/api/baseApi";
import authReducer, { setAuth, clearAuth } from "./authSlice";

export { setAuth, clearAuth } from "./authSlice";
export type { AuthState } from "./authSlice";

// When the active session changes (login, token refresh re-issuing a different
// user, or logout), wipe the RTK Query cache. Otherwise the next user keeps
// seeing the previous user's cached responses — including /api/permissions/mine,
// which would let the UI render write-buttons the new user isn't allowed to use.
const authListener = createListenerMiddleware();
authListener.startListening({
  matcher: isAnyOf(setAuth, clearAuth),
  effect: async (_action, api) => {
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
