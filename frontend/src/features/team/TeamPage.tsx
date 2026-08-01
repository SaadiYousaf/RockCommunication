import { roleLabel } from "../../shared/constants/roles";
import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useOrgTreeQuery, useSetTeamLeadMutation, useSetUserTeamMutation, useAgencyOptionsQuery,
} from "../../shared/api/baseApi";
import type { OrgPersonDto, OrgTeamDto, OrgTreeDto } from "../../shared/api/types";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, PageHeader,
  Select, Skeleton, Stat, useToast,
} from "../../shared/ui";
import { usePermission, Perm } from "../../shared/auth/permissions";
import { useConfirm } from "../../shared/components/ConfirmDialog";

/**
 * Org-chart view of the agency: CEO → leadership → teams → members.
 * Read-only for now; the data is sourced from `/api/org/tree`.
 */
export function TeamPage() {
  // SuperAdmin has no agency of their own, so the org tree needs a target agency;
  // everyone else is scoped to their own automatically.
  const isSuperAdmin = useSelector((s: RootState) => s.auth.user?.roles?.includes("SuperAdmin") ?? false);
  const { data: agencyOptions } = useAgencyOptionsQuery(undefined, { skip: !isSuperAdmin });
  const [agencyId, setAgencyId] = useState("");
  useEffect(() => {
    if (isSuperAdmin && !agencyId && agencyOptions && agencyOptions.length) setAgencyId(agencyOptions[0].id);
  }, [isSuperAdmin, agencyOptions, agencyId]);

  const { data, isLoading, isError, isFetching, refetch } = useOrgTreeQuery(
    isSuperAdmin ? { agencyId } : undefined,
    { skip: isSuperAdmin && !agencyId },
  );
  const waitingForAgency = isSuperAdmin && !agencyId;
  const [setUserTeam, { isLoading: moving }] = useSetUserTeamMutation();
  const [setTeamLead] = useSetTeamLeadMutation();
  const toast = useToast();
  const canEdit = usePermission(Perm.TeamWrite);

  async function moveToTeam(userId: string, teamId: string | null, label: string) {
    try {
      await setUserTeam({ userId, teamId }).unwrap();
      toast.success(teamId ? `Moved to ${label}` : "Removed from team", "");
    } catch (e) {
      toast.error("Couldn't move user", getErrorDetail(e) ?? "Try again.");
    }
  }

  async function makeLead(teamId: string, userId: string | null, teamName: string) {
    try {
      await setTeamLead({ teamId, userId }).unwrap();
      toast.success(userId ? "Lead assigned" : "Lead cleared", teamName);
    } catch (e) {
      toast.error("Couldn't update lead", getErrorDetail(e) ?? "Try again.");
    }
  }

  const totals = useMemo(() => {
    if (!data) return { teams: 0, members: 0, leaders: 0, unassigned: 0 };
    const teamMembers = data.teams.reduce(
      (sum, t) => sum + (t.lead ? 1 : 0) + t.members.length,
      0,
    );
    return {
      teams: data.teams.length,
      members: teamMembers + data.unassigned.length,
      leaders: data.leadership.length + (data.ceo ? 1 : 0),
      unassigned: data.unassigned.length,
    };
  }, [data]);

  return (
    <>
      <PageHeader
        eyebrow="Organization"
        title="Team"
        description="Hierarchy of your call center — from the CEO down to team members."
        breadcrumbs={[{ label: "Workspace" }, { label: "Team" }]}
        badge={data && <Badge tone="brand" variant="soft">{data.agencyName}</Badge>}
        actions={isSuperAdmin ? (
          <Select aria-label="Agency" value={agencyId} onChange={(e) => setAgencyId(e.target.value)} className="w-56">
            {(!agencyOptions || agencyOptions.length === 0) && <option value="">No agencies</option>}
            {(agencyOptions ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}{a.isActive ? "" : " (inactive)"}</option>)}
          </Select>
        ) : undefined}
      />

      {!canEdit && (
        <div className="mb-4 rounded-lg border border-ink-200 bg-ink-50 px-3 py-2 text-xs text-ink-600 flex items-center gap-2">
          <Icon name="lock" size={14} />
          Read-only — you don't have <code className="px-1 py-0.5 bg-white rounded border border-ink-200">team.write</code>. Ask your CEO to grant it via Role management.
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 mb-5">
        <Stat label="People" value={totals.members + totals.leaders} icon={<Icon name="users" />} tone="brand" hint="Across all teams" />
        <Stat label="Teams" value={totals.teams} icon={<Icon name="briefcase" />} tone="accent" />
        <Stat label="Leadership" value={totals.leaders} icon={<Icon name="shield" />} tone="success" hint="CEO + leaders" />
        <Stat label="Unassigned" value={totals.unassigned} icon={<Icon name="flag" />} tone={totals.unassigned > 0 ? "warning" : "neutral"} hint={totals.unassigned === 0 ? "All placed" : "Needs a team"} />
      </div>

      {waitingForAgency ? (
        <Notice icon="building" title="Pick an agency"
          body={!agencyOptions || agencyOptions.length === 0
            ? "No agencies exist yet. Create one from the Agencies page to see its org chart."
            : "Choose an agency from the selector above to view its organization chart."} />
      ) : isLoading ? (
        <div className="space-y-6">
          <div className="flex justify-center"><Skeleton className="h-28 w-72 rounded-2xl" /></div>
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {[0, 1, 2].map((i) => <Skeleton key={i} className="h-64 rounded-2xl" />)}
          </div>
        </div>
      ) : isError || !data ? (
        <Notice icon="warning" title="Couldn't load the team"
          body="Something went wrong fetching the organization chart. It may be a temporary issue."
          action={<Button variant="outline" size="sm" leftIcon={<Icon name="refresh" size={14} />} onClick={() => refetch()}>Try again</Button>} />
      ) : totals.members + totals.leaders === 0 ? (
        <Notice icon="users" title="No people yet"
          body="This agency has no team members. Invite people from User Management and assign them to teams." />
      ) : (
        <div className="space-y-6" aria-busy={isFetching}>
          <CeoLayer ceo={data.ceo} />
          {(data.ceo || data.leadership.length > 0) && data.teams.length > 0 && <TreeConnector />}
          <LeadershipLayer leaders={data.leadership} />
          {data.leadership.length > 0 && data.teams.length > 0 && <TreeConnector />}
          <TeamsLayer
            teams={data.teams}
            allUsers={collectAllPeople(data)}
            onMove={(userId, teamId, label) => moveToTeam(userId, teamId, label)}
            onMakeLead={(teamId, userId, teamName) => makeLead(teamId, userId, teamName)}
            disabled={moving || !canEdit}
            canEdit={canEdit}
          />
          {data.unassigned.length > 0 && (
            <Card accent="warning">
              <CardHeader
                eyebrow="Needs assignment"
                title="Unassigned members"
                subtitle="Pick a team for each member to place them in the org."
                bordered
                action={<Badge tone="warning" variant="soft">{data.unassigned.length}</Badge>}
              />
              <CardBody>
                <UnassignedList
                  people={data.unassigned}
                  teams={data.teams}
                  onMove={(userId, teamId, label) => moveToTeam(userId, teamId, label)}
                  disabled={moving || !canEdit}
                  canEdit={canEdit}
                />
              </CardBody>
            </Card>
          )}
        </div>
      )}
    </>
  );
}

function collectAllPeople(tree: OrgTreeDto): OrgPersonDto[] {
  const seen = new Map<string, OrgPersonDto>();
  const add = (p: OrgPersonDto | null | undefined) => { if (p && !seen.has(p.id)) seen.set(p.id, p); };
  add(tree.ceo);
  tree.leadership.forEach(add);
  tree.teams.forEach((t) => { add(t.lead); t.members.forEach(add); });
  tree.unassigned.forEach(add);
  return Array.from(seen.values()).sort((a, b) => a.userName.localeCompare(b.userName));
}

// ---- Layers ----------------------------------------------------------------

function CeoLayer({ ceo }: { ceo: OrgPersonDto | null }) {
  if (!ceo) {
    return (
      <Card accent="warning">
        <CardBody className="py-6 text-center">
          <Icon name="shield" size={28} className="mx-auto text-amber-500 mb-2" />
          <div className="text-sm font-semibold text-ink-900">No CEO assigned yet</div>
          <div className="text-xs text-ink-500 mt-1">A SuperAdmin can assign one from the Call Centers page.</div>
        </CardBody>
      </Card>
    );
  }
  return (
    <div className="flex justify-center">
      <PersonCard person={ceo} role="CEO" tone="brand" size="hero" />
    </div>
  );
}

function LeadershipLayer({ leaders }: { leaders: OrgPersonDto[] }) {
  if (leaders.length === 0) return null;
  return (
    <div>
      <LayerLabel><span className="inline-flex items-center gap-1">Leadership<InfoHint title="Leadership" side="bottom">Agency-wide managers (QA, Project, and Program leads) who sit above the individual teams.</InfoHint></span></LayerLabel>
      <div className="flex flex-wrap justify-center gap-3">
        {leaders.map((p) => (
          <PersonCard key={p.id} person={p} role={primaryLeadershipRole(p)} tone="accent" />
        ))}
      </div>
    </div>
  );
}

function TeamsLayer({
  teams, allUsers, onMove, onMakeLead, disabled, canEdit,
}: {
  teams: OrgTeamDto[];
  allUsers: OrgPersonDto[];
  onMove: (userId: string, teamId: string | null, label: string) => void;
  onMakeLead: (teamId: string, userId: string | null, teamName: string) => void;
  disabled?: boolean;
  canEdit: boolean;
}) {
  if (teams.length === 0) return null;
  return (
    <div>
      <LayerLabel>Teams</LayerLabel>
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
        {teams.map((t) => (
          <TeamCard
            key={t.id}
            team={t}
            otherTeams={teams.filter((x) => x.id !== t.id)}
            allUsers={allUsers}
            onMove={onMove}
            onMakeLead={onMakeLead}
            disabled={disabled}
            canEdit={canEdit}
          />
        ))}
      </div>
    </div>
  );
}

function TeamCard({
  team, otherTeams, allUsers, onMove, onMakeLead, disabled, canEdit,
}: {
  team: OrgTeamDto;
  otherTeams: OrgTeamDto[];
  allUsers: OrgPersonDto[];
  onMove: (userId: string, teamId: string | null, label: string) => void;
  onMakeLead: (teamId: string, userId: string | null, teamName: string) => void;
  disabled?: boolean;
  canEdit: boolean;
}) {
  const confirm = useConfirm();
  const total = (team.lead ? 1 : 0) + team.members.length;
  // Candidates for lead: lead can come from any user — but we filter to people on this
  // team or unassigned to avoid pulling someone away from another team via the dropdown.
  const leadCandidates = allUsers.filter((u) =>
    team.lead?.id === u.id || team.members.some((m) => m.id === u.id) || !otherTeams.some((t) => t.lead?.id === u.id || t.members.some((m) => m.id === u.id))
  );

  return (
    <Card className="flex flex-col">
      <CardHeader
        eyebrow={team.vertical ? (
          <span className="inline-flex items-center gap-1">{team.vertical}<InfoHint title="Vertical" side="right">The line of business or campaign this team is dedicated to (its "vertical").</InfoHint></span>
        ) : undefined}
        title={team.name}
        subtitle={`${total} ${total === 1 ? "person" : "people"}`}
        bordered
        action={<Badge tone="neutral" variant="soft">{total}</Badge>}
      />
      <CardBody className="flex flex-col gap-4">
        {/* Team-lead block — selectable dropdown so the user can promote/demote in place */}
        <div className={`rounded-lg border p-3 ${team.lead ? "border-brand-100 bg-brand-50/40" : "border-amber-100 bg-amber-50/40"}`}>
          <div className="section-title mb-1.5 flex items-center justify-between">
            <span>Team Lead</span>
            {team.lead && canEdit && (
              <button
                onClick={async () => {
                  if (!(await confirm({
                    title: "Remove team lead?",
                    description: `Clear the team lead for ${team.name}? You can assign a new one at any time.`,
                    confirmLabel: "Remove",
                    danger: true,
                  }))) return;
                  onMakeLead(team.id, null, team.name);
                }}
                disabled={disabled}
                title={disabled ? "Finishing the last change…" : "Remove this person as team lead"}
                className="text-[10px] font-medium uppercase tracking-wider text-rose-600 hover:underline disabled:opacity-50"
              >
                Clear
              </button>
            )}
          </div>
          {team.lead && <div className="mb-2"><PersonRow person={team.lead} /></div>}
          {canEdit && <Select
            value={team.lead?.id ?? ""}
            aria-label={`Set the team lead for ${team.name}`}
            title={disabled ? "Finishing the last change…" : "Choose who leads this team"}
            onChange={(e) => {
              const id = e.target.value;
              if (id && id !== team.lead?.id) onMakeLead(team.id, id, team.name);
            }}
            disabled={disabled}
          >
            <option value="">— pick a lead —</option>
            {leadCandidates.map((u) => (
              <option key={u.id} value={u.id}>
                {u.userName}{team.lead?.id === u.id ? "  (current)" : ""}
              </option>
            ))}
          </Select>}
        </div>

        <div>
          <div className="section-title mb-2 flex items-center justify-between">
            <span>Members</span>
            <span className="text-[10px] font-normal normal-case text-ink-400 tabular-nums">{team.members.length}</span>
          </div>
          {team.members.length === 0 ? (
            <div className="flex items-center gap-2 text-xs text-ink-500 py-1">
              <Icon name="users" size={14} className="text-ink-400" /> No members on this team yet.
            </div>
          ) : (
            <div className="flex flex-col gap-1.5">
              {team.members.map((m) => (
                <div key={m.id} className="flex items-center gap-2">
                  <div className="flex-1 min-w-0">
                    <PersonRow person={m} compact />
                  </div>
                  {canEdit && <MovePicker
                    person={m}
                    currentTeamId={team.id}
                    teams={otherTeams}
                    onMove={onMove}
                    disabled={disabled}
                  />}
                </div>
              ))}
            </div>
          )}
        </div>
      </CardBody>
    </Card>
  );
}

function MovePicker({
  person, currentTeamId, teams, onMove, disabled,
}: {
  person: OrgPersonDto;
  currentTeamId?: string;
  teams: OrgTeamDto[];
  onMove: (userId: string, teamId: string | null, label: string) => void;
  disabled?: boolean;
}) {
  return (
    <select
      value=""
      onChange={(e) => {
        const v = e.target.value;
        if (!v) return;
        if (v === "__remove__") onMove(person.id, null, "Unassigned");
        else {
          const target = teams.find((t) => t.id === v);
          if (target) onMove(person.id, v, target.name);
        }
        e.currentTarget.value = "";
      }}
      disabled={disabled}
      className="text-xs h-7 px-2 rounded-md border border-ink-200 bg-white text-ink-600 hover:border-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-400 cursor-pointer transition-colors"
      aria-label={`Move ${person.userName} to another team`}
      title={disabled ? "Finishing the last change…" : "Move to another team"}
    >
      <option value="">Move…</option>
      {teams.length > 0 && <optgroup label="Move to team">
        {teams.map((t) => (
          <option key={t.id} value={t.id}>{t.name}</option>
        ))}
      </optgroup>}
      {currentTeamId && <option value="__remove__">Remove from team</option>}
    </select>
  );
}

function UnassignedList({
  people, teams, onMove, disabled, canEdit,
}: {
  people: OrgPersonDto[];
  teams: OrgTeamDto[];
  onMove: (userId: string, teamId: string | null, label: string) => void;
  disabled?: boolean;
  canEdit: boolean;
}) {
  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-2">
      {people.map((p) => (
        <div key={p.id} className="flex items-center gap-3 rounded-lg border hairline px-3 py-2 bg-white hover:bg-ink-50/60 transition-colors">
          <div className="flex-1 min-w-0">
            <PersonRow person={p} />
          </div>
          {canEdit && <select
            value=""
            onChange={(e) => {
              const v = e.target.value;
              if (!v) return;
              const target = teams.find((t) => t.id === v);
              if (target) onMove(p.id, v, target.name);
              e.currentTarget.value = "";
            }}
            disabled={disabled || teams.length === 0}
            aria-label={`Assign ${p.userName} to a team`}
            title={teams.length === 0 ? "Create a team first before assigning people" : disabled ? "Finishing the last change…" : "Assign this person to a team"}
            className="text-xs h-8 px-2 rounded-md border border-brand-200 bg-brand-50 text-brand-700 font-medium hover:bg-brand-100 focus:outline-none focus:ring-2 focus:ring-brand-400 cursor-pointer disabled:opacity-50 transition-colors"
          >
            <option value="">Assign to team…</option>
            {teams.map((t) => (
              <option key={t.id} value={t.id}>{t.name}</option>
            ))}
          </select>}
        </div>
      ))}
    </div>
  );
}

