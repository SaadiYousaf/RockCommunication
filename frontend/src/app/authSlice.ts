import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { UserSummary } from "../shared/api/types";

export interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  user: UserSummary | null;
  /** True once an admin has picked their working context (or "all") this session. Gates the picker. */
  contextChosen?: boolean;
  /**
   * Set when the server refuses this network. Held here rather than shown as a toast because EVERY
   * request fails the same way — a toast per query would bury the one thing they need to read, and
   * the app behind it would be an empty shell. Not persisted: it describes where they are sitting
   * right now, so it must not survive into a session started somewhere else.
   */
  networkBlockedAddress?: string | null;
}

const persisted = (() => {
  try {
    const raw = localStorage.getItem("auth");
    return raw ? (JSON.parse(raw) as AuthState) : null;
  } catch { return null; }
})();

const initialState: AuthState = persisted ?? {
  accessToken: null, refreshToken: null, user: null,
};

const authSlice = createSlice({
  name: "auth",
  initialState,
  reducers: {
    setAuth(state, action: PayloadAction<AuthState>) {
      state.accessToken = action.payload.accessToken;
      state.refreshToken = action.payload.refreshToken;
      state.user = action.payload.user;
      // A silent token refresh spreads the current state (contextChosen preserved); login omits it
      // (falsy → picker shows); the context switch sets it true.
      state.contextChosen = action.payload.contextChosen ?? false;
      localStorage.setItem("auth", JSON.stringify({
        accessToken: state.accessToken, refreshToken: state.refreshToken,
        user: state.user, contextChosen: state.contextChosen,
      }));
    },
    /** The server refused this network. `null` clears it — used when a request succeeds again. */
    setNetworkBlocked(state, action: PayloadAction<string | null>) {
      state.networkBlockedAddress = action.payload;
    },
    clearAuth(state) {
      state.accessToken = null;
      state.refreshToken = null;
      state.user = null;
      state.contextChosen = false;
      state.networkBlockedAddress = null;
      localStorage.removeItem("auth");
    },
  },
});

export const { setAuth, clearAuth, setNetworkBlocked } = authSlice.actions;
export default authSlice.reducer;
