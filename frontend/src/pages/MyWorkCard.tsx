import { useQueueCountsQuery } from "../shared/api/baseApi";
import { Card, CardBody, Icon, Skeleton, Stat } from "../shared/ui";
import { WORK_MSG } from "./workMessages";

/**
 * "What do I need to work on?" — answered above everything else on the dashboard.
 *
 * The dashboard opened on KPI charts and a floor wallboard: useful to a supervisor, useless to
 * someone who just signed in wanting to know where to start. These are the only four numbers that
 * lead to an action, each one a link straight to that work. Anything at zero is dropped rather than
 * shown as an empty tile, so the section is never padded with things you do not have to do.
 */
export function MyWorkCard() {
  const { data, isLoading } = useQueueCountsQuery(undefined, { pollingInterval: 30_000 });

  const tiles = [
    {
      key: "mine",
      label: WORK_MSG.myLeads,
      value: data?.myLeads ?? 0,
      hint: WORK_MSG.myLeadsHint,
      icon: "inbox" as const,
      tone: "brand" as const,
      to: "/queue",
      // Always shown, even at zero: "you have nothing assigned" is itself the answer someone
      // signing in needs, and its empty state points them at Available Leads.
      always: true,
    },
    {
      key: "available",
      label: WORK_MSG.availableLeads,
      value: data?.available ?? 0,
      hint: WORK_MSG.availableHint,
      icon: "briefcase" as const,
      tone: "accent" as const,
      to: "/available",
      always: false,
    },
    {
      key: "callbacks",
      label: WORK_MSG.callbacksDue,
      value: data?.callbacks ?? 0,
      hint: WORK_MSG.callbacksHint,
      icon: "clock" as const,
      tone: "warning" as const,
      to: "/callbacks",
      always: false,
    },
    {
      key: "submissions",
      label: WORK_MSG.submissions,
      value: data?.submissionQueue ?? 0,
      hint: WORK_MSG.submissionsHint,
      icon: "shield" as const,
      tone: "success" as const,
      to: "/validate-queue",
      always: false,
    },
  ].filter((t) => t.always || t.value > 0);

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 mb-6">
        {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-[104px] rounded-2xl" />)}
      </div>
    );
  }

  return (
    <section aria-labelledby="my-work-heading" className="mb-6">
      <h2
        id="my-work-heading"
        className="text-xs font-semibold uppercase tracking-wider text-ink-500 mb-2"
      >
        {WORK_MSG.sectionTitle}
      </h2>

      {tiles.length === 1 && tiles[0].value === 0 ? (
        // Nothing anywhere — say so plainly and point at the one place work comes from.
        <Card>
          <CardBody className="flex items-center gap-3 py-5">
            <div className="h-10 w-10 rounded-xl bg-emerald-50 text-emerald-700 grid place-items-center shrink-0">
              <Icon name="check" size={18} />
            </div>
            <div className="min-w-0">
              <div className="font-semibold text-ink-900">{WORK_MSG.allClearTitle}</div>
              <p className="text-sm text-ink-500 mt-0.5">{WORK_MSG.allClearBody}</p>
            </div>
          </CardBody>
        </Card>
      ) : (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
          {tiles.map((t) => (
            <Stat
              key={t.key}
              label={t.label}
              value={t.value}
              hint={t.hint}
              tone={t.tone}
              to={t.to}
              icon={<Icon name={t.icon} size={16} />}
            />
          ))}
        </div>
      )}
    </section>
  );
}
