import { useState } from "react";
import {
  useChatOversightAgenciesQuery, useChatOversightCallCentersQuery,
  useChatOversightRoomsQuery, useChatOversightMessagesQuery,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, Input, PageHeader, Skeleton, cn,
} from "../../shared/ui";

const AGENCY_WIDE = "00000000-0000-0000-0000-000000000000";

/**
 * SuperAdmin chat oversight — read-only, cross-agency. A drill-down:
 *   Agencies → Call centers (of an agency) → Chats → transcript.
 * Chat rooms are agency-level, so a "call center" groups rooms by the call center of their members;
 * the "Agency-wide" bucket holds rooms with agency-level members.
 */
export function ChatOversightPage() {
  const [agency, setAgency] = useState<{ id: string; name: string } | null>(null);
  const [callCenter, setCallCenter] = useState<{ id: string; name: string } | null>(null);

  return (
    <>
      <PageHeader
        eyebrow="Oversight"
        title="Chat oversight"
        description="Read-only view of every chat conversation across all agencies. Visible to SuperAdmin only."
        badge={<Badge tone="brand" variant="soft"><Icon name="shield" size={11} className="-ml-0.5" /> SuperAdmin</Badge>}
      />

      {/* Breadcrumb trail */}
      <div className="flex items-center gap-1.5 text-sm mb-4 flex-wrap">
        <Crumb label="All agencies" active={!agency} onClick={() => { setAgency(null); setCallCenter(null); }} />
        {agency && (
          <>
            <Icon name="chevronRight" size={13} className="text-ink-400" />
            <Crumb label={agency.name} active={!callCenter} onClick={() => setCallCenter(null)} />
          </>
        )}
        {agency && callCenter && (
          <>
            <Icon name="chevronRight" size={13} className="text-ink-400" />
            <Crumb label={callCenter.name} active onClick={() => {}} />
          </>
        )}
      </div>

      {!agency ? (
        <AgencyGrid onPick={(a) => { setAgency(a); setCallCenter(null); }} />
      ) : !callCenter ? (
        <CallCenterGrid
          agency={agency}
          onPick={(c) => setCallCenter(c)}
          onAllChats={() => setCallCenter({ id: "all", name: "All chats" })}
        />
      ) : (
        <RoomBrowser agencyId={agency.id} callCenter={callCenter} />
      )}
    </>
  );
}

function Crumb({ label, active, onClick }: { label: string; active?: boolean; onClick: () => void }) {
  return (
    <button
      type="button" onClick={onClick} disabled={active}
      className={cn("px-2 py-1 rounded-md font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400",
        active ? "text-ink-900" : "text-brand-600 hover:bg-brand-50")}
    >{label}</button>
  );
}

function StatCard({ icon, title, subtitle, count, onClick }: {
  icon: string; title: string; subtitle: string; count: number; onClick: () => void;
}) {
  return (
    <button
      type="button" onClick={onClick}
      className="surface p-4 text-left hover:shadow-card-hover hover:border-brand-200 transition-all flex items-center gap-3 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
    >
      <div className="h-11 w-11 rounded-xl bg-brand-50 text-brand-600 grid place-items-center shrink-0">
        <Icon name={icon} size={20} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="font-semibold text-ink-900 truncate">{title}</div>
        <div className="text-xs text-ink-500 tabular-nums truncate">{subtitle}</div>
      </div>
      <Badge tone={count > 0 ? "brand" : "neutral"} variant="soft" className="tabular-nums">{count}</Badge>
      <Icon name="chevronRight" size={16} className="text-ink-300 shrink-0" />
    </button>
  );
}

function Empty({ title, body, icon = "chat" }: { title: string; body: string; icon?: string }) {
  return (
    <Card><CardBody>
      <EmptyState icon={<Icon name={icon} size={20} />} title={title} description={body} tone="neutral" />
    </CardBody></Card>
  );
}

function AgencyGrid({ onPick }: { onPick: (a: { id: string; name: string }) => void }) {
  const { data, isLoading } = useChatOversightAgenciesQuery();
  if (isLoading) return <Grid>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-[76px] rounded-xl" />)}</Grid>;
  if (!data || data.length === 0) return <Empty icon="building" title="No agencies" body="No agencies exist yet." />;
  return (
    <Grid>
      {data.map((a) => (
        <StatCard key={a.id} icon="building" title={a.name}
          subtitle={`${a.roomCount} chat ${a.roomCount === 1 ? "room" : "rooms"}`}
          count={a.roomCount} onClick={() => onPick({ id: a.id, name: a.name })} />
      ))}
    </Grid>
  );
}

