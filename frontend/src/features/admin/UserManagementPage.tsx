import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { ADMIN_MSG } from "./messages";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { useMemo, useState } from "react";
import {
  useListUsersQuery, useResetUserPasswordMutation,
  useSetUserActiveMutation, useUpdateUserRolesMutation,
  useListCallCentersQuery, useSetUserCallCenterMutation,
  useResendInvitationMutation, useSetUserTeamMutation, useSetUserAgencyMutation,
  useListAgenciesQuery, useOrgTreeQuery,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  Pager, SearchInput, Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast, usePagination,
} from "../../shared/ui";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { ALL_ROLES, roleLabel, ROLE_TONES as roleTones, canManageUser } from "../../shared/constants/roles";
import { RolePicker } from "../../shared/components/RolePicker";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { CreateUserModal } from "./CreateUserModal";




export function UserManagementPage() {
  const me = useSelector((s: RootState) => s.auth.user);
  const isSuperAdmin = (me?.roles ?? []).includes("SuperAdmin");

  const { data: users, isLoading } = useListUsersQuery();
  const { data: callCenters } = useListCallCentersQuery();
  // Agency list only matters to a SuperAdmin (nobody else may move a user between tenants).
  const { data: agencies } = useListAgenciesQuery(undefined, { skip: !isSuperAdmin });
  const [updateRoles] = useUpdateUserRolesMutation();
  const [setActive, { isLoading: settingActive }] = useSetUserActiveMutation();
  const [resetPw, { isLoading: resettingPw }] = useResetUserPasswordMutation();
  const [setUserCc] = useSetUserCallCenterMutation();
  const [setUserTeam] = useSetUserTeamMutation();
  const [setUserAgency] = useSetUserAgencyMutation();
  const [resendInvite, { isLoading: resending }] = useResendInvitationMutation();
  const toast = useToast();
  const confirm = useConfirm();

  // Teams come from the org tree, which is per-agency. An agency-scoped admin only ever needs their
  // own; a SuperAdmin's list spans agencies, so we look up each row's own agency.
  const { data: myOrg } = useOrgTreeQuery(isSuperAdmin ? undefined : undefined);
  const teamsFor = (agencyId: string | null | undefined) => {
    if (!myOrg) return [];
    // The tree we hold is for one agency; only offer its teams to users who belong to it.
    if (agencyId && myOrg.agencyId && agencyId !== myOrg.agencyId) return [];
    return myOrg.teams ?? [];
  };

  // Can the signed-in user manage this row (reset password / edit roles)? Mirrors the backend
  // rank rule so we disable — rather than 403 — actions on peers or higher-ranked accounts.
  const canManage = (u: { id: string; roles: string[] }) =>
    !!me && canManageUser({ id: me.id, roles: me.roles }, u);

  async function assignCallCenter(userId: string, value: string) {
    try {
      await setUserCc({ userId, callCenterId: value || null }).unwrap();
      toast.success(ADMIN_MSG.userMgmt.callCenterUpdated);
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.userMgmt.callCenterUpdateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function assignTeam(userId: string, value: string) {
    try {
      await setUserTeam({ userId, teamId: value || null }).unwrap();
      toast.success(ADMIN_MSG.userMgmt.teamUpdated);
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.userMgmt.teamUpdateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  // Moving agencies is destructive — it drops the user's team and call centre, because both belong
  // to the agency they are leaving. Confirm before doing it rather than surprising the admin.
  async function assignAgency(userId: string, userName: string, value: string) {
    if (!value) return;
    if (!(await confirm({
      title: ADMIN_MSG.userMgmt.moveAgencyConfirmTitle(userName),
      description: ADMIN_MSG.userMgmt.moveAgencyConfirmDesc,
      confirmLabel: ADMIN_MSG.userMgmt.moveAgencyConfirmLabel,
      danger: true,
    }))) return;
    try {
      await setUserAgency({ userId, agencyId: value }).unwrap();
      toast.success(ADMIN_MSG.userMgmt.agencyUpdated);
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.userMgmt.agencyUpdateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  const [search, setSearch] = useState("");
  // Narrowing filters — an agency with a few hundred users is unusable on free-text alone.
  const [roleFilter, setRoleFilter] = useState("");
  const [ccFilter, setCcFilter] = useState("");
  const [teamFilter, setTeamFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<{ id: string; userName: string; roles: string[] } | null>(null);
  const [resetting, setResetting] = useState<{ id: string; userName: string } | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState<{ id: string; userName: string } | null>(null);
  const [newPwd, setNewPwd] = useState("");

  const filtered = useMemo(() => {
    if (!users) return [];
    const q = search.trim().toLowerCase();
    return users.filter((u) => {
      if (q && !(
        u.userName.toLowerCase().includes(q) ||
        u.email.toLowerCase().includes(q) ||
        u.roles.some((r) => r.toLowerCase().includes(q))
      )) return false;

      if (roleFilter && !u.roles.includes(roleFilter)) return false;

      // "" = any; "none" = deliberately agency-level (no centre pinned).
      if (ccFilter === "none" ? !!u.callCenterId : ccFilter && u.callCenterId !== ccFilter) return false;
      if (teamFilter === "none" ? !!u.teamId : teamFilter && u.teamId !== teamFilter) return false;

      if (statusFilter === "active" && !(u.isActive ?? true)) return false;
      if (statusFilter === "inactive" && (u.isActive ?? true)) return false;
      if (statusFilter === "pending" && !u.mustChangePassword) return false;
      if (statusFilter === "noroles" && u.roles.length > 0) return false;

      return true;
    });
  }, [users, search, roleFilter, ccFilter, teamFilter, statusFilter]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    key: "userName",
    accessors: { role: (u) => u.roles[0] ?? "" },
  });

  // Paging is purely presentational: it slices the already-filtered+sorted list for display.
  // Selection, bulk deactivate and CSV export still work off the FULL filtered list.
  const pg = usePagination(sorted);

  const sel = useRowSelection(sorted.map((u) => u.id));

  function exportSelected() {
    const chosen = sorted.filter((u) => sel.isSelected(u.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (u) => u.userName },
      { header: "Email", value: (u) => u.email },
      { header: "Roles", value: (u) => u.roles.map((r) => roleLabel(r)).join("; ") },
      { header: "Call centre", value: (u) => (callCenters ?? []).find((c) => c.id === u.callCenterId)?.name ?? "Agency-level" },
      { header: "Active", value: (u) => ((u.isActive ?? true) ? "Yes" : "No") },
    ], `users-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(ADMIN_MSG.common.exportReady, ADMIN_MSG.common.exportReadyDesc(chosen.length));
  }

  // Bulk assignment — the single biggest time saver when standing up a new call centre or shift.
  // Loops the same per-row mutation the dropdowns use, so all the server-side tenant validation and
  // the "your call centre changed" notification apply to every user exactly as if done by hand.
  async function bulkAssignCallCenter(callCenterId: string | null) {
    const ids = sel.selectedIds;
    if (ids.length === 0) return;
    try {
      await Promise.all(ids.map((userId) => setUserCc({ userId, callCenterId }).unwrap()));
      toast.success(ADMIN_MSG.userMgmt.bulkCallCenterDone(ids.length));
      sel.clear();
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.userMgmt.callCenterUpdateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function bulkAssignTeam(teamId: string | null) {
    const ids = sel.selectedIds;
    if (ids.length === 0) return;
    try {
      await Promise.all(ids.map((userId) => setUserTeam({ userId, teamId }).unwrap()));
      toast.success(ADMIN_MSG.userMgmt.bulkTeamDone(ids.length));
      sel.clear();
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.userMgmt.teamUpdateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function activateSelected() {
    const ids = sel.selectedIds;
    if (ids.length === 0) return;
    try {
      await Promise.all(ids.map((id) => setActive({ id, isActive: true }).unwrap()));
      toast.success(ADMIN_MSG.userMgmt.usersActivated(ids.length));
      sel.clear();
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.userMgmt.activateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  // Bulk deactivate loops the SAME per-row setActive mutation over the ticked users — no new endpoint.
  async function deactivateSelected() {
    const n = sel.selectedCount;
    if (n === 0) return;
    if (!(await confirm({
      title: ADMIN_MSG.userMgmt.deactivateConfirmTitle(n),
      description: ADMIN_MSG.userMgmt.deactivateConfirmDesc,
      confirmLabel: ADMIN_MSG.userMgmt.deactivateConfirmLabel,
      danger: true,
    }))) return;
    try {
      await Promise.all(sel.selectedIds.map((id) => setActive({ id, isActive: false }).unwrap()));
      toast.success(ADMIN_MSG.userMgmt.usersDeactivated, ADMIN_MSG.common.canNoLongerSignIn(n));
      sel.clear();
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.common.deactivateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  const stats = useMemo(() => {
    const list = users ?? [];
    return {
      total: list.length,
      admins: list.filter((u) => u.roles.includes("Admin") || u.roles.includes("ProgramManager")).length,
      mustChange: list.filter((u) => u.mustChangePassword).length,
      expired: list.filter((u) => u.invitationExpired).length,
      noRoles: list.filter((u) => u.roles.length === 0).length,
    };
  }, [users]);

  return (
    <>
      <PageHeader
        eyebrow="Administration"
        title="User management"
        description="Add or remove user roles, reset passwords, and manage account access."
        actions={
          <Button leftIcon={<Icon name="userPlus" size={15} />} onClick={() => setCreating(true)}>
            {ADMIN_MSG.userMgmt.createSubmit}
          </Button>
        }
      />

      <CreateUserModal
        open={creating}
        onClose={() => setCreating(false)}
        isSuperAdmin={isSuperAdmin}
        defaultAgencyId={me?.agencyId}
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-5">
        <Stat label="Total users"  value={stats.total}      icon={<Icon name="users" size={18} />} tone="brand" />
        <Stat label="Admins"       value={stats.admins}     icon={<Icon name="shield" size={18} />} tone="danger"
              hint="Admins & program managers" />
        <Stat label="Pending pwd"  value={stats.mustChange} icon={<Icon name="key" size={18} />} tone="warning"
              hint={stats.expired > 0 ? `${stats.expired} invite${stats.expired === 1 ? "" : "s"} expired — resend needed` : "First-login change required; invites expire after 2 days"} />
        <Stat label="No roles"     value={stats.noRoles}    icon={<Icon name="userX" size={18} />} tone="neutral"
              hint="Need role assignment" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[260px]">
            <SearchInput
              value={search}
              onChange={setSearch}
              placeholder={ADMIN_MSG.search.users}
            />
          </div>
          {/* Narrowing filters. An agency with hundreds of users can't be worked by search alone —
              "every closer in Lahore with no team" is the question an admin actually has. */}
          <Select value={roleFilter} onChange={(e) => setRoleFilter(e.target.value)}
            className="text-xs min-w-[9rem]" aria-label={ADMIN_MSG.userMgmt.filterRole}>
            <option value="">{ADMIN_MSG.userMgmt.allRoles}</option>
            {ALL_ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}
          </Select>

          <Select value={ccFilter} onChange={(e) => setCcFilter(e.target.value)}
            className="text-xs min-w-[9rem]" aria-label={ADMIN_MSG.userMgmt.filterCallCenter}>
            <option value="">{ADMIN_MSG.userMgmt.allCallCenters}</option>
            <option value="none">{ADMIN_MSG.userMgmt.agencyLevel}</option>
            {(callCenters ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </Select>

          <Select value={teamFilter} onChange={(e) => setTeamFilter(e.target.value)}
            className="text-xs min-w-[8rem]" aria-label={ADMIN_MSG.userMgmt.filterTeam}>
            <option value="">{ADMIN_MSG.userMgmt.allTeams}</option>
            <option value="none">{ADMIN_MSG.userMgmt.noTeam}</option>
            {(myOrg?.teams ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
          </Select>

          <Select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}
            className="text-xs min-w-[8rem]" aria-label={ADMIN_MSG.userMgmt.filterStatus}>
            <option value="">{ADMIN_MSG.userMgmt.allStatuses}</option>
            <option value="active">{ADMIN_MSG.userMgmt.statusActive}</option>
            <option value="inactive">{ADMIN_MSG.userMgmt.statusInactive}</option>
            <option value="pending">{ADMIN_MSG.userMgmt.statusPending}</option>
            <option value="noroles">{ADMIN_MSG.userMgmt.statusNoRoles}</option>
          </Select>

          {users && (
            <Badge tone="neutral" variant="soft" className="tabular-nums whitespace-nowrap">
              {filtered.length} of {users.length}
            </Badge>
          )}
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>
          {[0, 1, 2, 3].map((i) => (
            <div key={i} className="flex items-center gap-3 py-3 border-b hairline last:border-0">
              <Skeleton className="h-9 w-9 rounded-full" />
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-4 w-56 ml-auto" />
            </div>
          ))}
        </CardBody></Card>
      ) : !users || users.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="users" size={20} />}
            title={ADMIN_MSG.userMgmt.emptyTitle}
            description={ADMIN_MSG.userMgmt.emptyDesc}
          />
        </CardBody></Card>
      ) : (
        <>
        <Table>
          <THead>
            <TR>
              <TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH>
              <TH sortDir={dirFor("userName")} onClick={() => toggle("userName")}>User</TH>
              <TH sortDir={dirFor("email")} onClick={() => toggle("email")}>Email</TH>
              <TH sortDir={dirFor("role")} onClick={() => toggle("role")}><span className="inline-flex items-center gap-1">Roles<InfoHint title="Roles" side="top">A user's roles decide which modules they see and what they can do. No roles means they can sign in but can't access anything until you assign one.</InfoHint></span></TH>
              <TH><span className="inline-flex items-center gap-1">Call center<InfoHint title="Call center" side="top">Pin a user to one call center to limit their pipeline to just that center; leave it agency-level to see the whole agency.</InfoHint></span></TH>
              <TH><span className="inline-flex items-center gap-1">Team<InfoHint title="Team" side="top">The team this user reports into. Teams live inside an agency, so moving someone to another agency clears their team.</InfoHint></span></TH>
              {isSuperAdmin && (
                <TH><span className="inline-flex items-center gap-1">Agency<InfoHint title="Agency" side="top">Which agency this user belongs to. Moving them clears their team and call centre, because both live inside an agency.</InfoHint></span></TH>
              )}
              <TH className="sticky right-0 bg-ink-50 border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {pg.pageItems.map((u) => {
              // Backend now returns isActive on every UserSummary. We default to
              // `true` for older payloads so we don't accidentally grey out everyone.
              const active = u.isActive ?? true;
              return (
              <TR key={u.id} className={sel.isSelected(u.id) ? "bg-brand-50/40" : (active ? "" : "bg-rose-50/30")}>
                <TD><Checkbox aria-label={`Select ${u.userName}`} {...sel.checkboxProps(u.id)} /></TD>
                <TD>
                  <div className="flex items-center gap-3">
                    <Avatar name={u.userName} size={36} className={active ? "" : "opacity-50 grayscale"} />
                    <div className="min-w-0">
                      <div className={"font-medium truncate " + (active ? "text-ink-900" : "text-ink-500 line-through decoration-rose-400/40")} title={u.userName}>
                        {u.userName}
                      </div>
                      <div className="flex items-center gap-1 mt-0.5">
                        {!active && (
                          <Badge tone="danger" variant="soft" size="sm">
                            <Icon name="userX" size={10} className="mr-1" /> Deactivated
                          </Badge>
                        )}
                        {u.mustChangePassword && (
                          u.invitationExpired ? (
                            <Badge tone="danger" variant="soft" size="sm">
                              <Icon name="clock" size={10} className="mr-1" /> Invite expired
                            </Badge>
                          ) : (
                            <Badge tone="warning" variant="soft" size="sm">
                              <Icon name="key" size={10} className="mr-1" /> Must change password
                            </Badge>
                          )
                        )}
                      </div>
                    </div>
                  </div>
                </TD>
                <TD className="text-ink-600"><span className="block truncate max-w-[15rem]" title={u.email}>{u.email}</span></TD>
                <TD>
                  <div className="flex flex-wrap gap-1">
                    {u.roles.length === 0
                      ? <span className="text-xs text-ink-400">No roles</span>
                      : u.roles.map((r) => (
                        <Badge key={r} tone={roleTones[r] ?? "neutral"} variant="soft">{roleLabel(r)}</Badge>
                      ))}
                  </div>
                </TD>
                <TD>
                  <Select
                    value={u.callCenterId ?? ""}
                    onChange={(e) => assignCallCenter(u.id, e.target.value)}
                    className="text-xs min-w-[9rem]"
                  >
                    <option value="">Agency-level (all)</option>
                    {/* A SuperAdmin's list spans agencies, so only offer the centres that belong to
                        THIS user's agency — assigning someone to another tenant's centre would be
                        rejected by the server anyway, and is confusing to even show. */}
                    {(callCenters ?? [])
                      .filter((c) => !c.agencyId || c.agencyId === u.agencyId)
                      .map((c) => (
                        <option key={c.id} value={c.id}>{c.name}</option>
                      ))}
                  </Select>
                </TD>
                <TD>
                  <Select
                    value={u.teamId ?? ""}
                    onChange={(e) => assignTeam(u.id, e.target.value)}
                    className="text-xs min-w-[9rem]"
                    disabled={!canManage(u)}
                    aria-label={`Team for ${u.userName}`}
                  >
                    <option value="">No team</option>
                    {/* Teams are agency-scoped, so only this user's own agency's teams are valid. */}
                    {teamsFor(u.agencyId).map((t) => (
                      <option key={t.id} value={t.id}>{t.name}</option>
                    ))}
                  </Select>
                </TD>
                {isSuperAdmin && (
                  <TD>
                    <Select
                      value={u.agencyId ?? ""}
                      onChange={(e) => assignAgency(u.id, u.userName, e.target.value)}
                      className="text-xs min-w-[9rem]"
                      aria-label={`Agency for ${u.userName}`}
                    >
                      <option value="">—</option>
                      {(agencies ?? []).map((a) => (
                        <option key={a.id} value={a.id}>{a.name}</option>
                      ))}
                    </Select>
                  </TD>
                )}
                <TD className="sticky right-0 bg-white border-l hairline shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">
                  {/* Icon-only actions with tooltips so the row fits without horizontal scroll. */}
                  <div className="flex items-center justify-end gap-1">
                    <Button
                      variant="outline" size="sm" disabled={!canManage(u)}
                      title={canManage(u) ? "Edit roles" : "You can only manage users below your role level"}
                      aria-label="Edit roles"
                      onClick={() => setEditing({ id: u.id, userName: u.userName, roles: u.roles })}
                    ><Icon name="userCog" size={15} /></Button>
                    <Button
                      variant="ghost" size="sm" disabled={!canManage(u)}
                      title={canManage(u) ? "Reset password" : "You can only manage users below your role level"}
                      aria-label="Reset password"
                      onClick={() => { setResetting({ id: u.id, userName: u.userName }); setNewPwd(""); }}
                    ><Icon name="key" size={15} /></Button>
                    {active && u.mustChangePassword && (
                      <Button
                        variant={u.invitationExpired ? "outline" : "ghost"} size="sm" disabled={resending}
                        className={u.invitationExpired ? "text-amber-700 border-amber-300 hover:bg-amber-50" : undefined}
                        title={u.invitationExpired ? "Invitation expired — resend a fresh link" : "Resend invitation"}
                        aria-label="Resend invitation"
                        onClick={async () => {
                          try {
                            await resendInvite(u.id).unwrap();
                            toast.success(ADMIN_MSG.common.invitationResent, ADMIN_MSG.common.invitationResentDesc(u.userName));
                          } catch (err: unknown) {
                            toast.error(ADMIN_MSG.common.resendInviteFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
                          }
                        }}
                      ><Icon name="mail" size={15} /></Button>
                    )}
                    {active ? (
                      <Button
                        variant="ghost" size="sm" className="text-rose-600 hover:bg-rose-50" title="Deactivate user" aria-label="Deactivate user"
                        onClick={() => setConfirmDeactivate({ id: u.id, userName: u.userName })}
                      ><Icon name="userX" size={15} /></Button>
                    ) : (
                      <Button
                        variant="ghost" size="sm" className="text-emerald-700 hover:bg-emerald-50" title="Reactivate user" aria-label="Reactivate user"
                        onClick={async () => {
                          try {
                            await setActive({ id: u.id, isActive: true }).unwrap();
                            toast.success(ADMIN_MSG.userMgmt.userReactivated, ADMIN_MSG.userMgmt.userReactivatedDesc(u.userName));
                          } catch (err: unknown) {
                            toast.error(ADMIN_MSG.common.reactivateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
                          }
                        }}
                      ><Icon name="userCheck" size={15} /></Button>
                    )}
                  </div>
                </TD>
              </TR>
              );
            })}
          </TBody>
        </Table>
        <Pager {...pg} onPage={pg.setPage} unit="users" />

        {/* Bulk placement. Standing up a shift or a new centre means moving twenty people at once;
            doing that one dropdown at a time was the single slowest thing on this screen. The
            selects reset to their placeholder after firing, so they read as an action, not a state. */}
        {sel.selectedCount > 0 && (
          <Card className="mt-3">
            <CardBody className="flex items-center gap-3 flex-wrap">
              <span className="text-sm font-medium text-ink-700">
                {ADMIN_MSG.userMgmt.bulkAssignLabel(sel.selectedCount)}
              </span>
              <Select
                value="" className="text-xs min-w-[11rem]"
                aria-label={ADMIN_MSG.userMgmt.bulkToCallCenter}
                onChange={(e) => { const v = e.target.value; e.target.value = ""; if (v) bulkAssignCallCenter(v === "none" ? null : v); }}
              >
                <option value="">{ADMIN_MSG.userMgmt.bulkToCallCenter}</option>
                <option value="none">{ADMIN_MSG.userMgmt.agencyLevel}</option>
                {(callCenters ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </Select>
              <Select
                value="" className="text-xs min-w-[10rem]"
                aria-label={ADMIN_MSG.userMgmt.bulkToTeam}
                onChange={(e) => { const v = e.target.value; e.target.value = ""; if (v) bulkAssignTeam(v === "none" ? null : v); }}
              >
                <option value="">{ADMIN_MSG.userMgmt.bulkToTeam}</option>
                <option value="none">{ADMIN_MSG.userMgmt.noTeam}</option>
                {(myOrg?.teams ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
              </Select>
            </CardBody>
          </Card>
        )}

        <BulkActionBar
          count={sel.selectedCount} itemNoun="user" onClear={sel.clear}
          actions={[
            { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
            { key: "activate", label: "Activate", icon: "check", onClick: activateSelected },
            { key: "deactivate", label: "Deactivate", icon: "userX", onClick: deactivateSelected, danger: true },
          ]}
        />
        </>
      )}

      {/* Edit roles modal */}
      <Modal
        open={editing !== null}
        onClose={() => setEditing(null)}
        title={editing ? `Edit roles · ${editing.userName}` : ""}
        description="Pick the roles this user should have. They take effect immediately."
        size="lg"
      >
        {editing && (
          <RolePicker
            initial={editing.roles}
            onCancel={() => setEditing(null)}
            onSave={async (roles) => {
              try {
                await updateRoles({ id: editing.id, roles }).unwrap();
                toast.success(ADMIN_MSG.userMgmt.rolesUpdated, ADMIN_MSG.userMgmt.rolesUpdatedDesc(editing.userName, roles.length));
                setEditing(null);
              } catch (err: unknown) {
                toast.error(ADMIN_MSG.userMgmt.rolesUpdateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
              }
            }}
          />
        )}
      </Modal>

      {/* Reset password modal */}
      <Modal
        open={resetting !== null}
        onClose={() => setResetting(null)}
        title={resetting ? `Reset password · ${resetting.userName}` : ""}
        description="The user will need to use this password on their next sign-in."
        footer={
          <>
            <Button variant="ghost" onClick={() => setResetting(null)}>Cancel</Button>
            <Button
              disabled={newPwd.length < 8}
              loading={resettingPw}
              title={newPwd.length < 8 ? "Enter at least 8 characters first" : undefined}
              onClick={async () => {
                if (!resetting) return;
                try {
                  await resetPw({ id: resetting.id, newPassword: newPwd }).unwrap();
                  toast.success(ADMIN_MSG.common.passwordReset, ADMIN_MSG.common.passwordResetDesc(resetting.userName));
                  setResetting(null);
                } catch (err: unknown) {
                  toast.error(ADMIN_MSG.common.resetPasswordFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
                }
              }}
            >Set password</Button>
          </>
        }
      >
        <Input
          type="password"
          // "new-password" tells the browser this is a password-creation field, so it won't try to
          // autofill a saved credential (and won't pair it with a "username" field like the search box).
          autoComplete="new-password"
          name="admin-new-password"
          label="New password"
          hint="Minimum 8 characters with uppercase, lowercase, digit, and symbol."
          value={newPwd}
          onChange={(e) => setNewPwd(e.target.value)}
          autoFocus
        />
      </Modal>

      {/* Deactivate confirm */}
      <Modal
        open={confirmDeactivate !== null}
        onClose={() => setConfirmDeactivate(null)}
        title="Deactivate user"
        description={confirmDeactivate ? `This will prevent ${confirmDeactivate.userName} from signing in.` : ""}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmDeactivate(null)}>Cancel</Button>
            <Button
              variant="danger"
              loading={settingActive}
              onClick={async () => {
                if (!confirmDeactivate) return;
                try {
                  await setActive({ id: confirmDeactivate.id, isActive: false }).unwrap();
                  toast.success(ADMIN_MSG.userMgmt.userDeactivated, ADMIN_MSG.common.canNoLongerSignIn(confirmDeactivate.userName));
                  setConfirmDeactivate(null);
                } catch (err: unknown) {
                  toast.error(ADMIN_MSG.common.deactivateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
                }
              }}
            >Deactivate user</Button>
          </>
        }
      >
        <div className="text-sm text-ink-700">
          You can re-activate the user later if needed.
        </div>
      </Modal>
    </>
  );
}

