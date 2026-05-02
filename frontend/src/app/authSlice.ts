import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { UserSummary } from "../shared/api/types";

export interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  user: UserSummary | null;
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
      localStorage.setItem("auth", JSON.stringify(action.payload));
    },
    clearAuth(state) {
      state.accessToken = null;
      state.refreshToken = null;
      state.user = null;
      localStorage.removeItem("auth");
    },
  },
});

export const { setAuth, clearAuth } = authSlice.actions;
export default authSlice.reducer;