function CallCenterGrid({ agency, onPick, onAllChats }: {
  agency: { id: string; name: string };
  onPick: (c: { id: string; name: string }) => void;
  onAllChats: () => void;
}) {
  const { data, isLoading } = useChatOversightCallCentersQuery(agency.id);
  return (
    <>
      <div className="mb-3">
        <Button variant="outline" size="sm" leftIcon={<Icon name="chat" size={14} />} onClick={onAllChats}>
          View all chats in {agency.name}
        </Button>
      </div>
      {isLoading ? (
        <Grid>{[0, 1, 2].map((i) => <Skeleton key={i} className="h-[76px] rounded-xl" />)}</Grid>
      ) : !data || data.length === 0 ? (
        <Empty icon="headset" title="No call centers with chats" body={`No conversations grouped by call center in ${agency.name}. Use "View all chats" above.`} />
      ) : (
        <Grid>
          {data.map((c) => (
            <StatCard key={c.id} icon={c.id === AGENCY_WIDE ? "users" : "headset"} title={c.name}
              subtitle={`${c.roomCount} chat ${c.roomCount === 1 ? "room" : "rooms"}`}
              count={c.roomCount} onClick={() => onPick({ id: c.id, name: c.name })} />
          ))}
        </Grid>
      )}
    </>
  );
}

function RoomBrowser({ agencyId, callCenter }: {
  agencyId: string; callCenter: { id: string; name: string };
}) {
  const [q, setQ] = useState("");
  const [roomId, setRoomId] = useState<string | null>(null);
  const roomsArg = callCenter.id === "all"
    ? { agencyId }
    : { agencyId, callCenterId: callCenter.id };
  const { data: rooms, isLoading } = useChatOversightRoomsQuery(roomsArg);
  const { data: messages, isFetching: msgLoading } = useChatOversightMessagesQuery(roomId ?? "", { skip: !roomId });

  const filtered = (rooms ?? []).filter((r) => !q.trim() ||
    `${r.name} ${r.members.join(" ")}`.toLowerCase().includes(q.trim().toLowerCase()));
  const activeRoom = rooms?.find((r) => r.id === roomId);
  const roomTitle = (r?: { isDirect: boolean; name: string; members: string[] }) =>
    !r ? "" : r.isDirect ? (r.members.join(" · ") || r.name) : r.name;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-4">
      <Card className="lg:h-[68vh] flex flex-col overflow-hidden">
        <div className="p-3 border-b hairline">
          <Input placeholder="Search rooms or people…" leftIcon={<Icon name="search" size={14} />} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <div className="flex-1 overflow-y-auto">
          {isLoading ? (
            <div className="p-3 space-y-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-14" />)}</div>
          ) : filtered.length === 0 ? (
            <div className="p-6 text-center text-sm text-ink-500">
              {q ? "No rooms match your search." : "No conversations here."}
            </div>
          ) : filtered.map((r) => (
            <button
              key={r.id} onClick={() => setRoomId(r.id)}
              className={cn("w-full text-left px-3 py-2.5 border-b hairline hover:bg-brand-50/40 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-400", roomId === r.id && "bg-brand-50/60")}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium text-ink-900 truncate">{roomTitle(r)}</span>
                <span className="text-[11px] text-ink-400 shrink-0 tabular-nums whitespace-nowrap">{r.lastMessageAt ? new Date(r.lastMessageAt).toLocaleDateString() : "—"}</span>
              </div>
              <div className="text-xs text-ink-500 mt-0.5 tabular-nums">{r.messageCount} msg · {r.members.length} people</div>
            </button>
          ))}
        </div>
      </Card>

      <Card className="lg:h-[68vh] flex flex-col overflow-hidden">
        {!roomId ? (
          <div className="flex-1 grid place-items-center text-center px-6">
            <div>
              <div className="mx-auto mb-3 h-12 w-12 rounded-2xl bg-ink-100 grid place-items-center text-ink-400"><Icon name="chat" size={22} /></div>
              <div className="font-semibold text-ink-900">Select a conversation</div>
              <div className="text-sm text-ink-500 mt-1">Pick a room on the left to read its full transcript.</div>
            </div>
          </div>
        ) : (
          <>
            <div className="px-4 py-3 border-b hairline">
              <div className="font-semibold text-ink-900">{roomTitle(activeRoom)}</div>
              <div className="text-xs text-ink-500 truncate">{activeRoom?.members.join(", ")}</div>
            </div>
            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {msgLoading ? (
                [0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)
              ) : (messages ?? []).length === 0 ? (
                <div className="text-center text-sm text-ink-500 py-8">This room has no messages yet.</div>
              ) : (messages ?? []).map((m) => (
                <div key={m.id} className="flex gap-2.5">
                  <Avatar name={m.sender} size={30} />
                  <div className="min-w-0">
                    <div className="flex items-baseline gap-2">
                      <span className="font-medium text-ink-900 text-sm truncate">{m.sender}</span>
                      <span className="text-[11px] text-ink-400 tabular-nums whitespace-nowrap shrink-0">{new Date(m.sentAt).toLocaleString()}</span>
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
  );
}

function Grid({ children }: { children: React.ReactNode }) {
  return <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">{children}</div>;
}
