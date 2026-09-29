import { Button, Icon } from "../ui";
import { useNewVersionAvailable } from "../hooks/useBuildVersion";
import { APP_MSG } from "../constants/messages";

/**
 * Tells someone their tab is running an old copy of the app, and reloads it for them.
 *
 * Deliberately a prompt rather than a silent automatic reload: an agent may be mid-call with a
 * half-filled closing application on screen, and throwing that away to pick up a newer build would
 * be a worse bug than the one being fixed. So it asks, stays until acted on, and gets out of the
 * way of the work underneath.
 */
export function NewVersionBanner() {
  const stale = useNewVersionAvailable();
  if (!stale) return null;

  return (
    <div
      role="status"
      className="sticky top-16 z-40 mx-4 mt-3 flex flex-wrap items-center gap-3 rounded-xl border border-brand-200 bg-brand-50 px-4 py-2.5 shadow-sm sm:mx-6"
    >
      <span className="grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-brand-100 text-brand-700">
        <Icon name="refresh" size={15} />
      </span>
      <div className="min-w-0 flex-1">
        <div className="text-sm font-medium text-ink-900">{APP_MSG.newVersionTitle}</div>
        <div className="text-xs text-ink-600">{APP_MSG.newVersionBody}</div>
      </div>
      <Button size="sm" onClick={() => window.location.reload()}>
        {APP_MSG.newVersionAction}
      </Button>
    </div>
  );
}
