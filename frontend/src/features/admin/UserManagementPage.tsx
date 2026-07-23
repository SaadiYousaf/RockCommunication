import { useMemo, useState } from "react";
import {
  useListUsersQuery, useResetUserPasswordMutation,
  useSetUserActiveMutation, useUpdateUserRolesMutation,
  useListCallCentersQuery, useSetUserCallCenterMutation,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { Link } from "react-router-dom";

const ALL_ROLES = [
  "Admin", "ProgramManager", "TeamLead",
  "Fronter", "Verifier",
  "JrCloser", "Closer", "Validator", "SelfValidator",
  "Followups", "Correspondence", "Winbacks",
];

const roleTones: Record<string, "brand" | "info" | "success" | "warning" | "danger" | "neutral"> = {
  Admin: "danger", ProgramManager: "danger", TeamLead: "warning",
  Closer: "success", JrCloser: "success", SelfValidator: "success",
  Validator: "info", Fronter: "brand", Verifier: "brand",
  Followups: "neutral", Correspondence: "neutral", Winbacks: "neutral",
};

export function UserManagementPage() {
  const { data: users, isLoading } = useListUsersQuery();
  const { data: callCenters } = useListCallCentersQuery();
  const [updateRoles] = useUpdateUserRolesMutation();
  const [setActive] = useSetUserActiveMutation();
  const [resetPw] = useResetUserPasswordMutation();
  const [setUserCc] = useSetUserCallCenterMutation();
  const toast = useToast();

  async function assignCallCenter(userId: string, value: string) {
    try {
      await setUserCc({ userId, callCenterId: value || null }).unwrap();
      toast.success("Call center updated");
    } catch (err: any) {
      toast.error("Couldn't update call center", err?.data?.detail ?? "Try again.");
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

  const stats = useMemo(() => {
    const list = users ?? [];
    return {
      total: list.length,
      admins: list.filter((u) => u.roles.includes("Admin") || u.roles.includes("ProgramManager")).length,
      mustChange: list.filter((u: any) => u.mustChangePassword).length,
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

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Total users"  value={stats.total}      icon={<Icon name="users" size={16} />} tone="brand" />
        <Stat label="Admins"       value={stats.admins}     icon={<Icon name="shield" size={16} />} tone="danger"
              hint="Admin & ProgramManager" />
        <Stat label="Pending pwd"  value={stats.mustChange} icon={<Icon name="key" size={16} />} tone="warning"
              hint="First-login change required" />
        <Stat label="No roles"     value={stats.noRoles}    icon={<Icon name="userX" size={16} />} tone="neutral"
              hint="Need role assignment" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[260px]">
            <Input
              leftIcon={<Icon name="search" size={16} />}
              placeholder="Search by name, email, or role…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          {users && (
            <Badge tone="neutral" variant="soft">
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
              <TH>User</TH>
              <TH>Email</TH>
              <TH>Roles</TH>
              <TH>Call center</TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {filtered.map((u) => {
              // Backend now returns isActive on every UserSummary. We default to
              // `true` for older payloads so we don't accidentally grey out everyone.
              const active = (u as any).isActive ?? true;
              return (
              <TR key={u.id} className={active ? "" : "bg-rose-50/30"}>
                <TD>
                  <div className="flex items-center gap-3">
                    <Avatar name={u.userName} size={36} className={active ? "" : "opacity-50 grayscale"} />
                    <div className="min-w-0">
                      <div className={"font-medium truncate " + (active ? "text-ink-900" : "text-ink-500 line-through decoration-rose-400/40")}>
                        {u.userName}
                      </div>
                      <div className="flex items-center gap-1 mt-0.5">
                        {!active && (
                          <Badge tone="danger" variant="soft" size="sm">
                            <Icon name="userX" size={10} className="mr-1" /> Deactivated
                          </Badge>
                        )}
                        {(u as any).mustChangePassword && (
                          <Badge tone="warning" variant="soft" size="sm">
                            <Icon name="key" size={10} className="mr-1" /> Must change password
                          </Badge>
                        )}
                      </div>
                    </div>
                  </div>
                </TD>
                <TD className="text-ink-600">{u.email}</TD>
                <TD>
                  <div className="flex flex-wrap gap-1">
                    {u.roles.length === 0
                      ? <span className="text-xs text-ink-400">No roles</span>
                      : u.roles.map((r) => (
                        <Badge key={r} tone={roleTones[r] ?? "neutral"} variant="soft">{r}</Badge>
                      ))}
                  </div>
                </TD>
                <TD>
                  <Select
                    value={(u as any).callCenterId ?? ""}
                    onChange={(e) => assignCallCenter(u.id, e.target.value)}
                    className="text-xs"
                  >
                    <option value="">Agency-level (all)</option>
                    {(callCenters ?? []).map((c) => (
                      <option key={c.id} value={c.id}>{c.name}</option>
                    ))}
                  </Select>
                </TD>
                <TD>
                  <div className="flex items-center justify-end gap-1.5">
                    <Button
                      variant="outline" size="sm"
                      leftIcon={<Icon name="userCog" size={13} />}
                      onClick={() => setEditing({ id: u.id, userName: u.userName, roles: u.roles })}
                    >Roles</Button>
                    <Button
                      variant="ghost" size="sm"
                      leftIcon={<Icon name="key" size={13} />}
                      onClick={() => { setResetting({ id: u.id, userName: u.userName }); setNewPwd(""); }}
                    >Reset</Button>
                    {active ? (
                      <Button
                        variant="ghost" size="sm" className="text-rose-600 hover:bg-rose-50"
                        leftIcon={<Icon name="userX" size={13} />}
                        onClick={() => setConfirmDeactivate({ id: u.id, userName: u.userName })}
                      >Deactivate</Button>
                    ) : (
                      <Button
                        variant="ghost" size="sm" className="text-emerald-700 hover:bg-emerald-50"
                        leftIcon={<Icon name="userCheck" size={13} />}
                        onClick={async () => {
                          try {
                            await setActive({ id: u.id, isActive: true }).unwrap();
                            toast.success("User reactivated", `${u.userName} can sign in again.`);
                          } catch (err: any) {
                            toast.error("Couldn't reactivate", err?.data?.detail ?? "Try again.");
                          }
                        }}
                      >Reactivate</Button>
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
              } catch (err: any) {
                toast.error("Couldn't update roles", err?.data?.detail ?? "Try again.");
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
                } catch (err: any) {
                  toast.error("Couldn't reset password", err?.data?.detail ?? "Try again.");
                }
              }}
            >Set password</Button>
          </>
        }
      >
        <Input
          type="password"
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
                } catch (err: any) {
                  toast.error("Couldn't deactivate", err?.data?.detail ?? "Try again.");
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
                "flex items-center gap-2 px-3 py-2 rounded-lg border text-sm transition-all " +
                (active
                  ? "border-brand-500 bg-brand-50 text-brand-700"
                  : "border-ink-200 hover:border-ink-300 text-ink-700")
              }
            >
              <span className={"h-4 w-4 rounded border grid place-items-center " +
                (active ? "border-brand-600 bg-brand-600 text-white" : "border-ink-300 bg-white")}>
                {active && <Icon name="check" size={12} />}
              </span>
              <span className="truncate">{r}</span>
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
