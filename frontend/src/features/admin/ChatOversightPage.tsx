import { useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useChatOversightRoomsQuery, useChatOversightMessagesQuery, useAgencyOptionsQuery,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, Card, EmptyState, Icon, Input, PageHeader, Select, Skeleton, cn,
} from "../../shared/ui";

/**
 * SuperAdmin chat oversight — read-only view of every conversation across all
 * agencies. Left pane lists rooms (searchable, agency-filterable); right pane
 * shows the selected room's full transcript.
 */
export function ChatOversightPage() {
  const isSuperAdmin = useSelector((s: RootState) => s.auth.user?.roles?.includes("SuperAdmin") ?? false);
  const { data: agencyOptions } = useAgencyOptionsQuery(undefined, { skip: !isSuperAdmin });
  const [agencyId, setAgencyId] = useState("");
  const [q, setQ] = useState("");
  const [roomId, setRoomId] = useState<string | null>(null);

  const { data: rooms, isLoading } = useChatOversightRoomsQuery(agencyId ? { agencyId } : undefined);
  const { data: messages, isFetching: msgLoading } = useChatOversightMessagesQuery(roomId ?? "", { skip: !roomId });

  const filtered = (rooms ?? []).filter((r) => !q.trim() ||
    `${r.name} ${r.agencyName} ${r.members.join(" ")}`.toLowerCase().includes(q.trim().toLowerCase()));
  const activeRoom = rooms?.find((r) => r.id === roomId);
  const roomTitle = (r?: { isDirect: boolean; name: string; members: string[] }) =>
    !r ? "" : r.isDirect ? (r.members.join(" · ") || r.name) : r.name;

  return (
    <>
      <PageHeader
        eyebrow="Oversight"
        title="Chat oversight"
        description="Read-only view of every chat conversation across all agencies. Visible to SuperAdmin only."
        badge={<Badge tone="brand" variant="soft"><Icon name="shield" size={11} className="-ml-0.5" /> SuperAdmin</Badge>}
        actions={
          <Select aria-label="Agency" value={agencyId} onChange={(e) => { setAgencyId(e.target.value); setRoomId(null); }} className="w-52">
            <option value="">All agencies</option>
            {(agencyOptions ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </Select>
        }
      />

      <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-4">
        {/* Room list */}
        <Card className="lg:h-[70vh] flex flex-col overflow-hidden">
          <div className="p-3 border-b hairline">
            <Input placeholder="Search rooms, people, agency…" leftIcon={<Icon name="search" size={14} />} value={q} onChange={(e) => setQ(e.target.value)} />
          </div>
          <div className="flex-1 overflow-y-auto">
            {isLoading ? (
              <div className="p-3 space-y-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-14" />)}</div>
            ) : filtered.length === 0 ? (
              <div className="p-4"><EmptyState icon={<Icon name="chat" size={20} />} title="No conversations" description={q ? "No matches." : "No chat rooms yet."} /></div>
            ) : filtered.map((r) => (
              <button
                key={r.id} onClick={() => setRoomId(r.id)}
                className={cn("w-full text-left px-3 py-2.5 border-b hairline hover:bg-brand-50/40 transition-colors", roomId === r.id && "bg-brand-50/60")}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium text-ink-900 truncate">{roomTitle(r)}</span>
                  <span className="text-[11px] text-ink-400 shrink-0">{r.lastMessageAt ? new Date(r.lastMessageAt).toLocaleDateString() : "—"}</span>
                </div>
                <div className="flex items-center gap-2 mt-0.5">
                  <Badge tone="neutral" variant="soft">{r.agencyName}</Badge>
                  <span className="text-xs text-ink-500">{r.messageCount} msg · {r.members.length} people</span>
                </div>
              </button>
            ))}
          </div>
        </Card>

        {/* Transcript */}
        <Card className="lg:h-[70vh] flex flex-col overflow-hidden">
          {!roomId ? (
            <div className="flex-1 grid place-items-center">
              <EmptyState icon={<Icon name="chat" size={24} />} title="Select a conversation" description="Pick a room on the left to read its full transcript." />
            </div>
          ) : (
            <>
              <div className="px-4 py-3 border-b hairline">
                <div className="font-semibold text-ink-900">{roomTitle(activeRoom)}</div>
                <div className="text-xs text-ink-500 truncate">{activeRoom?.agencyName} · {activeRoom?.members.join(", ")}</div>
              </div>
              <div className="flex-1 overflow-y-auto p-4 space-y-3">
                {msgLoading ? (
                  [0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)
                ) : (messages ?? []).length === 0 ? (
                  <EmptyState icon={<Icon name="chat" size={20} />} title="No messages" description="This room has no messages yet." />
                ) : (messages ?? []).map((m) => (
                  <div key={m.id} className="flex gap-2.5">
                    <Avatar name={m.sender} size={30} />
                    <div className="min-w-0">
                      <div className="flex items-baseline gap-2">
                        <span className="font-medium text-ink-900 text-sm">{m.sender}</span>
                        <span className="text-[11px] text-ink-400">{new Date(m.sentAt).toLocaleString()}</span>
                      </div>
                      <div className="text-sm text-ink-700 whitespace-pre-wrap break-words">{m.body}</div>
                      {m.attachmentName && (
                        <div className="text-xs text-brand-600 mt-0.5 inline-flex items-center gap-1"><Icon name="file" size={12} /> {m.attachmentName}</div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
        </Card>
      </div>
    </>
  );
}
