import { API_URL } from "../../shared/config";
import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from "@microsoft/signalr";
import {
  useChatRoomsQuery, useChatUnreadQuery, useCreateRoomMutation, useListUsersQuery,
  useStartDirectMessageMutation,
  useMarkRoomReadMutation, useRoomMessagesQuery, useSendAttachmentMutation,
  useSendMessageMutation, useUserDirectoryQuery,
  useToggleReactionMutation, useEditChatMessageMutation, useDeleteChatMessageMutation,
} from "../../shared/api/baseApi";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import type { ChatMessage, ChatReaction } from "../../shared/api/types";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  Avatar, Badge, Button, EmptyState, Icon, Input, Modal, Skeleton, useToast, cn,
} from "../../shared/ui";
import { MESSAGES } from "../../shared/constants/messages";
import { CHAT_MSG } from "./messages";


function formatTime(iso: string) {
  const d = new Date(iso);
  return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function dayLabel(iso: string) {
  const d = new Date(iso);
  d.setHours(0, 0, 0, 0);
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const diff = (today.getTime() - d.getTime()) / (1000 * 60 * 60 * 24);
  if (diff === 0) return "Today";
  if (diff === 1) return "Yesterday";
  if (diff < 7) return d.toLocaleDateString([], { weekday: "long" });
  return d.toLocaleDateString();
}

/** A direct room stores its name as "DM: <person>" — show just the person. Channels keep their name. */
const QUICK_REACTIONS = ["👍", "❤️", "😂", "🎉", "👀"];

function roomListTime(iso: string) {
  const d = new Date(iso);
  const now = new Date();
  return d.toDateString() === now.toDateString()
    ? d.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })
    : d.toLocaleDateString([], { month: "short", day: "numeric" });
}

function roomLabel(name: string, isDirect: boolean) {
  return isDirect ? name.replace(/^DM:\s*/i, "") : name;
}

/** True for a short message that's only emoji — rendered large without a bubble, like modern chat apps. */
function isEmojiOnly(s: string): boolean {
  const t = s.trim();
  if (!t || t.length > 12) return false;
  return /\p{Extended_Pictographic}/u.test(t)
    && /^(?:\p{Extended_Pictographic}|\p{Emoji_Component}|️|‍|\s)+$/u.test(t);
}

