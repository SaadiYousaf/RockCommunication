import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";

const API_URL = (import.meta as any).env?.VITE_API_URL ?? "http://localhost:5050";

type Handler = (event: string, payload: any) => void;

const EVENTS = [
  "incoming-call",
  "call-ringing",
  "call-answered",
  "call-ended",
  "call-state-changed",
  "screen-pop",
  "toast",
] as const;

/**
 * Subscribes to /hubs/agent for the signed-in user.
 *
 * Notes on resilience (these mattered after we saw the 401 storm):
 *  - We tear the connection down whenever the access token disappears, so a
 *    forced sign-out (token expired + refresh failed) doesn't leave SignalR
 *    auto-reconnecting forever with a dead token.
 *  - The token is read from the store on every reconnect via `accessTokenFactory`
 *    so token rotation after a silent /api/auth/refresh is honoured without
 *    rebuilding the connection.
 *  - First-connection failures are swallowed (so the UI stays usable when the
 *    API is offline) but we don't attempt automatic reconnects in that case —
 *    SignalR's reconnect loop only fires for *dropped* connections, not initial
 *    failures, which avoids the spam pattern we saw before.
 */
export function useAgentHub(handler: Handler) {
  const token = useSelector((s: RootState) => s.auth.accessToken);
  const handlerRef = useRef(handler);
  handlerRef.current = handler;
  // Live ref so SignalR's accessTokenFactory always reads the freshest value.
  const tokenRef = useRef<string | null>(token);
  tokenRef.current = token;

  useEffect(() => {
    if (!token) return; // unauthenticated → no hub

    const conn: HubConnection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/agent`, {
        accessTokenFactory: () => tokenRef.current ?? "",
      })
      .withAutomaticReconnect({
        // Cap retries; if we still can't reconnect after a few tries, give up
        // until something else (login, navigation) drops us back into this effect.
        nextRetryDelayInMilliseconds: (ctx) => {
          if (!tokenRef.current) return null; // signed out — stop reconnecting
          if (ctx.previousRetryCount >= 4) return null;
          return Math.min(1000 * 2 ** ctx.previousRetryCount, 15_000);
        },
      })
      .configureLogging(LogLevel.Warning)
      .build();

    EVENTS.forEach((ev) => {
      conn.on(ev, (payload: any) => handlerRef.current(ev, payload));
    });

    let cancelled = false;
    conn.start().catch(() => {
      // Initial connection failed (API offline, 401 from a stale token, etc.).
      // Don't loop — we'll reconnect when token / dependencies change.
    });

    return () => {
      cancelled = true;
      void cancelled;
      if (conn.state !== HubConnectionState.Disconnected) {
        conn.stop().catch(() => {});
      }
    };
  }, [token]);
}
