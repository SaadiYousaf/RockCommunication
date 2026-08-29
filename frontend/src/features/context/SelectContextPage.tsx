import { useMemo, useState } from "react";
import { useDispatch, useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import { getErrorDetail } from "../../shared/api/apiError";
import { setAuth } from "../../app/store";
import type { RootState } from "../../app/store";
import {
  useSetContextMutation, useListCallCentersQuery, useListAgenciesQuery,
  useAgencyCallCentersQuery, useListUsersQuery,
} from "../../shared/api/baseApi";
import { roleLabel } from "../../shared/constants/roles";
import { MESSAGES } from "../../shared/constants/messages";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, ErrorState, Icon, Skeleton, Stepper, cn, useToast,
} from "../../shared/ui";
import { BrandLogo } from "../../shared/components/BrandLogo";
import { CONTEXT_MSG } from "./messages";

type StepKey = "agency" | "center" | "roster";

/**
 * Admin context picker — the post-login interstitial where an admin chooses the slice of the org
 * to work in, then the app hard-scopes to it (server re-issues a scoped session).
 *   Super Admin: agency → call center → roster → Enter
 *   Agency Admin: call center → roster → Enter
 *   Call Center Admin: their center is pre-selected → roster → Enter
 * "All" choices keep the wider (agency-wide / platform) view. Not gated by Layout chrome.
 */
