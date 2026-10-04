import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { useSelector } from "react-redux";
import {
  useChatRoomsQuery, useChatUnreadQuery, useListUsersQuery,
  useNotificationsQuery, useNotificationsUnreadCountQuery,
  useMarkNotificationReadMutation, useMarkAllNotificationsReadMutation,
} from "../api/baseApi";
import type { RootState } from "../../app/store";
import { Avatar, Badge, Button, Icon, Tooltip, useToast, cn } from "../ui";
import { useAgentHub } from "../hooks/useAgentHub";
import { timeAgo } from "../lib/time";
import { getErrorDetail } from "../api/apiError";
import { MESSAGES } from "../constants/messages";
import { isNotificationSoundMuted, setNotificationSoundMuted, useNotificationSound } from "../hooks/useNotificationSound";

/**
 * Header notifications bell — the unread count, and a dropdown of what is waiting.
 *
 * It used to hold its own /hubs/chat connection to raise toasts. ChatLiveProvider owns that now,
 * so this component reads counts and renders; it no longer maintains a socket of its own.
 *
 * Mounted once in Layout, so it runs for the lifetime of the authenticated session.
 */
export function NotificationsBell() {
  const auth = useSelector((s: RootState) => s.auth);
  // During onboarding (must change password / enrol 2FA) the server blocks everything but the
  // setup endpoints — skip these background reads + the chat hub so they don't 403-loop.
  const onboarding = !!(auth.user?.mustChangePassword || auth.user?.twoFactorSetupRequired);
  const gated = !auth.accessToken || onboarding;
  const { data: unread = [], refetch: refetchUnread } = useChatUnreadQuery(undefined, {
    skip: gated,
    pollingInterval: 30_000, skipPollingIfUnfocused: true,
  });
  const { data: rooms } = useChatRoomsQuery(undefined, { skip: gated });
  const { data: users } = useListUsersQuery(undefined, { skip: gated });

  // Work-assignment / pipeline notifications (durable inbox, not just a transient toast).
  const { data: notifUnread = 0, refetch: refetchNotifCount } = useNotificationsUnreadCountQuery(undefined, {
    skip: gated, pollingInterval: 30_000, skipPollingIfUnfocused: true,
  });
  const { data: notifs = [], refetch: refetchNotifs } = useNotificationsQuery({ take: 15 }, { skip: gated });
  const [markNotifRead] = useMarkNotificationReadMutation();
  const [markAllNotifRead] = useMarkAllNotificationsReadMutation();

  // All three of these queries are SKIPPED during onboarding, and RTK Query throws if you refetch
  // one it never started — which would take down the app shell, not just a page. Every refresh goes
  // through here so no call site has to remember that.
  const refreshInbox = useCallback(() => {
    if (gated) return;
    refetchUnread(); refetchNotifCount(); refetchNotifs();
  }, [gated, refetchUnread, refetchNotifCount, refetchNotifs]);

  const playAlert = useNotificationSound();
  const [soundMuted, setSoundMuted] = useState(isNotificationSoundMuted());
  const navigate = useNavigate();
  const location = useLocation();
  const toast = useToast();

  // Live pipeline notifications (a lead/sale forwarded to this user's queue) → popup toast.
  useAgentHub((ev, payload) => {
    if (ev !== "notification") return;
    playAlert();
    const url = payload.url;
    toast.show({
      title: payload.title ?? "New notification",
      description: payload.body,
      action: url ? { label: "Open", onClick: () => navigate(url) } : undefined,
    });
    // Durable side: refresh the inbox count + list so the bell badge updates immediately.
    refreshInbox();
  });

  const chatUnread = unread.reduce((s, u) => s + (u.unreadCount || 0), 0);
  const unreadRooms = unread.filter((u) => u.unreadCount > 0);
  // The bell badge = work notifications + chat messages.
  const totalUnread = chatUnread + notifUnread;

  // Sound whenever the badge GOES UP. The live hub push above covers a connected client, but most
  // notices arrive on the polling refresh — and a chat message raises the chat count, which the hub
  // event does not carry at all. Watching the total catches every route in one place.
  //
  // Seeded with null rather than 0 so the first read after a page load is treated as the starting
  // point. Without that, signing in with eight unread messages would set the whole room ringing.
  const lastTotalRef = useRef<number | null>(null);
  useEffect(() => {
    const previous = lastTotalRef.current;
    lastTotalRef.current = totalUnread;
    if (previous !== null && totalUnread > previous) playAlert();
  }, [totalUnread, playAlert]);

  async function openNotification(id: string, url: string | null, isRead: boolean) {
    setOpen(false);
    if (!isRead) { try { await markNotifRead(id).unwrap(); } catch { /* best-effort */ } }
    if (url) navigate(url);
  }

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

  // Click-outside close
  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [open]);

  // The global chat hub used to live here, with its own connection, its own room-joining and its
  // own toast. ChatLiveProvider in the layout now owns all of that — one connection for the whole
  // session, a sound, and a desktop notification when the CRM isn't the tab in front. Keeping this
  // copy meant two connections per user and two toasts per message.
  //
  // The bell still shows the count; it just no longer maintains a socket to do it.


  if (!auth.accessToken) return null;

  return (
    <div className="relative" ref={dropdownRef}>
      <Tooltip content={totalUnread > 0 ? `${totalUnread} unread notification${totalUnread === 1 ? "" : "s"}` : "Notifications"}>
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
                  "rounded-full bg-rose-500 text-white text-[10px] font-bold tabular-nums",
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
        <div className="absolute right-0 mt-2 w-80 max-w-[calc(100vw-2rem)] surface-elevated overflow-hidden animate-scale-in z-40">
          <div className="px-4 py-3 border-b hairline bg-gradient-to-b from-brand-soft to-white">
            <div className="flex items-center justify-between">
              <div>
                <div className="text-sm font-semibold text-ink-900">Notifications</div>
                <div className="text-xs text-ink-500">
                  {totalUnread > 0 ? `${totalUnread} unread` : "You're all caught up"}
                </div>
              </div>
              <div className="flex items-center gap-1">
                {/* A chime that cuts through a call floor is the point, but someone on a call needs
                    to be able to silence it without digging through settings. */}
                <button
                  onClick={() => {
                    const next = !soundMuted;
                    setSoundMuted(next);
                    setNotificationSoundMuted(next);
                    if (!next) playAlert();   // unmuting previews it, so the choice is audible
                  }}
                  title={soundMuted ? MESSAGES.soundOffTitle : MESSAGES.soundOnTitle}
                  aria-label={soundMuted ? MESSAGES.soundOffTitle : MESSAGES.soundOnTitle}
                  aria-pressed={!soundMuted}
                  className="p-1.5 rounded text-ink-500 hover:text-ink-900 hover:bg-ink-100/70 transition-colors"
                >
                  <Icon name={soundMuted ? "mute" : "volume"} size={15} />
                </button>
                {notifUnread > 0 && (
                  <button
                    onClick={async () => {
                      try { await markAllNotifRead().unwrap(); }
                      catch (err: unknown) {
                        toast.error(MESSAGES.markAllReadFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
                      }
                    }}
                    className="text-xs font-medium text-brand-700 hover:text-brand-800 px-2 py-1 rounded transition-colors"
                  >Mark all read</button>
                )}
              </div>
            </div>
          </div>

          <div className="max-h-[60vh] overflow-y-auto">
            {notifs.length === 0 && unreadRooms.length === 0 ? (
              <div className="px-4 py-8 text-center text-sm text-ink-500">
                <Icon name="check" size={20} className="mx-auto mb-2 text-emerald-500" />
                You're all caught up.
              </div>
            ) : (
              <>
                {/* Work / pipeline assignments */}
                {notifs.length > 0 && (
                  <>
                    <div className="px-4 pt-2.5 pb-1 text-[10px] font-bold uppercase tracking-[0.08em] text-ink-400">Work</div>
                    {notifs.map((n) => (
                      <button
                        key={n.id}
                        onClick={() => openNotification(n.id, n.url, n.isRead)}
                        className={cn(
                          "w-full flex items-start gap-3 px-4 py-3 text-left hover:bg-ink-50 transition-colors border-b hairline last:border-b-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500/40",
                          !n.isRead && "bg-brand-50/40",
                        )}
                      >
                        <span className="mt-0.5 h-8 w-8 rounded-full bg-brand-50 text-brand-600 grid place-items-center shrink-0">
                          <Icon name="bell" size={14} />
                        </span>
                        <div className="flex-1 min-w-0">
                          <div className={cn("text-sm truncate", n.isRead ? "text-ink-700" : "font-semibold text-ink-900")}>{n.title}</div>
                          <div className="text-xs text-ink-500 truncate">{n.body}</div>
                          <div className="text-[11px] text-ink-400 mt-0.5 tabular-nums">{timeAgo(n.createdAt)}</div>
                        </div>
                        {!n.isRead && <span className="mt-1 h-2 w-2 rounded-full bg-rose-500 shrink-0" aria-label="unread" />}
                      </button>
                    ))}
                  </>
                )}
                {/* Chat messages */}
                {unreadRooms.length > 0 && (
                  <>
                    <div className="px-4 pt-2.5 pb-1 text-[10px] font-bold uppercase tracking-[0.08em] text-ink-400">Messages</div>
                    {unreadRooms.map((u) => {
                      const room = (rooms ?? []).find((r) => r.id === u.roomId);
                      const name = room?.name ?? "Conversation";
                      return (
                        <button
                          key={u.roomId}
                          onClick={() => { setOpen(false); navigate(`/chat?room=${u.roomId}`); }}
                          className="w-full flex items-start gap-3 px-4 py-3 text-left hover:bg-ink-50 transition-colors border-b hairline last:border-b-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500/40"
                        >
                          <Avatar name={name} size={32} />
                          <div className="flex-1 min-w-0">
                            <div className="text-sm font-medium text-ink-900 truncate">{name}</div>
                            <div className="text-xs text-ink-500 tabular-nums">
                              {u.unreadCount} new message{u.unreadCount === 1 ? "" : "s"}
                            </div>
                          </div>
                          <Badge tone="brand" variant="solid">{u.unreadCount}</Badge>
                        </button>
                      );
                    })}
                  </>
                )}
              </>
            )}
          </div>

          <div className="border-t hairline px-3 py-2 flex items-center justify-between">
            <button
              onClick={() => { setOpen(false); refreshInbox(); }}
              className="text-xs text-ink-500 hover:text-ink-800 px-2 py-1 rounded transition-colors inline-flex items-center gap-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
            >
              <Icon name="refresh" size={12} /> Refresh
            </button>
            <button
              onClick={() => { setOpen(false); navigate("/notifications"); }}
              className="text-xs font-semibold text-brand-700 hover:text-brand-800 px-2 py-1 rounded transition-colors inline-flex items-center gap-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
            >
              View all <Icon name="arrowRight" size={12} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