// ---- Person primitives ------------------------------------------------------

function PersonCard({
  person, role, tone, size = "default",
}: {
  person: OrgPersonDto;
  role?: string;
  tone: "brand" | "accent" | "success";
  size?: "hero" | "default";
}) {
  const isHero = size === "hero";
  const ring = {
    brand:   "ring-brand-200/60 bg-gradient-to-b from-brand-50 to-white",
    accent:  "ring-accent-200/60 bg-gradient-to-b from-accent-50 to-white",
    success: "ring-emerald-200/60 bg-gradient-to-b from-emerald-50 to-white",
  }[tone];

  return (
    <div
      className={`
        surface relative overflow-hidden
        ${isHero ? "px-7 py-6 min-w-[280px]" : "px-5 py-4 min-w-[220px]"}
        ring-1 ${ring}
      `}
    >
      <div aria-hidden className="absolute inset-0 bg-noise opacity-40 pointer-events-none" />
      <div className="relative flex items-center gap-3">
        <Avatar name={person.userName} size={isHero ? 48 : 40} />
        <div className="min-w-0">
          {role && (
            <div className={`section-title mb-0.5 ${tone === "brand" ? "text-brand-600" : tone === "accent" ? "text-accent-600" : "text-emerald-700"}`}>
              {roleLabel(role)}
            </div>
          )}
          <div className={`font-semibold text-ink-900 truncate ${isHero ? "text-lg" : "text-sm"}`}>
            {person.userName}
          </div>
          <div className="text-xs text-ink-500 truncate">{person.email}</div>
        </div>
      </div>
      {person.roles.length > 0 && (
        <div className="relative mt-3 flex flex-wrap gap-1">
          {person.roles.slice(0, isHero ? 6 : 3).map((r) => (
            <Badge key={r} tone={tone === "brand" ? "brand" : tone === "accent" ? "info" : "success"} variant="soft">
              {roleLabel(r)}
            </Badge>
          ))}
          {person.roles.length > (isHero ? 6 : 3) && (
            <Badge tone="neutral" variant="soft">+{person.roles.length - (isHero ? 6 : 3)}</Badge>
          )}
        </div>
      )}
    </div>
  );
}

