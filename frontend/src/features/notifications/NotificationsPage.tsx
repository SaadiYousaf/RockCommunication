import { useNavigate } from "react-router-dom";
import {
  useNotificationsQuery, useMarkNotificationReadMutation, useMarkAllNotificationsReadMutation,
} from "../../shared/api/baseApi";
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, PageHeader, Skeleton, cn } from "../../shared/ui";

/**
 * Full notification inbox — every work-assignment / pipeline alert for the signed-in user,
 * beyond the last few shown in the header bell. Click an item to mark it read and jump to it.
 */
export function NotificationsPage() {
  const { data: notifs = [], isLoading } = useNotificationsQuery({ take: 100 });
  const [markRead] = useMarkNotificationReadMutation();
  const [markAll] = useMarkAllNotificationsReadMutation();
  const navigate = useNavigate();
  const unread = notifs.filter((n) => !n.isRead).length;

  async function open(id: string, url: string | null, isRead: boolean) {
    if (!isRead) { try { await markRead(id).unwrap(); } catch { /* best-effort */ } }
    if (url) navigate(url);
  }

  return (
    <>
      <PageHeader
        title="Notifications"
        description="Every alert about work assigned to you and leads moving into your queue."
      />
      <Card>
        <CardHeader
          title="All notifications"
          subtitle={notifs.length ? `${unread} unread · ${notifs.length} total` : undefined}
          action={unread > 0 ? (
            <Button size="sm" variant="outline" leftIcon={<Icon name="check" size={14} />}
              onClick={async () => { try { await markAll().unwrap(); } catch { /* best-effort */ } }}>
              Mark all read
            </Button>
          ) : undefined}
        />
        <CardBody className="pt-0">
          {isLoading ? (
            <div className="space-y-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-14" />)}</div>
          ) : notifs.length === 0 ? (
            <EmptyState
              icon={<Icon name="bell" size={20} />}
              title="No notifications yet"
              description="When work is assigned to you or a lead moves into your queue, it'll show up here."
            />
          ) : (
            <div className="divide-y divide-ink-100">
              {notifs.map((n) => (
                <button
                  key={n.id}
                  onClick={() => open(n.id, n.url, n.isRead)}
                  className={cn(
                    "w-full flex items-start gap-3 py-3 px-2 -mx-2 rounded-lg text-left transition-colors hover:bg-ink-50/70",
                    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40",
                    !n.isRead && "bg-brand-50/30",
                  )}
                >
                  <span className="mt-0.5 h-9 w-9 rounded-full bg-brand-50 text-brand-600 grid place-items-center shrink-0">
                    <Icon name="bell" size={16} />
                  </span>
                  <div className="flex-1 min-w-0">
                    <div className={cn("text-sm", n.isRead ? "text-ink-700" : "font-semibold text-ink-900")}>{n.title}</div>
                    <div className="text-sm text-ink-600">{n.body}</div>
                    <div className="text-xs text-ink-400 mt-0.5 tabular-nums">{new Date(n.createdAt).toLocaleString()}</div>
                  </div>
                  {!n.isRead && <Badge tone="brand" variant="soft" size="sm">New</Badge>}
                </button>
              ))}
            </div>
          )}
        </CardBody>
      </Card>
    </>
  );
}