export function SelectContextPage() {
  const auth = useSelector((s: RootState) => s.auth);
  const user = auth.user;
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const toast = useToast();
  const [setContext, { isLoading: entering }] = useSetContextMutation();

  const roles = user?.roles ?? [];
  const isSuperAdmin = roles.includes("SuperAdmin");
  const pinnedCallCenter = user?.callCenterId ?? null;   // a Call Center Admin is pinned to one

  const flow: StepKey[] = isSuperAdmin
    ? ["agency", "center", "roster"]
    : pinnedCallCenter
      ? ["roster"]
      : ["center", "roster"];

  const [stepIdx, setStepIdx] = useState(0);
  const step = flow[stepIdx];

  // Selection so far. `agencyId` only for Super Admin; `callCenterId` = null means "all call centers".
  const [agencyId, setAgencyId] = useState<string | null>(null);
  const [agencyLabel, setAgencyLabel] = useState<string>("");
  const [callCenterId, setCallCenterId] = useState<string | null>(pinnedCallCenter);
  const [callCenterLabel, setCallCenterLabel] = useState<string>(user?.callCenterName ?? "");

  // Enter the chosen workspace. `destination` lets a clicked roster member land straight on their
  // profile (the scope is set first, so the app is already narrowed when the profile opens).
  async function enter(destination: string = "/dashboard") {
    try {
      const res = await setContext({
        agencyId: isSuperAdmin ? agencyId : undefined,
        callCenterId: pinnedCallCenter ?? callCenterId,
      }).unwrap();
      dispatch(setAuth({
        accessToken: res.accessToken, refreshToken: res.refreshToken, user: res.user, contextChosen: true,
      }));
      navigate(destination);
    } catch (err: unknown) {
      toast.error(CONTEXT_MSG.enterFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  const stepperSteps = flow.map((s) => ({
    key: s, label: s === "agency" ? "Agency" : s === "center" ? "Call center" : "Team",
  }));

  return (
    <div className="min-h-screen bg-ink-50/60 flex flex-col items-center px-4 py-10">
      <div className="w-full max-w-3xl">
        <div className="flex items-center justify-between mb-6">
          <BrandLogo />
          <div className="text-right">
            <div className="text-sm font-medium text-ink-800">{user?.userName}</div>
            <div className="text-xs text-ink-500">{roleLabel(roles[0] ?? "")}</div>
          </div>
        </div>

        <Card className="animate-rise">
          <CardBody className="space-y-5">
            {stepIdx > 0 && (
              <button type="button" onClick={() => setStepIdx((i) => Math.max(0, i - 1))}
                className="inline-flex items-center gap-1 -mt-1 -ml-1 text-sm font-medium text-ink-600 hover:text-ink-900 transition-colors">
                <Icon name="chevronLeft" size={16} /> Back
              </button>
            )}
            <div>
              <div className="section-title mb-1.5 flex items-center gap-1.5">
                <span aria-hidden className="inline-block h-1 w-1 rounded-full bg-brand-500" /> Choose your workspace
              </div>
              <h1 className="text-2xl font-semibold tracking-tight text-ink-900">
                {step === "agency" ? "Which agency?" : step === "center" ? "Which call center?" : "You're all set"}
              </h1>
              <p className="text-sm text-ink-500 mt-1">
                {step === "agency" ? "Pick an agency to work in, or view the whole platform."
                  : step === "center" ? "Pick a call center to focus on, or work across the whole agency."
                    : "Review the team, then enter the workspace. The app will work within this scope."}
              </p>
            </div>

            {flow.length > 1 && <Stepper steps={stepperSteps} currentIndex={stepIdx} />}

            {step === "agency" && (
              <AgencyStep onPick={(id, label) => { setAgencyId(id); setAgencyLabel(label); setStepIdx((i) => i + 1); }} />
            )}
            {step === "center" && (
              <CenterStep isSuperAdmin={isSuperAdmin} agencyId={agencyId}
                onPick={(id, label) => { setCallCenterId(id); setCallCenterLabel(label); setStepIdx((i) => i + 1); }} />
            )}
            {step === "roster" && (
              <RosterStep
                agencyId={isSuperAdmin ? agencyId : (user?.agencyId ?? null)}
                callCenterId={callCenterId}
                scopeLabel={callCenterId ? callCenterLabel : (isSuperAdmin && agencyId ? `${agencyLabel} · all call centers` : "All call centers")}
                entering={entering}
                onEnter={enter}
              />
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

/** Super-Admin step 1: pick an agency (or all agencies / platform). */
function AgencyStep({ onPick }: { onPick: (id: string | null, label: string) => void }) {
  const { data: agencies, isLoading } = useListAgenciesQuery();
  if (isLoading) return <StepSkeleton />;
  return (
    <div className="space-y-2">
      <PickRow icon="globe" title="All agencies" subtitle="Platform-wide view across every agency" onClick={() => onPick(null, "All agencies")} />
      {(agencies ?? []).map((a) => (
        <PickRow key={a.id} icon="building" title={a.name} subtitle={a.isActive ? `${a.userCount} users` : "Inactive"}
          disabled={!a.isActive} onClick={() => onPick(a.id, a.name)} />
      ))}
    </div>
  );
}

/** Step 2: pick a call center within the (chosen or own) agency, or all call centers. */
function CenterStep({ isSuperAdmin, agencyId, onPick }: {
  isSuperAdmin: boolean; agencyId: string | null; onPick: (id: string | null, label: string) => void;
}) {
  const own = useListCallCentersQuery(undefined, { skip: isSuperAdmin });
  const byAgency = useAgencyCallCentersQuery(agencyId ?? "", { skip: !isSuperAdmin || !agencyId });
  const data = isSuperAdmin ? byAgency.data : own.data;
  const isLoading = isSuperAdmin ? byAgency.isLoading : own.isLoading;
  if (isLoading) return <StepSkeleton />;
  return (
    <div className="space-y-2">
      <PickRow icon="building" title="All call centers" subtitle="Work across the whole agency" onClick={() => onPick(null, "All call centers")} />
      {(data ?? []).length === 0 && <p className="text-sm text-ink-400 px-1">No call centers yet.</p>}
      {(data ?? []).map((c) => (
        <PickRow key={c.id} icon="headset" title={c.name} subtitle={c.isActive ? `${c.leadCount} leads` : "Inactive"}
          disabled={!c.isActive} onClick={() => onPick(c.id, c.name)} />
      ))}
    </div>
  );
}

/** Final step: roster of the chosen scope. Click a member to open their profile; or Enter the workspace. */
function RosterStep({ agencyId, callCenterId, scopeLabel, entering, onEnter }: {
  agencyId: string | null; callCenterId: string | null; scopeLabel: string;
  entering: boolean; onEnter: (destination?: string) => void;
}) {
  const { data: users, isLoading, isError, error, refetch } = useListUsersQuery(agencyId ? { agencyId } : undefined);
  const roster = useMemo(
    () => (users ?? []).filter((u) => (callCenterId ? u.callCenterId === callCenterId : true)),
    [users, callCenterId],
  );

  return (
    <div className="space-y-4">
      <div className="rounded-xl border border-brand-200 bg-brand-50/60 px-3.5 py-2.5 flex items-center gap-2">
        <Icon name="target" size={15} className="text-brand-600" />
        <span className="text-sm text-ink-800">Entering <span className="font-semibold">{scopeLabel}</span></span>
        <Badge tone="brand" variant="soft" className="ml-auto">{roster.length} {roster.length === 1 ? "member" : "members"}</Badge>
      </div>

      {isLoading ? (
        <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)}</div>
      ) : isError ? (
        // A failed roster load used to read as "no members yet", which makes a healthy call centre
        // look empty at the very moment someone is deciding where to work.
        <ErrorState error={error} resource={CONTEXT_MSG.rosterResourceName} onRetry={refetch} />
      ) : roster.length === 0 ? (
        <EmptyState icon={<Icon name="users" size={20} />} title={CONTEXT_MSG.noMembersTitle}
          description={CONTEXT_MSG.noMembersDesc} />
      ) : (
        <div className="max-h-72 overflow-y-auto -mx-1 px-1 space-y-1.5">
          {roster.map((u) => (
            <button key={u.id} type="button" disabled={entering}
              onClick={() => onEnter(`/profile/${u.id}`)} title={`Open ${u.userName}'s profile`}
              className="group w-full flex items-center gap-3 rounded-xl border hairline px-3 py-2 text-left transition-colors hover:border-brand-300 hover:bg-brand-50/40 disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/30">
              <Avatar name={u.userName} size={32} />
              <div className="min-w-0">
                <div className="text-sm font-medium text-ink-900 truncate">{u.userName}</div>
                <div className="text-xs text-ink-500 truncate">{u.email}</div>
              </div>
              <div className="ml-auto flex items-center gap-1.5">
                {!u.isActive && <Badge tone="neutral" variant="soft">Inactive</Badge>}
                <Badge tone="neutral" variant="soft">{roleLabel(u.roles?.[0] ?? "")}</Badge>
                <Icon name="chevronRight" size={15} className="text-ink-300 group-hover:text-brand-500 transition-colors" />
              </div>
            </button>
          ))}
        </div>
      )}

      <p className="text-xs text-ink-400 -mt-1">Tip: click a teammate to open their profile.</p>

      <div className="flex items-center justify-end pt-1">
        <Button loading={entering} onClick={() => onEnter()} leftIcon={<Icon name="arrowRight" size={15} />}>Enter workspace</Button>
      </div>
    </div>
  );
}

function PickRow({ icon, title, subtitle, onClick, disabled }: {
  icon: Parameters<typeof Icon>[0]["name"]; title: string; subtitle: string; onClick: () => void; disabled?: boolean;
}) {
  return (
    <button type="button" onClick={onClick} disabled={disabled}
      className={cn("group w-full flex items-center gap-3 rounded-xl border hairline px-3.5 py-3 text-left transition-colors",
        disabled ? "opacity-50 cursor-not-allowed" : "hover:border-brand-300 hover:bg-brand-50/40")}>
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-ink-100 text-ink-500 group-hover:bg-brand-100 group-hover:text-brand-600 transition-colors">
        <Icon name={icon} size={16} />
      </span>
      <div className="min-w-0">
        <div className="text-sm font-medium text-ink-900 truncate">{title}</div>
        <div className="text-xs text-ink-500 truncate">{subtitle}</div>
      </div>
      <Icon name="chevronRight" size={16} className="ml-auto text-ink-300 group-hover:text-brand-500 transition-colors" />
    </button>
  );
}

function StepSkeleton() {
  return <div className="space-y-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-14" />)}</div>;
}