function PersonRow({ person, compact }: { person: OrgPersonDto; compact?: boolean }) {
  return (
    <div className="flex items-center gap-3">
      <Avatar name={person.userName} size={compact ? 28 : 32} />
      <div className="min-w-0 flex-1">
        <div className="text-sm font-medium text-ink-900 truncate">{person.userName}</div>
        {!compact && <div className="text-[11px] text-ink-500 truncate">{person.email}</div>}
        {/* Always surface every role the person holds (or flag that they have none). */}
        <div className="flex flex-wrap gap-1 mt-1">
          {person.roles.length > 0
            ? person.roles.map((r) => (
                <Badge key={r} tone={roleTone(r)} variant="soft" size="sm">{roleLabel(r)}</Badge>
              ))
            : <Badge tone="warning" variant="soft" size="sm">No role assigned</Badge>}
        </div>
      </div>
    </div>
  );
}

/** Tone a role badge by seniority so leadership stands out from agents at a glance. */
function roleTone(r: string): "brand" | "info" | "success" | "neutral" {
  if (r === "SuperAdmin" || r === "CEO" || r === "Admin") return "brand";
  if (LEADERSHIP.includes(r) || r === "CallCenterAdmin" || r === "TeamLead") return "info";
  if (r === "Closer" || r === "Validator" || r === "Fronter" || r === "Verifier") return "success";
  return "neutral";
}