export function ChatPage() {
  const auth = useSelector((s: RootState) => s.auth);
  const { data: rooms, isLoading: roomsLoading } = useChatRoomsQuery();
  const { data: users } = useListUsersQuery();
  const { data: directory } = useUserDirectoryQuery();
  const { data: unread, refetch: refetchUnread } = useChatUnreadQuery(undefined, { pollingInterval: 15_000 });
  const [createRoom, { isLoading: creatingRoom }] = useCreateRoomMutation();
  const [send, { isLoading: sending }] = useSendMessageMutation();
  const [sendAttachment, { isLoading: uploading }] = useSendAttachmentMutation();
  const [markRead] = useMarkRoomReadMutation();
  const toast = useToast();

  const unreadByRoom = useMemo(
    () => new Map((unread ?? []).map((u) => [u.roomId, u.unreadCount])),
    [unread],
  );

  const [searchParams] = useSearchParams();
  const [activeRoom, setActiveRoom] = useState<string | null>(searchParams.get("room"));

  // If we land here from a notification with ?room=…, switch to that room.
  useEffect(() => {
    const r = searchParams.get("room");
    if (r) setActiveRoom(r);
  }, [searchParams]);

  // Deep-link: /chat?dm={userId} starts (or opens) a direct message with that user — lets any
  // "Chat" button / @mention across the app jump straight into a conversation with someone.
  const dmParam = searchParams.get("dm");
  useEffect(() => {
    if (dmParam) handleStartDm(dmParam);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dmParam]);
  const { data: serverMessages, refetch, isLoading: msgsLoading } = useRoomMessagesQuery(
    { roomId: activeRoom! }, { skip: !activeRoom },
  );
  const [liveMessages, setLiveMessages] = useState<ChatMessage[]>([]);
  const [body, setBody] = useState("");
  const [showNewRoom, setShowNewRoom] = useState(false);
  const [showDm, setShowDm] = useState(false);
  const [startDm] = useStartDirectMessageMutation();
  const [search, setSearch] = useState("");
  const [connectionState, setConnectionState] = useState<"connecting" | "connected" | "disconnected">("connecting");
  const connRef = useRef<HubConnection | null>(null);
  const presenceRef = useRef<HubConnection | null>(null);
  // userId -> true when online (PresenceHub reports anything other than Offline)
  const [onlineUsers, setOnlineUsers] = useState<Set<string>>(new Set());
  // roomId -> userId -> lastReadAt (ISO). Mirrors server state, updated live via "RoomRead" events.
  const [roomReads, setRoomReads] = useState<Record<string, Record<string, string>>>({});
  // userId -> last "Typing" timestamp (ms) in the ACTIVE room; entries expire after a few seconds.
  const [typingUsers, setTypingUsers] = useState<Record<string, number>>({});
  const lastTypingSentRef = useRef(0);
  // Live overrides applied over the fetched/live messages: reaction sets, edits, and deletions
  // arriving via SignalR, so we never mutate the RTK cache directly.
  type MsgOverride = { body?: string; editedAt?: string | null; reactions?: ChatReaction[]; deleted?: boolean };
  const [overrides, setOverrides] = useState<Record<string, MsgOverride>>({});
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editBody, setEditBody] = useState("");
  const [toggleReactionMut] = useToggleReactionMutation();
  const [editMsg] = useEditChatMessageMutation();
  const [deleteMsg] = useDeleteChatMessageMutation();
  const confirm = useConfirm();
  const [msgSearch, setMsgSearch] = useState("");
  const [showMsgSearch, setShowMsgSearch] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const [atBottom, setAtBottom] = useState(true);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Live refs for values that change often but shouldn't rebuild the SignalR connection.
  // (Previously these were in the effect deps and tore the connection down on every
  // room-switch / RTK-Query render, leaving orphaned negotiation IDs that came back as 404.)
  const tokenRef = useRef<string | null>(auth.accessToken);
  tokenRef.current = auth.accessToken;
  const activeRoomRef = useRef<string | null>(activeRoom);
  activeRoomRef.current = activeRoom;
  const refetchUnreadRef = useRef(refetchUnread);
  refetchUnreadRef.current = refetchUnread;
  const markReadRef = useRef(markRead);
  markReadRef.current = markRead;
  const lastActiveMarkReadRef = useRef(0);

  // SignalR setup — runs only when the access token actually changes.
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
      setLiveMessages((prev) => [...prev, msg]);
      if (msg.roomId !== activeRoomRef.current) {
        refetchUnreadRef.current();
      } else if (document.hasFocus() && Date.now() - lastActiveMarkReadRef.current > 2000) {
        // A message landed in the room we're actively viewing — re-mark it read so its unread
        // badge doesn't reappear on the next poll (and others see it as seen). Throttled so a
        // busy channel doesn't spam the hub.
        lastActiveMarkReadRef.current = Date.now();
        const room = activeRoomRef.current;
        if (room) markReadRef.current(room).unwrap().then(() => refetchUnreadRef.current()).catch(() => {});
      }
    });
    // Other members' read receipts. Server fires this whenever any member calls
    // markRead — we use it to upgrade our own messages from "delivered" to "seen".
    conn.on("RoomRead", (e: { roomId: string; userId: string; readAt: string }) => {
      setRoomReads((prev) => {
        const room = { ...(prev[e.roomId] ?? {}) };
        room[e.userId] = e.readAt;
        return { ...prev, [e.roomId]: room };
      });
    });
    // Live "someone is typing" — the server broadcasts this to a room's other members.
    conn.on("Typing", (roomId: string, uid: string) => {
      if (roomId !== activeRoomRef.current || uid === auth.user?.id) return;
      setTypingUsers((prev) => ({ ...prev, [uid]: Date.now() }));
    });
    conn.on("ReactionsChanged", (e: { roomId: string; messageId: string; reactions: ChatReaction[] }) => {
      if (e.roomId !== activeRoomRef.current) return;
      setOverrides((prev) => ({ ...prev, [e.messageId]: { ...prev[e.messageId], reactions: e.reactions } }));
    });
    conn.on("MessageEdited", (m: ChatMessage) => {
      if (m.roomId !== activeRoomRef.current) return;
      setOverrides((prev) => ({ ...prev, [m.id]: { ...prev[m.id], body: m.body, editedAt: m.editedAt, reactions: m.reactions ?? undefined } }));
    });
    conn.on("MessageDeleted", (e: { roomId: string; messageId: string }) => {
      if (e.roomId !== activeRoomRef.current) return;
      setOverrides((prev) => ({ ...prev, [e.messageId]: { ...prev[e.messageId], deleted: true } }));
    });
    conn.onreconnecting(() => setConnectionState("connecting"));
    conn.onreconnected(() => setConnectionState("connected"));
    conn.onclose(() => setConnectionState("disconnected"));

    setConnectionState("connecting");
    conn.start()
      .then(() => { connRef.current = conn; setConnectionState("connected"); })
      .catch(() => setConnectionState("disconnected"));

    return () => {
      connRef.current = null;
      if (conn.state !== HubConnectionState.Disconnected) {
        conn.stop().catch(() => {});
      }
    };
  }, [auth.accessToken]);

  // PresenceHub — tracks who else is online so the chat header / room list
  // can show a green dot, and so we can decide between "delivered" vs "sent"
  // ticks on our own messages. Status enum: Offline=0, anything else = online.
  useEffect(() => {
    if (!auth.accessToken) return;
    const conn = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/presence`, { accessTokenFactory: () => tokenRef.current ?? "" })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => {
          if (!tokenRef.current) return null;
          if (ctx.previousRetryCount >= 4) return null;
          return Math.min(1000 * 2 ** ctx.previousRetryCount, 15_000);
        },
      })
      .build();

    const OFFLINE = 0; // matches AgentStatus.Offline on the server (0, not 2 — 2 is OnCall)
    conn.on("PresenceChanged", (uid: string, status: number) => {
      setOnlineUsers((prev) => {
        const next = new Set(prev);
        if (status === OFFLINE) next.delete(uid);
        else next.add(uid);
        return next;
      });
    });

    conn.start()
      .then(async () => {
        presenceRef.current = conn;
        try {
          const all = await conn.invoke<Record<string, number>>("GetAll");
          setOnlineUsers(new Set(
            Object.entries(all).filter(([, s]) => s !== OFFLINE).map(([uid]) => uid),
          ));
        } catch { /* ignore */ }
      })
      .catch(() => { /* presence is best-effort */ });

    return () => {
      presenceRef.current = null;
      if (conn.state !== HubConnectionState.Disconnected) conn.stop().catch(() => {});
    };
  }, [auth.accessToken]);

  // Room-switch side effects (mark-read, focus, clear live-tail).
  useEffect(() => {
    setLiveMessages([]);
    setTypingUsers({});
    setOverrides({});
    setEditingId(null);
    setMsgSearch("");
    setShowMsgSearch(false);
    if (activeRoom) {
      markRead(activeRoom).unwrap().then(() => refetchUnread()).catch(() => {});
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [activeRoom, markRead, refetchUnread]);

  // Expire stale typing indicators (~3.5s after the sender's last keystroke).
  useEffect(() => {
    const t = setInterval(() => {
      setTypingUsers((prev) => {
        if (Object.keys(prev).length === 0) return prev;
        const now = Date.now();
        const next: Record<string, number> = {};
        let changed = false;
        for (const [uid, ts] of Object.entries(prev)) {
          if (now - ts < 3500) next[uid] = ts; else changed = true;
        }
        return changed ? next : prev;
      });
    }, 1200);
    return () => clearInterval(t);
  }, []);

  // Join the SignalR group for the active room — runs ONLY once both the
  // connection is ready and we have a room.  Previously this was tucked into
  // the room-switch effect which fired before `conn.start()` resolved, so
  // `connRef.current` was null and the JoinRoom call was silently dropped.
  // That's why messages didn't arrive in real-time and you had to refresh.
  useEffect(() => {
    if (connectionState !== "connected" || !activeRoom || !connRef.current) return;
    const conn = connRef.current;
    conn.invoke("JoinRoom", activeRoom).catch(() => {});
    return () => {
      // Leave the previous group when switching rooms / unmounting.
      conn.invoke("LeaveRoom", activeRoom).catch(() => {});
    };
  }, [connectionState, activeRoom]);

  // Auto-scroll on new messages.
  // Dedupe by id — own messages otherwise show twice, once from the REST send
  // refetch (serverMessages) and once from the SignalR broadcast (liveMessages).
  const messages = useMemo(() => {
    const seen = new Set<string>();
    const out: ChatMessage[] = [];
    const add = (m: ChatMessage) => {
      if (seen.has(m.id)) return;
      seen.add(m.id);
      const o = overrides[m.id];
      if (o?.deleted) return;
      out.push(o ? { ...m, ...(o.body !== undefined && { body: o.body }), ...(o.editedAt !== undefined && { editedAt: o.editedAt }), ...(o.reactions !== undefined && { reactions: o.reactions }) } : m);
    };
    for (const m of serverMessages ?? []) add(m);
    for (const m of liveMessages) { if (m.roomId === activeRoom) add(m); }
    return out;
  }, [serverMessages, liveMessages, activeRoom, overrides]);
  useEffect(() => {
    // Don't yank the view down while the user is reading history or searching.
    if (atBottom && !msgSearch) messagesEndRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [messages.length, atBottom, msgSearch]);

  function jumpToLatest() {
    setMsgSearch("");
    requestAnimationFrame(() => messagesEndRef.current?.scrollIntoView({ behavior: "smooth", block: "end" }));
  }
  function onMessagesScroll() {
    const el = scrollRef.current;
    if (el) setAtBottom(el.scrollHeight - el.scrollTop - el.clientHeight < 80);
  }

  const filteredRooms = useMemo(() => {
    if (!rooms) return [];
    const q = search.trim().toLowerCase();
    const list = q
      ? rooms.filter((r) => r.name.toLowerCase().includes(q) || (r.lastMessagePreview ?? "").toLowerCase().includes(q))
      : rooms;
    // Most-recently-active room first (rooms with no messages fall to the bottom).
    return [...list].sort((a, b) =>
      (b.lastMessageAt ? new Date(b.lastMessageAt).getTime() : 0) -
      (a.lastMessageAt ? new Date(a.lastMessageAt).getTime() : 0));
  }, [rooms, search]);

  // Seed lastReadAt map from the rooms list. Live updates layer on top.
  useEffect(() => {
    if (!rooms) return;
    setRoomReads((prev) => {
      const next = { ...prev };
      for (const r of rooms) {
        const room = next[r.id] ?? {};
        for (const m of r.members ?? []) {
          if (m.lastReadAt && !room[m.userId]) room[m.userId] = m.lastReadAt;
        }
        next[r.id] = room;
      }
      return next;
    });
  }, [rooms]);

  const userMap = useMemo(() => {
    const m = new Map<string, string>();
    for (const u of directory ?? []) m.set(u.id, u.userName);
    for (const u of users ?? []) m.set(u.id, u.userName);
    if (auth.user) m.set(auth.user.id, auth.user.userName);
    return m;
  }, [directory, users, auth.user]);
  const activeRoomData = rooms?.find((r) => r.id === activeRoom);

  // "Other members" of the active room (everyone except me). Drives the
  // online-dot in the header and the read-receipt logic on my own messages.
  const otherMembers = useMemo(
    () => (activeRoomData?.memberUserIds ?? []).filter((id) => id !== auth.user?.id),
    [activeRoomData, auth.user?.id],
  );
  const anyOtherOnline = otherMembers.some((id) => onlineUsers.has(id));
  // Earliest "lastReadAt" across all other members — a message is "seen by all"
  // only when every other member has read past it. For 1:1 chats this collapses
  // to the single recipient's lastReadAt.
  const minOtherReadAt = useMemo(() => {
    if (!activeRoom || otherMembers.length === 0) return null;
    const reads = roomReads[activeRoom] ?? {};
    let min: number | null = null;
    for (const uid of otherMembers) {
      const t = reads[uid];
      if (!t) return null; // someone has never read → can't be "seen by all"
      const ms = new Date(t).getTime();
      if (min === null || ms < min) min = ms;
    }
    return min;
  }, [activeRoom, otherMembers, roomReads]);

  // Tell the room I'm typing — throttled to at most once every 2s so we don't spam the hub.
  function handleTyping() {
    const now = Date.now();
    if (now - lastTypingSentRef.current < 2000) return;
    lastTypingSentRef.current = now;
    const room = activeRoomRef.current;
    if (room && connRef.current) connRef.current.invoke("Typing", room).catch(() => {});
  }

  const typingNames = useMemo(
    () => Object.keys(typingUsers).filter((uid) => uid !== auth.user?.id).map((uid) => userMap.get(uid) ?? "Someone"),
    [typingUsers, userMap, auth.user?.id],
  );
  const typingText = typingNames.length === 0 ? ""
    : typingNames.length === 1 ? `${typingNames[0]} is typing…`
    : typingNames.length === 2 ? `${typingNames[0]} and ${typingNames[1]} are typing…`
    : "Several people are typing…";

  function toggleReaction(messageId: string, emoji: string) {
    if (!activeRoom) return;
    toggleReactionMut({ messageId, emoji, roomId: activeRoom }).unwrap().catch(() => {});
  }
  function startEdit(m: ChatMessage) { setEditingId(m.id); setEditBody(m.body); }
  function cancelEdit() { setEditingId(null); setEditBody(""); }
  async function saveEdit(m: ChatMessage) {
    const text = editBody.trim();
    if (!text || !activeRoom) { cancelEdit(); return; }
    if (text === m.body) { cancelEdit(); return; }
    try { await editMsg({ messageId: m.id, body: text, roomId: activeRoom }).unwrap(); }
    catch (err: unknown) { toast.error(CHAT_MSG.editFailed, getErrorDetail(err) ?? MESSAGES.tryAgain); }
    cancelEdit();
  }
  async function onDeleteMessage(m: ChatMessage) {
    if (!activeRoom) return;
    if (!(await confirm({ title: CHAT_MSG.deleteMessageTitle, description: CHAT_MSG.deleteMessageDesc, confirmLabel: "Delete", danger: true }))) return;
    try { await deleteMsg({ messageId: m.id, roomId: activeRoom }).unwrap(); }
    catch (err: unknown) { toast.error(CHAT_MSG.deleteFailed, getErrorDetail(err) ?? MESSAGES.tryAgain); }
  }

  async function handleSend(e: React.FormEvent) {
    e.preventDefault();
    if (!activeRoom || !body.trim()) return;
    const text = body;
    setBody("");
    try {
      await send({ roomId: activeRoom, body: text }).unwrap();
      refetch();
    } catch (err: unknown) {
      setBody(text); // restore on error
      toast.error(CHAT_MSG.sendFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  const MAX_BYTES = 25 * 1024 * 1024;
  async function handleAttach(file: File) {
    if (!activeRoom) return;
    if (file.size > MAX_BYTES) {
      toast.error(CHAT_MSG.fileTooLargeTitle, CHAT_MSG.fileTooLargeDesc(MAX_BYTES / (1024 * 1024)));
      return;
    }
    const text = body;
    setBody("");
    try {
      await sendAttachment({ roomId: activeRoom, body: text, file }).unwrap();
      refetch();
    } catch (err: unknown) {
      setBody(text);
      toast.error(CHAT_MSG.uploadFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function handleCreate(name: string, memberIds: string[]) {
    try {
      const room = await createRoom({ name, isDirect: false, memberUserIds: memberIds }).unwrap();
      toast.success(CHAT_MSG.roomCreated, `#${room.name}`);
      setActiveRoom(room.id);
      setShowNewRoom(false);
    } catch (err: unknown) {
      toast.error(CHAT_MSG.createRoomFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function handleStartDm(userId: string) {
    try {
      const room = await startDm(userId).unwrap();
      setActiveRoom(room.id);
      setShowDm(false);
    } catch (err: unknown) {
      toast.error(CHAT_MSG.startChatFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  // Group messages by day for date separators
  type Group = { day: string; items: typeof messages };
  const grouped = useMemo<Group[]>(() => {
    const q = msgSearch.trim().toLowerCase();
    const src = q ? messages.filter((m) => (m.body ?? "").toLowerCase().includes(q)) : messages;
    const groups: Group[] = [];
    for (const m of src) {
      const day = dayLabel(m.sentAt);
      const last = groups[groups.length - 1];
      if (last && last.day === day) last.items.push(m);
      else groups.push({ day, items: [m] });
    }
    return groups;
  }, [messages, msgSearch]);

  return (
    <div className="flex h-[calc(100vh-9rem)] surface overflow-hidden -m-1">
      {/* Sidebar — on mobile this is the master list: full-width when no room is
          open, hidden once a room is selected (the message pane takes over). */}
      <aside className={cn(
        "w-full md:w-72 bg-ink-50/60 border-r hairline flex-col shrink-0",
        activeRoom ? "hidden md:flex" : "flex",
      )}>
        <div className="p-4 border-b hairline">
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-base font-semibold text-ink-900">Conversations</h2>
            <div className="flex items-center gap-1.5">
              <Button
                size="sm" variant="outline" leftIcon={<Icon name="users" size={14} />}
                onClick={() => setShowDm(true)}
              >Message</Button>
              <Button
                size="sm" leftIcon={<Icon name="plus" size={14} />}
                onClick={() => setShowNewRoom(true)}
              >New</Button>
            </div>
          </div>
          <Input
            leftIcon={<Icon name="search" size={14} />}
            placeholder={CHAT_MSG.searchConversations}
            value={search} onChange={(e) => setSearch(e.target.value)}
            className="h-9"
          />
        </div>

        <div className="flex-1 overflow-y-auto p-2">
          {roomsLoading ? (
            <div className="space-y-2 p-2">
              {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-14" />)}
            </div>
          ) : !rooms || rooms.length === 0 ? (
            <div className="p-6">
              <EmptyState
                icon={<Icon name="chat" size={18} />}
                title={CHAT_MSG.noConversationsTitle}
                description={CHAT_MSG.noConversationsDesc}
                action={
                  <Button size="sm" leftIcon={<Icon name="plus" size={14} />} onClick={() => setShowNewRoom(true)}>
                    New room
                  </Button>
                }
              />
            </div>
          ) : filteredRooms.length === 0 ? (
            <div className="p-4 text-center text-sm text-ink-500">No rooms match "{search}"</div>
          ) : (
            <ul className="space-y-1">
              {filteredRooms.map((r) => {
                const active = activeRoom === r.id;
                const unreadCount = unreadByRoom.get(r.id) ?? 0;
                return (
                  <li key={r.id}>
                    <button
                      onClick={() => setActiveRoom(r.id)}
                      className={cn(
                        "w-full text-left px-3 py-2.5 rounded-lg transition-colors flex items-center gap-3",
                        active ? "bg-white shadow-card border hairline" : "hover:bg-white/60",
                      )}
                    >
                      {r.isDirect
                        ? <span className="relative shrink-0">
                            <Avatar name={roomLabel(r.name, true)} size={36} />
                            {r.memberUserIds.some((id) => id !== auth.user?.id && onlineUsers.has(id)) && (
                              <span className="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full bg-emerald-500 ring-2 ring-white" title="Online" />
                            )}
                          </span>
                        : <span className="h-9 w-9 shrink-0 grid place-items-center rounded-full bg-brand-100 text-brand-700 text-sm font-semibold">#</span>}
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center justify-between gap-2">
                          <span className={cn("text-sm truncate", active || unreadCount > 0 ? "font-semibold text-ink-900" : "font-medium text-ink-700")}>
                            {r.isDirect ? roomLabel(r.name, true) : r.name}
                          </span>
                          <span className="flex items-center gap-1.5 shrink-0">
                            {r.lastMessageAt && <span className="text-[11px] text-ink-400 tabular-nums">{roomListTime(r.lastMessageAt)}</span>}
                            {unreadCount > 0 && (
                              <Badge tone="brand" variant="solid" className="tabular-nums">{unreadCount}</Badge>
                            )}
                          </span>
                        </div>
                        <div className={cn("text-xs truncate", unreadCount > 0 ? "text-ink-700 font-medium" : "text-ink-500")}>
                          {r.lastMessagePreview
                            ? <>{r.lastMessageSenderId === auth.user?.id ? "You: " : ""}{r.lastMessagePreview}</>
                            : <span className="italic text-ink-400">No messages yet</span>}
                        </div>
                      </div>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        <div className="p-3 border-t hairline">
          <ConnectionStatus state={connectionState} />
        </div>
      </aside>

      {/* Main — on mobile this replaces the list once a room is open; the empty
          state only ever shows on desktop (mobile shows the list instead). */}
      <main className={cn(
        "relative flex-1 flex-col bg-white min-w-0",
        activeRoom ? "flex" : "hidden md:flex",
      )}>
        {!activeRoom ? (
          <div className="flex-1 grid place-items-center p-6">
            <EmptyState
              icon={<Icon name="chat" size={20} />}
              title={CHAT_MSG.pickConversationTitle}
              description={CHAT_MSG.pickConversationDesc}
              action={
                <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setShowNewRoom(true)}>
                  New conversation
                </Button>
              }
            />
          </div>
        ) : (
          <>
            {/* Conversation header */}
            <div className="h-16 border-b hairline px-4 sm:px-5 flex items-center gap-3 shrink-0">
              <button
                onClick={() => setActiveRoom(null)}
                className="md:hidden -ml-1 p-1.5 rounded-lg text-ink-500 hover:bg-ink-100 hover:text-ink-900 transition-colors"
                aria-label="Back to conversations"
              >
                <Icon name="chevronLeft" size={20} />
              </button>
              {activeRoomData?.isDirect
                ? <Avatar name={roomLabel(activeRoomData?.name ?? "?", true)} size={36} />
                : <span className="h-9 w-9 shrink-0 grid place-items-center rounded-full bg-brand-100 text-brand-700 text-base font-semibold">#</span>}
              <div className="flex-1 min-w-0">
                <div className="text-sm font-semibold text-ink-900 truncate">
                  {activeRoomData?.isDirect ? roomLabel(activeRoomData?.name ?? "", true) : activeRoomData?.name}
                </div>
                <div className="text-xs text-ink-500 flex items-center gap-1.5">
                  <span className="tabular-nums">{activeRoomData?.memberUserIds.length} members</span>
                  {otherMembers.length > 0 && (
                    <>
                      <span>·</span>
                      <span className="inline-flex items-center gap-1">
                        <span className={cn("h-1.5 w-1.5 rounded-full", anyOtherOnline ? "bg-emerald-500" : "bg-ink-300")} />
                        <span className={anyOtherOnline ? "text-emerald-600" : "text-ink-500"}>
                          {anyOtherOnline ? "online" : "offline"}
                        </span>
                      </span>
                    </>
                  )}
                </div>
              </div>
              {showMsgSearch && (
                <Input autoFocus leftIcon={<Icon name="search" size={14} />} placeholder={CHAT_MSG.searchInConversation}
                  value={msgSearch} onChange={(e) => setMsgSearch(e.target.value)} className="h-9 w-40 sm:w-56" />
              )}
              <Button variant="ghost" size="icon" aria-label="Search messages"
                onClick={() => setShowMsgSearch((v) => { if (v) setMsgSearch(""); return !v; })}>
                <Icon name="search" size={18} />
              </Button>
              <Button variant="ghost" size="icon" aria-label="Refresh" onClick={() => refetch()}>
                <Icon name="refresh" size={18} />
              </Button>
            </div>

            {/* Messages */}
            <div ref={scrollRef} onScroll={onMessagesScroll} className="flex-1 overflow-y-auto px-5 py-4 bg-ink-50/30">
              {msgsLoading ? (
                <div className="space-y-3">
                  {[0, 1, 2].map((i) => (
                    <div key={i} className={cn("flex", i % 2 ? "justify-end" : "justify-start")}>
                      <Skeleton className="h-12 w-64 rounded-2xl" />
                    </div>
                  ))}
                </div>
              ) : messages.length === 0 ? (
                <div className="h-full grid place-items-center">
                  <EmptyState
                    icon={<Icon name="chat" size={20} />}
                    title={CHAT_MSG.noMessagesTitle}
                    description={CHAT_MSG.noMessagesDesc}
                  />
                </div>
              ) : (
                <div className="space-y-4">
                  {grouped.map((g) => (
                    <div key={g.day} className="space-y-2">
                      <div className="flex items-center gap-3 my-3">
                        <div className="flex-1 border-t hairline" />
                        <span className="text-[11px] uppercase tracking-wider font-medium text-ink-400">{g.day}</span>
                        <div className="flex-1 border-t hairline" />
                      </div>
                      {g.items.map((m, idx) => {
                        const isMe = m.senderUserId === auth.user?.id;
                        const prev = g.items[idx - 1];
                        const showHeader = !prev || prev.senderUserId !== m.senderUserId;
                        const senderName = isMe ? "You" : (userMap.get(m.senderUserId) ?? "User");
                        const emojiOnly = !!m.body && !m.attachmentName && isEmojiOnly(m.body);
                        const isEditing = editingId === m.id;
                        return (
                          <div key={m.id} className={cn("group relative flex gap-2.5", isMe ? "justify-end" : "justify-start")}>
                            {!isMe && (
                              <div className="w-8 shrink-0">
                                {showHeader && <Avatar name={senderName} size={32} />}
                              </div>
                            )}
                            <div className={cn("max-w-[68%] flex flex-col", isMe && "items-end")}>
                              {showHeader && (
                                <div className={cn("text-[11px] text-ink-500 mb-1 px-1 tabular-nums", isMe && "text-right")}>
                                  {senderName} · {formatTime(m.sentAt)}
                                </div>
                              )}
                              {isEditing ? (
                                <div className="flex items-center gap-2">
                                  <input autoFocus className="input-base h-9 w-56 sm:w-64" value={editBody}
                                    onChange={(e) => setEditBody(e.target.value)}
                                    onKeyDown={(e) => { if (e.key === "Enter") saveEdit(m); if (e.key === "Escape") cancelEdit(); }} />
                                  <Button size="sm" onClick={() => saveEdit(m)}>Save</Button>
                                  <Button size="sm" variant="ghost" onClick={cancelEdit}>Cancel</Button>
                                </div>
                              ) : emojiOnly ? (
                                <div className={cn("text-[2.75rem] leading-none select-none px-1 py-0.5", isMe && "text-right")}>{m.body}</div>
                              ) : (
                                <div className={cn(
                                  "rounded-2xl shadow-sm break-words overflow-hidden",
                                  isMe
                                    ? "bg-gradient-to-br from-brand-500 to-brand-700 text-white rounded-br-md"
                                    : "bg-white text-ink-800 ring-1 ring-ink-200/70 rounded-bl-md",
                                )}>
                                  {m.attachmentName && (
                                    <Attachment message={m} accessToken={auth.accessToken} isMe={isMe} />
                                  )}
                                  {m.body && (
                                    <div className="px-3.5 py-2 text-sm leading-relaxed">
                                      {m.body}
                                      {m.editedAt && <span className={cn("text-[10px] ml-1.5", isMe ? "text-white/60" : "text-ink-400")}>(edited)</span>}
                                    </div>
                                  )}
                                </div>
                              )}

                              {/* Reactions */}
                              {m.reactions && m.reactions.some((r) => r.userIds.length > 0) && (
                                <div className={cn("flex flex-wrap gap-1 mt-1", isMe && "justify-end")}>
                                  {m.reactions.filter((r) => r.userIds.length > 0).map((r) => {
                                    const mine = r.userIds.includes(auth.user?.id ?? "");
                                    return (
                                      <button key={r.emoji} type="button" onClick={() => toggleReaction(m.id, r.emoji)}
                                        className={cn("inline-flex items-center gap-1 px-1.5 h-6 rounded-full text-xs border transition-colors",
                                          mine ? "bg-brand-50 border-brand-300 text-brand-700" : "bg-white border-ink-200 text-ink-600 hover:border-ink-300")}>
                                        <span>{r.emoji}</span><span className="tabular-nums">{r.userIds.length}</span>
                                      </button>
                                    );
                                  })}
                                </div>
                              )}

                              {isMe && otherMembers.length > 0 && !isEditing && (
                                <div className="mt-0.5 px-1 text-right">
                                  <ReadReceipt
                                    state={
                                      minOtherReadAt !== null && minOtherReadAt >= new Date(m.sentAt).getTime()
                                        ? "seen"
                                        : anyOtherOnline ? "delivered" : "sent"
                                    }
                                  />
                                </div>
                              )}
                            </div>

                            {/* Hover toolbar: quick-react + edit/delete (own) */}
                            {!isEditing && (
                              <div className={cn(
                                "absolute -top-3 opacity-0 group-hover:opacity-100 focus-within:opacity-100 transition-opacity flex items-center gap-0.5 bg-white border hairline rounded-lg shadow-sm px-1 h-7 z-10",
                                isMe ? "right-1" : "left-10",
                              )}>
                                {QUICK_REACTIONS.map((e) => (
                                  <button key={e} type="button" onClick={() => toggleReaction(m.id, e)}
                                    className="h-6 w-6 grid place-items-center hover:bg-ink-100 rounded text-sm" aria-label={`React ${e}`}>{e}</button>
                                ))}
                                {isMe && !m.attachmentName && (
                                  <button type="button" onClick={() => startEdit(m)}
                                    className="h-6 w-6 grid place-items-center hover:bg-ink-100 rounded" aria-label="Edit message"><Icon name="edit" size={12} /></button>
                                )}
                                {isMe && (
                                  <button type="button" onClick={() => onDeleteMessage(m)}
                                    className="h-6 w-6 grid place-items-center hover:bg-rose-50 rounded" aria-label="Delete message"><Icon name="trash" size={12} className="text-rose-500" /></button>
                                )}
                              </div>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  ))}
                  <div ref={messagesEndRef} />
                </div>
              )}
            </div>

            {/* Jump to latest — shown when the user has scrolled up */}
            {!atBottom && (
              <button onClick={jumpToLatest} aria-label="Jump to latest messages"
                className="absolute bottom-24 right-6 z-10 h-10 w-10 rounded-full bg-white border hairline shadow-card grid place-items-center text-ink-600 hover:text-brand-600 hover:shadow-card-hover transition-all">
                <Icon name="chevronDown" size={18} />
              </button>
            )}

            {/* Live typing indicator */}
            {typingText && (
              <div className="px-5 py-2 bg-white flex items-center gap-2 text-xs text-ink-500 shrink-0">
                <span className="flex gap-1">
                  <span className="h-1.5 w-1.5 rounded-full bg-ink-400 animate-bounce [animation-delay:-0.3s]" />
                  <span className="h-1.5 w-1.5 rounded-full bg-ink-400 animate-bounce [animation-delay:-0.15s]" />
                  <span className="h-1.5 w-1.5 rounded-full bg-ink-400 animate-bounce" />
                </span>
                <span>{typingText}</span>
              </div>
            )}

            {/* Composer */}
            <Composer
              inputRef={inputRef}
              body={body}
              setBody={setBody}
              sending={sending}
              uploading={uploading}
              maxBytes={MAX_BYTES}
              onSend={handleSend}
              onAttach={handleAttach}
              onTyping={handleTyping}
            />
          </>
        )}
      </main>

      {/* New room modal — fed from the OPEN user directory (/api/users/directory), which every
          authenticated user in the agency can read. The permission-gated /api/users list (`users`)
          403s for non-privileged roles (License Agent, Fronter, …), which is what left the member
          picker showing "No teammates match". */}
      <NewRoomModal
        open={showNewRoom}
        onClose={() => setShowNewRoom(false)}
        users={(directory ?? []).filter((u) => u.id !== auth.user?.id)}
        loading={creatingRoom}
        onCreate={handleCreate}
      />

      {/* Direct message (same office) */}
      <DirectMessageModal
        open={showDm}
        onClose={() => setShowDm(false)}
        users={(directory ?? []).filter((u) => u.id !== auth.user?.id)}
        onPick={handleStartDm}
      />
    </div>
  );
}

function DirectMessageModal({
  open, onClose, users, onPick,
}: {
  open: boolean;
  onClose: () => void;
  users: { id: string; userName: string; email?: string }[];
  onPick: (userId: string) => void;
}) {
  const [q, setQ] = useState("");
  const filtered = users.filter((u) =>
    u.userName.toLowerCase().includes(q.toLowerCase()) ||
    (u.email ?? "").toLowerCase().includes(q.toLowerCase()));
  return (
    <Modal open={open} onClose={onClose} title="New direct message" size="sm"
      description="Pick a colleague in your office to start a private 1:1 chat.">
      <Input
        autoFocus
        leftIcon={<Icon name="search" size={14} />}
        placeholder={CHAT_MSG.searchPeople}
        value={q}
        onChange={(e) => setQ(e.target.value)}
        containerClassName="mb-3"
      />
      <div className="max-h-72 overflow-auto -mx-1">
        {filtered.length === 0 ? (
          <div className="text-sm text-ink-500 px-3 py-6 text-center">No colleagues found.</div>
        ) : filtered.map((u) => (
          <button
            key={u.id}
            onClick={() => onPick(u.id)}
            className="w-full flex items-center gap-3 px-3 py-2 rounded-lg hover:bg-ink-50 text-left transition-colors"
          >
            <Avatar name={u.userName} size={32} />
            <span className="min-w-0">
              <span className="block text-sm font-medium text-ink-900 truncate">{u.userName}</span>
              <span className="block text-[11px] text-ink-500 truncate">{u.email}</span>
            </span>
            <Icon name="chat" size={14} className="ml-auto text-ink-400" />
          </button>
        ))}
      </div>
    </Modal>
  );
}

function ReadReceipt({ state }: { state: "sent" | "delivered" | "seen" }) {
  // WhatsApp-style ticks. Single = sent (server has it), double grey = delivered
  // (someone else is online and the broadcaster reached them), double blue = seen
  // (every other member's lastReadAt has passed this message).
  const color = state === "seen" ? "text-brand-500" : "text-white/70";
  const label = state === "seen" ? "Seen" : state === "delivered" ? "Delivered" : "Sent";
  return (
    <span className={cn("inline-flex items-center gap-0.5", color)} title={label} aria-label={label}>
      <svg width="14" height="10" viewBox="0 0 16 11" fill="none" xmlns="http://www.w3.org/2000/svg">
        <path d="M1 6L4.5 9.5L10 2" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
        {(state === "delivered" || state === "seen") && (
          <path d="M6 6L9.5 9.5L15 2" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
        )}
      </svg>
    </span>
  );
}

function formatBytes(n: number) {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / (1024 * 1024)).toFixed(1)} MB`;
}

function Attachment({
  message, accessToken, isMe,
}: {
  message: ChatMessage;
  accessToken: string | null;
  isMe: boolean;
}) {
  const [blobUrl, setBlobUrl] = useState<string | null>(null);
  const isImage = (message.attachmentContentType ?? "").startsWith("image/");

  // The download endpoint requires a Bearer token, so we can't drop the URL
  // straight into <img src="…"> — fetch with auth and present the bytes via
  // an object URL. Cleanup runs on unmount / message swap.
  useEffect(() => {
    if (!isImage || !accessToken) return;
    let url: string | null = null;
    let cancelled = false;
    fetch(`${API_URL}/api/chat/messages/${message.id}/attachment`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    })
      .then((r) => (r.ok ? r.blob() : Promise.reject(r)))
      .then((blob) => {
        if (cancelled) return;
        url = URL.createObjectURL(blob);
        setBlobUrl(url);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [isImage, accessToken, message.id]);

  async function handleDownload() {
    if (!accessToken) return;
    const r = await fetch(`${API_URL}/api/chat/messages/${message.id}/attachment`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    if (!r.ok) return;
    const blob = await r.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = message.attachmentName ?? "file";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }

  if (isImage) {
    return (
      <button
        type="button"
        onClick={handleDownload}
        className="block w-full"
        title={message.attachmentName ?? ""}
      >
        {blobUrl ? (
          <img src={blobUrl} alt={message.attachmentName ?? ""} className="block max-w-[280px] max-h-[280px] object-cover" />
        ) : (
          <div className="w-[280px] h-[160px] bg-ink-100 animate-pulse" />
        )}
      </button>
    );
  }

  return (
    <button
      type="button"
      onClick={handleDownload}
      className={cn(
        "flex items-center gap-2.5 px-3 py-2.5 text-left w-full",
        isMe ? "hover:bg-white/10" : "hover:bg-ink-50",
      )}
    >
      <div className={cn(
        "h-9 w-9 rounded-lg grid place-items-center shrink-0",
        isMe ? "bg-white/15" : "bg-ink-100",
      )}>
        <Icon name="file" size={18} />
      </div>
      <div className="min-w-0 flex-1">
        <div className={cn("text-sm font-medium truncate", isMe ? "text-white" : "text-ink-800")}>
          {message.attachmentName}
        </div>
        <div className={cn("text-[11px] tabular-nums", isMe ? "text-white/70" : "text-ink-500")}>
          {formatBytes(message.attachmentSize ?? 0)}
        </div>
      </div>
    </button>
  );
}

const EMOJIS = [
  "😀","😃","😄","😁","😆","😅","😂","🤣","😊","😇","🙂","🙃","😉","😌","😍","🥰",
  "😘","😗","😙","😚","😋","😛","😜","🤪","😎","🤩","🥳","😏","😒","😞","😔","😢",
  "😭","😤","😠","😡","🤔","🤨","😐","😑","🙄","😴","🤤","🤒","🤕","🤧","😷","🤯",
  "👍","👎","👏","🙏","💪","🙌","👌","✌️","🤝","🔥","✨","🎉","💯","❤️","💔","✅",
];

function Composer({
  inputRef, body, setBody, sending, uploading, maxBytes, onSend, onAttach, onTyping,
}: {
  inputRef: React.RefObject<HTMLInputElement | null>;
  body: string;
  setBody: (v: string) => void;
  sending: boolean;
  uploading: boolean;
  maxBytes: number;
  onSend: (e: React.FormEvent) => void;
  onAttach: (file: File) => void;
  onTyping?: () => void;
}) {
  const fileRef = useRef<HTMLInputElement>(null);
  const [showEmoji, setShowEmoji] = useState(false);

  function insertEmoji(e: string) {
    const el = inputRef.current;
    if (!el) { setBody(body + e); return; }
    const start = el.selectionStart ?? body.length;
    const end = el.selectionEnd ?? body.length;
    const next = body.slice(0, start) + e + body.slice(end);
    setBody(next);
    requestAnimationFrame(() => {
      el.focus();
      const pos = start + e.length;
      el.setSelectionRange(pos, pos);
    });
  }

  return (
    <form onSubmit={onSend} className="border-t hairline bg-white p-3 flex items-end gap-2 shrink-0 relative">
      <input
        ref={fileRef}
        type="file"
        className="hidden"
        onChange={(e) => {
          const f = e.target.files?.[0];
          if (f) onAttach(f);
          e.target.value = "";
        }}
      />
      <Button
        type="button" variant="ghost" size="icon" aria-label="Attach file"
        onClick={() => fileRef.current?.click()}
        loading={uploading}
        title={`Attach a file (max ${maxBytes / (1024 * 1024)} MB)`}
      >
        <Icon name="attach" size={18} />
      </Button>
      <Button
        type="button" variant="ghost" size="icon" aria-label="Insert emoji"
        onClick={() => setShowEmoji((v) => !v)}
      >
        <Icon name="smile" size={18} />
      </Button>

      {showEmoji && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setShowEmoji(false)} />
          <div className="absolute bottom-16 left-3 z-20 bg-white border hairline rounded-xl shadow-lg p-2 grid grid-cols-8 gap-1 w-[280px]">
            {EMOJIS.map((e) => (
              <button
                key={e} type="button"
                className="h-8 w-8 grid place-items-center text-lg hover:bg-ink-100 rounded-lg transition-colors"
                onClick={() => { insertEmoji(e); }}
              >{e}</button>
            ))}
          </div>
        </>
      )}

      <input
        ref={inputRef}
        className="input-base h-11 flex-1"
        placeholder="Type a message…  (Enter to send)"
        value={body}
        onChange={(e) => { setBody(e.target.value); onTyping?.(); }}
      />
      <Button
        type="submit" size="lg" loading={sending}
        disabled={!body.trim() || uploading}
        leftIcon={!sending ? <Icon name="arrowRight" size={16} /> : undefined}
      >
        Send
      </Button>
    </form>
  );
}

function ConnectionStatus({ state }: { state: "connecting" | "connected" | "disconnected" }) {
  const config = {
    connected:    { tone: "success" as const, label: "You're online",   pulse: true },
    connecting:   { tone: "warning" as const, label: "Connecting…",     pulse: true },
    disconnected: { tone: "danger" as const,  label: "You're offline",  pulse: false },
  }[state];
  const dotColor = state === "connected" ? "bg-emerald-500" : state === "connecting" ? "bg-amber-500" : "bg-rose-500";
  return (
    <div className="flex items-center gap-2 text-xs text-ink-600">
      <span className="relative flex h-2 w-2">
        {config.pulse && <span className={cn("absolute inline-flex h-full w-full rounded-full opacity-60 animate-ping", dotColor)} />}
        <span className={cn("relative inline-flex rounded-full h-2 w-2", dotColor)} />
      </span>
      <span>{config.label}</span>
    </div>
  );
}

function NewRoomModal({
  open, onClose, users, loading, onCreate,
}: {
  open: boolean;
  onClose: () => void;
  users: { id: string; userName: string }[];
  loading: boolean;
  onCreate: (name: string, memberIds: string[]) => void;
}) {
  const [name, setName] = useState("");
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [filter, setFilter] = useState("");

  useEffect(() => {
    if (open) { setName(""); setPicked(new Set()); setFilter(""); }
  }, [open]);

  const filtered = users.filter((u) =>
    !filter || u.userName.toLowerCase().includes(filter.toLowerCase()),
  );

  const toggle = (id: string) =>
    setPicked((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="New conversation"
      description="Pick the team members you'd like to include."
      size="lg"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button
            loading={loading}
            disabled={!name.trim() || picked.size === 0}
            onClick={() => onCreate(name.trim(), Array.from(picked))}
          >
            Create {picked.size > 0 ? `with ${picked.size}` : ""}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Input
          label="Room name" required
          placeholder="e.g. ACA Closers, Daily standup"
          value={name} onChange={(e) => setName(e.target.value)}
          autoFocus
        />
        <div>
          <label className="text-xs font-medium text-ink-700 mb-1.5 block">Members</label>
          <Input
            leftIcon={<Icon name="search" size={14} />}
            placeholder="Filter teammates..."
            value={filter} onChange={(e) => setFilter(e.target.value)}
            className="mb-2"
          />
          <div className="max-h-64 overflow-y-auto rounded-lg border hairline divide-y divide-ink-100">
            {filtered.length === 0 ? (
              <div className="p-6 text-center text-sm text-ink-500">No teammates match.</div>
            ) : filtered.map((u) => {
              const active = picked.has(u.id);
              return (
                <button
                  key={u.id}
                  type="button"
                  onClick={() => toggle(u.id)}
                  className={cn(
                    "w-full flex items-center gap-3 px-3 py-2.5 text-left hover:bg-ink-50 transition-colors",
                    active && "bg-brand-50/60",
                  )}
                >
                  <Avatar name={u.userName} size={32} />
                  <span className="flex-1 text-sm text-ink-800">{u.userName}</span>
                  <span className={cn(
                    "h-5 w-5 rounded border grid place-items-center transition-colors",
                    active ? "border-brand-600 bg-brand-600 text-white" : "border-ink-300 bg-white",
                  )}>
                    {active && <Icon name="check" size={12} />}
                  </span>
                </button>
              );
            })}
          </div>
          {picked.size > 0 && (
            <p className="text-xs text-ink-500 mt-2">{picked.size} selected</p>
          )}
        </div>
      </div>
    </Modal>
  );
}
