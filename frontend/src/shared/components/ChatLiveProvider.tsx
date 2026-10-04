import {
  createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode,
} from "react";
import { useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from "@microsoft/signalr";
import type { RootState } from "../../app/store";
import { API_URL } from "../config";
import { useToast } from "../ui";
import { useChatRoomsQuery, useListUsersQuery } from "../api/baseApi";
import { useNotificationSound } from "../hooks/useNotificationSound";
import { CHAT_LIVE_MSG } from "../constants/messages";

/**
 * One chat connection for the whole application, held for as long as someone is signed in.
 *
 * It used to live inside the chat page, which meant it only existed while that page was open — so a
 * message reached you only if you were already looking at chat, and anywhere else in the CRM
 * nothing happened until a thirty-second poll noticed the unread count had moved. An agent working
 * a lead had no idea they had been messaged.
 *
 * Now the connection is mounted in the layout, the server puts it in every room the user belongs to
 * on connect, and a message raises a toast and a sound wherever they are. The chat page attaches its
 * own handlers to this same connection rather than opening a second one.
 */

interface ChatLive {
  /** The live connection, or null while it is down. */
  connection: HubConnection | null;
  state: "connecting" | "connected" | "disconnected";
  /**
   * The room the user is currently looking at, if any. Set by the chat page so an arriving message
   * in the conversation already on screen doesn't also announce itself.
   */
  setActiveRoom: (roomId: string | null) => void;
}

const ChatLiveCtx = createContext<ChatLive>({
  connection: null,
  state: "disconnected",
  setActiveRoom: () => {},
});

export const useChatLive = () => useContext(ChatLiveCtx);

interface IncomingMessage {
  id: string;
  roomId: string;
  senderUserId: string;
  body: string;
  attachmentName?: string | null;
}

/**
 * A notification from the operating system, for when the CRM is not the tab in front.
 *
 * Best-effort throughout: permission may never have been granted, the browser may refuse, or the
 * platform may not support it at all. Any of those simply means the message is still waiting in the
 * app — it must never surface as an error, and the sound has already played regardless.
 */
function showDesktopNotification(
  title: string, body: string, roomId: string, navigate: (to: string) => void,
): void {
  try {
    if (typeof Notification === "undefined" || Notification.permission !== "granted") return;

    const n = new Notification(title, {
      body,
      // One notification per conversation: ten messages from the same person replace each other
      // rather than stacking ten deep down the corner of the screen.
      tag: `chat-${roomId}`,
      renotify: true,
      icon: "/favicon.svg",
    } as NotificationOptions);

    n.onclick = () => {
      window.focus();
      navigate(`/chat?room=${roomId}`);
      n.close();
    };
  } catch { /* unsupported or blocked — the in-app badge still carries it */ }
}

export function ChatLiveProvider({ children }: { children: ReactNode }) {
  const auth = useSelector((s: RootState) => s.auth);
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const [state, setState] = useState<ChatLive["state"]>("disconnected");

  const navigate = useNavigate();
  const toast = useToast();
  const playAlert = useNotificationSound();

  // Names, so the alert says WHO and WHERE rather than just "New message". Both lists are already
  // fetched by the notifications bell, so RTK Query serves these from cache.
  const { data: rooms } = useChatRoomsQuery(undefined, { skip: !auth.accessToken });
  const { data: users } = useListUsersQuery(undefined, { skip: !auth.accessToken });
  const roomsRef = useRef(rooms);
  roomsRef.current = rooms;
  const usersRef = useRef(users);
  usersRef.current = users;

  // Refs, so the message handler never has to be torn down and re-registered as these change —
  // re-registering would mean dropping messages during the swap.
  const tokenRef = useRef(auth.accessToken);
  tokenRef.current = auth.accessToken;
  const myIdRef = useRef(auth.user?.id);
  myIdRef.current = auth.user?.id;
  const activeRoomRef = useRef<string | null>(null);
  const navigateRef = useRef(navigate);
  navigateRef.current = navigate;
  const toastRef = useRef(toast);
  toastRef.current = toast;
  const playRef = useRef(playAlert);
  playRef.current = playAlert;

  const setActiveRoom = useCallback((roomId: string | null) => {
    activeRoomRef.current = roomId;
  }, []);

  // During onboarding the server blocks everything but the setup endpoints, so connecting would
  // only produce a failing retry loop.
  const onboarding = !!(auth.user?.mustChangePassword || auth.user?.twoFactorSetupRequired);
  const enabled = !!auth.accessToken && !onboarding;

  // Ask once. If they decline, nothing here asks again — the sound and the in-app toast still work,
  // and nagging for a permission is worse than going without it.
  useEffect(() => {
    if (!enabled) return;
    try {
      if (typeof Notification !== "undefined" && Notification.permission === "default") {
        void Notification.requestPermission().catch(() => {});
      }
    } catch { /* not supported — nothing to do */ }
  }, [enabled]);

  useEffect(() => {
    if (!enabled) { setConnection(null); setState("disconnected"); return; }

    const conn = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/chat`, { accessTokenFactory: () => tokenRef.current ?? "" })
      .withAutomaticReconnect({
        // Retry for as long as they are signed in, backing off to 30s. Giving up would put us back
        // where we started: a silent chat that only a page reload can revive.
        nextRetryDelayInMilliseconds: (ctx) =>
          tokenRef.current ? Math.min(1000 * 2 ** Math.min(ctx.previousRetryCount, 5), 30_000) : null,
      })
      .build();

    conn.on("MessageReceived", (msg: IncomingMessage) => {
      // Never announce your own message back to you.
      if (!msg || msg.senderUserId === myIdRef.current) return;

      // Looking straight at the conversation, with the window focused? Then they have already seen
      // it land, and a toast over the top of it is just noise.
      const watching = activeRoomRef.current === msg.roomId && document.hasFocus();
      if (watching) return;

      playRef.current();

      const sender = (usersRef.current ?? []).find((u) => u.id === msg.senderUserId);
      const room = (roomsRef.current ?? []).find((r) => r.id === msg.roomId);
      const senderName = sender?.userName ?? CHAT_LIVE_MSG.someone;
      // A direct room is already named for the other person, so repeating it would read
      // "Zuhaib · Zuhaib". A group is worth naming, because the sender alone doesn't say where.
      const title = room && !room.isDirect ? `${senderName} · ${room.name}` : senderName;

      const preview = msg.body?.trim()
        ? (msg.body.length > 120 ? `${msg.body.slice(0, 120).trimEnd()}…` : msg.body)
        : msg.attachmentName
          ? CHAT_LIVE_MSG.sentAttachmentNamed(msg.attachmentName)
          : CHAT_LIVE_MSG.sentAttachment;

      // An in-app toast is invisible when the CRM isn't the tab in front. Agents work with the
      // carrier portal, a dialer and a spreadsheet open, so most of the day it isn't — which is
      // why messages were being missed entirely. When the page is hidden, ask the operating
      // system to show it instead; clicking it brings them straight to the conversation.
      if (document.visibilityState === "hidden") {
        showDesktopNotification(title, preview, msg.roomId, navigateRef.current);
      } else {
        toastRef.current.show({
          tone: "info",
          title,
          description: preview,
          duration: 8000,
          action: {
            label: CHAT_LIVE_MSG.open,
            onClick: () => navigateRef.current(`/chat?room=${msg.roomId}`),
          },
        });
      }
    });

    conn.onreconnecting(() => setState("connecting"));
    conn.onreconnected(() => setState("connected"));
    conn.onclose(() => setState("disconnected"));

    // Automatic reconnect only covers a connection that succeeded at least once, so a first attempt
    // that fails — every open tab during a deploy — needs its own retry.
    let cancelled = false;
    let timer: number | undefined;
    const start = (attempt = 0) => {
      if (cancelled) return;
      setState("connecting");
      conn.start()
        .then(() => {
          if (cancelled) return;
          setConnection(conn);
          setState("connected");
        })
        .catch(() => {
          if (cancelled || !tokenRef.current) { setState("disconnected"); return; }
          setState("disconnected");
          timer = window.setTimeout(() => start(attempt + 1), Math.min(1000 * 2 ** Math.min(attempt, 5), 30_000));
        });
    };
    start();

    return () => {
      cancelled = true;
      if (timer) window.clearTimeout(timer);
      setConnection(null);
      if (conn.state !== HubConnectionState.Disconnected) conn.stop().catch(() => {});
    };
  }, [enabled]);

  const value = useMemo<ChatLive>(
    () => ({ connection, state, setActiveRoom }),
    [connection, state, setActiveRoom],
  );

  return <ChatLiveCtx.Provider value={value}>{children}</ChatLiveCtx.Provider>;
}