// ---- Visual chrome ----------------------------------------------------------

function LayerLabel({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 mb-3">
      <span className="flex-1 h-px bg-gradient-to-r from-transparent via-ink-200 to-transparent" />
      <span className="section-title">{children}</span>
      <span className="flex-1 h-px bg-gradient-to-r from-transparent via-ink-200 to-transparent" />
    </div>
  );
}

function TreeConnector() {
  return (
    <div className="flex justify-center">
      <div className="w-px h-8 bg-gradient-to-b from-ink-200 to-transparent" />
    </div>
  );
}

/** Centered status card — used for the loading-agency, error and empty cases so the page
 *  always says something clear instead of sitting on a blank/spinner. */
function Notice({ icon, title, body, action }: {
  icon: string; title: string; body: string; action?: React.ReactNode;
}) {
  return (
    <Card><CardBody>
      <EmptyState
        icon={<Icon name={icon} size={24} />}
        tone={icon === "warning" ? "warning" : "neutral"}
        title={title}
        description={body}
        action={action}
      />
    </CardBody></Card>
  );
}

// ---- helpers ---------------------------------------------------------------

const LEADERSHIP = ["QAManager", "ProjectManager", "TechLead", "ProgramManager"];

function primaryLeadershipRole(p: OrgPersonDto): string | undefined {
  return p.roles.find((r) => LEADERSHIP.includes(r));
}
