import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { UserSummary } from "../shared/api/types";

export interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  user: UserSummary | null;
  /** True once an admin has picked their working context (or "all") this session. Gates the picker. */
  contextChosen?: boolean;
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
    clearAuth(state) {
      state.accessToken = null;
      state.refreshToken = null;
      state.user = null;
      state.contextChosen = false;
      localStorage.removeItem("auth");
    },
  },
});

export const { setAuth, clearAuth } = authSlice.actions;
export default authSlice.reducer;
