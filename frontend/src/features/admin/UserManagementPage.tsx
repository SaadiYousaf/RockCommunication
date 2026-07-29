import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import {
  useListUsersQuery, useResetUserPasswordMutation,
  useSetUserActiveMutation, useUpdateUserRolesMutation,
  useListCallCentersQuery, useSetUserCallCenterMutation,
  useResendInvitationMutation,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { Link } from "react-router-dom";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { ALL_ROLES, roleLabel, ROLE_TONES as roleTones, canManageUser } from "../../shared/constants/roles";
import { useTableSort } from "../../shared/hooks/useTableSort";




export function UserManagementPage() {
  const me = useSelector((s: RootState) => s.auth.user);
  const { data: users, isLoading } = useListUsersQuery();
  const { data: callCenters } = useListCallCentersQuery();
  const [updateRoles] = useUpdateUserRolesMutation();
  const [setActive] = useSetUserActiveMutation();
  const [resetPw] = useResetUserPasswordMutation();
  const [setUserCc] = useSetUserCallCenterMutation();
  const [resendInvite, { isLoading: resending }] = useResendInvitationMutation();
  const toast = useToast();

  // Can the signed-in user manage this row (reset password / edit roles)? Mirrors the backend
  // rank rule so we disable — rather than 403 — actions on peers or higher-ranked accounts.
  const canManage = (u: { id: string; roles: string[] }) =>
    !!me && canManageUser({ id: me.id, roles: me.roles }, u);

  async function assignCallCenter(userId: string, value: string) {
    try {
      await setUserCc({ userId, callCenterId: value || null }).unwrap();
      toast.success("Call center updated");
    } catch (err: unknown) {
      toast.error("Couldn't update call center", getErrorDetail(err) ?? "Try again.");
    }
  }

  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<{ id: string; userName: string; roles: string[] } | null>(null);
  const [resetting, setResetting] = useState<{ id: string; userName: string } | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState<{ id: string; userName: string } | null>(null);
  const [newPwd, setNewPwd] = useState("");

  const filtered = useMemo(() => {
    if (!users) return [];
    const q = search.trim().toLowerCase();
    if (!q) return users;
    return users.filter((u) =>
      u.userName.toLowerCase().includes(q) ||
      u.email.toLowerCase().includes(q) ||
      u.roles.some((r) => r.toLowerCase().includes(q))
    );
  }, [users, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    key: "userName",
    accessors: { role: (u) => u.roles[0] ?? "" },
  });

  const stats = useMemo(() => {
    const list = users ?? [];
    return {
      total: list.length,
      admins: list.filter((u) => u.roles.includes("Admin") || u.roles.includes("ProgramManager")).length,
      mustChange: list.filter((u) => u.mustChangePassword).length,
      noRoles: list.filter((u) => u.roles.length === 0).length,
    };
  }, [users]);

  return (
    <>
      <PageHeader
        title="User management"
        description="Add or remove user roles, reset passwords, and manage account access."
        actions={
          <Link to="/admin/register">
            <Button leftIcon={<Icon name="userPlus" size={15} />}>
              Invite user
            </Button>
          </Link>
        }
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-5">
        <Stat label="Total users"  value={stats.total}      icon={<Icon name="users" size={18} />} tone="brand" />
        <Stat label="Admins"       value={stats.admins}     icon={<Icon name="shield" size={18} />} tone="danger"
              hint="Admin & ProgramManager" />
        <Stat label="Pending pwd"  value={stats.mustChange} icon={<Icon name="key" size={18} />} tone="warning"
              hint="First-login change required" />
        <Stat label="No roles"     value={stats.noRoles}    icon={<Icon name="userX" size={18} />} tone="neutral"
              hint="Need role assignment" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[260px]">
            <SearchInput
              value={search}
              onChange={setSearch}
              placeholder="Search by name, email, or role…"
            />
          </div>
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
            title="No users yet"
            description="Users will appear here once they're created."
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH sortDir={dirFor("userName")} onClick={() => toggle("userName")}>User</TH>
              <TH sortDir={dirFor("email")} onClick={() => toggle("email")}>Email</TH>
              <TH sortDir={dirFor("role")} onClick={() => toggle("role")}>Roles</TH>
              <TH><span className="inline-flex items-center gap-1">Call center<InfoHint title="Call center" side="top">Pin a user to one call center to limit their pipeline to just that center; leave it agency-level to see the whole agency.</InfoHint></span></TH>
              <TH className="sticky right-0 bg-ink-50 border-l hairline text-right shadow-[-8px_0_8px_-8px_rgba(0,0,0,0.10)]">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {sorted.map((u) => {
              // Backend now returns isActive on every UserSummary. We default to
              // `true` for older payloads so we don't accidentally grey out everyone.
              const active = u.isActive ?? true;
              return (
              <TR key={u.id} className={active ? "" : "bg-rose-50/30"}>
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
                          <Badge tone="warning" variant="soft" size="sm">
                            <Icon name="key" size={10} className="mr-1" /> Must change password
                          </Badge>
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
                    {(callCenters ?? []).map((c) => (
                      <option key={c.id} value={c.id}>{c.name}</option>
                    ))}
                  </Select>
                </TD>
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
                        variant="ghost" size="sm" disabled={resending} title="Resend invitation" aria-label="Resend invitation"
                        onClick={async () => {
                          try {
                            await resendInvite(u.id).unwrap();
                            toast.success("Invitation resent", `A fresh temporary password was emailed to ${u.userName}.`);
                          } catch (err: unknown) {
                            toast.error("Couldn't resend invitation", getErrorDetail(err) ?? "Try again.");
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
                            toast.success("User reactivated", `${u.userName} can sign in again.`);
                          } catch (err: unknown) {
                            toast.error("Couldn't reactivate", getErrorDetail(err) ?? "Try again.");
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
                toast.success("Roles updated", `${editing.userName} now has ${roles.length} role(s).`);
                setEditing(null);
              } catch (err: unknown) {
                toast.error("Couldn't update roles", getErrorDetail(err) ?? "Try again.");
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
              onClick={async () => {
                if (!resetting) return;
                try {
                  await resetPw({ id: resetting.id, newPassword: newPwd }).unwrap();
                  toast.success("Password reset", `New password set for ${resetting.userName}.`);
                  setResetting(null);
                } catch (err: unknown) {
                  toast.error("Couldn't reset password", getErrorDetail(err) ?? "Try again.");
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
              onClick={async () => {
                if (!confirmDeactivate) return;
                try {
                  await setActive({ id: confirmDeactivate.id, isActive: false }).unwrap();
                  toast.success("User deactivated", `${confirmDeactivate.userName} can no longer sign in.`);
                  setConfirmDeactivate(null);
                } catch (err: unknown) {
                  toast.error("Couldn't deactivate", getErrorDetail(err) ?? "Try again.");
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

function RolePicker({
  initial, onSave, onCancel,
}: { initial: string[]; onSave: (roles: string[]) => void; onCancel: () => void }) {
  const [picked, setPicked] = useState(new Set(initial));
  const toggle = (r: string) =>
    setPicked((prev) => {
      const next = new Set(prev);
      next.has(r) ? next.delete(r) : next.add(r);
      return next;
    });

  return (
    <>
      <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
        {ALL_ROLES.map((r) => {
          const active = picked.has(r);
          return (
            <button
              key={r} type="button"
              onClick={() => toggle(r)}
              className={
                "flex items-center gap-2 px-3 py-2 rounded-lg border text-sm transition-all focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40 " +
                (active
                  ? "border-brand-500 bg-brand-50 text-brand-700"
                  : "border-ink-200 hover:border-ink-300 hover:bg-ink-50/60 text-ink-700")
              }
            >
              <span className={"h-4 w-4 rounded border grid place-items-center " +
                (active ? "border-brand-600 bg-brand-600 text-white" : "border-ink-300 bg-white")}>
                {active && <Icon name="check" size={12} />}
              </span>
              <span className="truncate">{roleLabel(r)}</span>
            </button>
          );
        })}
      </div>
      <div className="flex justify-end gap-2 mt-5">
        <Button variant="ghost" onClick={onCancel}>Cancel</Button>
        <Button onClick={() => onSave(Array.from(picked))}>
          Save {picked.size > 0 ? `(${picked.size})` : ""}
        </Button>
      </div>
    </>
  );
}
