import { API_URL } from "../config";
import { useEffect, useRef, useState } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { useSelector } from "react-redux";
import { HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr";
import {
  useChatRoomsQuery, useChatUnreadQuery, useListUsersQuery,
} from "../api/baseApi";
import type { ChatMessage } from "../api/types";
import type { RootState } from "../../app/store";
import { Avatar, Badge, Button, Icon, Tooltip, useToast, cn } from "../ui";
import { useAgentHub } from "../hooks/useAgentHub";


/**
 * Header notifications bell — shows live unread count + a dropdown of rooms with unread.
 * Also subscribes to /hubs/chat globally so a toast appears whenever a message arrives
 * while the user is on a non-chat page.
 *
 * Mounted once in Layout, so it runs for the lifetime of the authenticated session.
 */
export function NotificationsBell() {
  const auth = useSelector((s: RootState) => s.auth);
  const { data: unread = [], refetch: refetchUnread } = useChatUnreadQuery(undefined, {
    skip: !auth.accessToken,
    pollingInterval: 30_000,
  });
  const { data: rooms } = useChatRoomsQuery(undefined, { skip: !auth.accessToken });
  const { data: users } = useListUsersQuery(undefined, { skip: !auth.accessToken });
  const navigate = useNavigate();
  const location = useLocation();
  const toast = useToast();

  // Live pipeline notifications (a lead/sale forwarded to this user's queue) → popup toast.
  useAgentHub((ev, payload) => {
    if (ev !== "notification") return;
    const url = payload.url;
    toast.show({
      title: payload.title ?? "New notification",
      description: payload.body,
      action: url ? { label: "Open", onClick: () => navigate(url) } : undefined,
    });
  });

  const totalUnread = unread.reduce((s, u) => s + (u.unreadCount || 0), 0);
  const unreadRooms = unread.filter((u) => u.unreadCount > 0);

  const [open, setOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement | null>(null);

  // Live refs so SignalR callbacks always read the freshest values without
  // rebuilding the connection.
  const tokenRef = useRef<string | null>(auth.accessToken);
  tokenRef.current = auth.accessToken;
  const pathRef = useRef(location.pathname);
  pathRef.current = location.pathname;
  const usersRef = useRef(users);
  usersRef.current = users;
  const roomsRef = useRef(rooms);
  roomsRef.current = rooms;

  // Track the live connection + readiness so we can join rooms when both the
  // connection AND the room list become available.
  const connRef = useRef<import("@microsoft/signalr").HubConnection | null>(null);
  const [hubReady, setHubReady] = useState(false);
  // Track which room IDs we've already joined so room-list updates only join new ones.
  const joinedRoomsRef = useRef(new Set<string>());

  // Click-outside close
  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [open]);

  // Global chat hub — toasts a notification whenever the user is NOT on the chat page.
  useEffect(() => {
    if (!auth.accessToken) return;

    const conn = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/chat`, { accessTokenFactory: () => tokenRef.current ?? "" })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => {
          if (!tokenRef.current) return null;
          if (ctx.previousRetryCount >= 4) return null;
          return Math.min(1000 * 2 ** ctx.previousRetryCount, 15_000);
        },
      })
      .build();

    conn.on("MessageReceived", (msg: ChatMessage) => {
      // Always refresh unread counts — the bell badge stays accurate.
      refetchUnread();

      // Skip toasts when the user is already in the chat UI (they'll see it inline).
      if (pathRef.current.startsWith("/chat")) return;
      // Don't toast our own messages echoed back.
      if (msg.senderUserId === auth.user?.id) return;

      const sender = (usersRef.current ?? []).find((u) => u.id === msg.senderUserId);
      const room = (roomsRef.current ?? []).find((r) => r.id === msg.roomId);
      const senderName = sender?.userName ?? "Someone";
      const roomName = room?.name ?? "a conversation";
      const preview = (msg.body ?? "").slice(0, 80);

      toast.show({
        tone: "info",
        title: `${senderName} · ${roomName}`,
        description: preview,
        duration: 6000,
        action: {
          label: "Open chat",
          onClick: () => navigate(`/chat?room=${msg.roomId}`),
        },
      });
    });

    // Re-join everything after a transparent reconnect.
    conn.onreconnected(() => {
      joinedRoomsRef.current.clear();
      setHubReady(true);
    });
    conn.onclose(() => setHubReady(false));

    conn.start()
      .then(() => { connRef.current = conn; setHubReady(true); })
      .catch(() => { /* offline / unauth — handled elsewhere */ });

    return () => {
      connRef.current = null;
      setHubReady(false);
      joinedRoomsRef.current.clear();
      if (conn.state !== HubConnectionState.Disconnected) conn.stop().catch(() => {});
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth.accessToken]);

  // Subscribe (JoinRoom) to every room the user belongs to once the connection
  // is up and the room list has loaded. Without this, the chat hub never
  // broadcasts MessageReceived to us → no toasts, no bell badge updates.
  useEffect(() => {
    if (!hubReady || !connRef.current || !rooms) return;
    const conn = connRef.current;
    for (const r of rooms) {
      if (joinedRoomsRef.current.has(r.id)) continue;
      conn.invoke("JoinRoom", r.id)
        .then(() => joinedRoomsRef.current.add(r.id))
        .catch(() => {});
    }
  }, [hubReady, rooms]);

  if (!auth.accessToken) return null;

  return (
    <div className="relative" ref={dropdownRef}>
      <Tooltip content={totalUnread > 0 ? `${totalUnread} unread message${totalUnread === 1 ? "" : "s"}` : "Notifications"}>
        <Button
          variant="ghost"
          size="icon"
          aria-label="Notifications"
          onClick={() => setOpen((o) => !o)}
        >
          <span className="relative inline-flex">
            <Icon name="bell" size={18} />
            {totalUnread > 0 && (
              <span
                className={cn(
                  "absolute -top-1.5 -right-1.5 min-w-[16px] h-4 px-1",
                  "rounded-full bg-rose-500 text-white text-[10px] font-bold",
                  "ring-2 ring-white grid place-items-center animate-pulse-ring",
                )}
              >
                {totalUnread > 99 ? "99+" : totalUnread}
              </span>
            )}
          </span>
        </Button>
      </Tooltip>

      {open && (
        <div className="absolute right-0 mt-2 w-80 surface-elevated overflow-hidden animate-scale-in z-40">
          <div className="px-4 py-3 border-b hairline bg-gradient-to-b from-brand-soft to-white">
            <div className="flex items-center justify-between">
              <div>
                <div className="text-sm font-semibold text-ink-900">Notifications</div>
                <div className="text-xs text-ink-500">
                  {totalUnread > 0
                    ? `${totalUnread} unread message${totalUnread === 1 ? "" : "s"}`
                    : "You're all caught up"}
                </div>
              </div>
              {totalUnread > 0 && (
                <Badge tone="danger" variant="solid">{totalUnread}</Badge>
              )}
            </div>
          </div>

          <div className="max-h-[60vh] overflow-y-auto">
            {unreadRooms.length === 0 ? (
              <div className="px-4 py-8 text-center text-sm text-ink-500">
                <Icon name="check" size={20} className="mx-auto mb-2 text-emerald-500" />
                No new messages.
              </div>
            ) : (
              unreadRooms.map((u) => {
                const room = (rooms ?? []).find((r) => r.id === u.roomId);
                const name = room?.name ?? "Conversation";
                return (
                  <button
                    key={u.roomId}
                    onClick={() => { setOpen(false); navigate(`/chat?room=${u.roomId}`); }}
                    className="w-full flex items-start gap-3 px-4 py-3 text-left hover:bg-ink-50 transition-colors border-b hairline last:border-b-0"
                  >
                    <Avatar name={name} size={32} />
                    <div className="flex-1 min-w-0">
                      <div className="text-sm font-medium text-ink-900 truncate">{name}</div>
                      <div className="text-xs text-ink-500">
                        {u.unreadCount} new message{u.unreadCount === 1 ? "" : "s"}
                      </div>
                    </div>
                    <Badge tone="brand" variant="solid">{u.unreadCount}</Badge>
                  </button>
                );
              })
            )}
          </div>

          <div className="border-t hairline px-3 py-2 flex items-center justify-between">
            <button
              onClick={() => { setOpen(false); refetchUnread(); }}
              className="text-xs text-ink-500 hover:text-ink-800 px-2 py-1 rounded transition-colors"
            >
              Refresh
            </button>
            <button
              onClick={() => { setOpen(false); navigate("/chat"); }}
              className="text-xs font-semibold text-brand-700 hover:text-brand-800 px-2 py-1 rounded transition-colors inline-flex items-center gap-1"
            >
              Open chat <Icon name="arrowRight" size={12} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
