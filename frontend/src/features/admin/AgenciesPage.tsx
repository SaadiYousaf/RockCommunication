import { useEffect, useMemo, useState } from "react";
import {
  useAssignAgencyCeoMutation,
  useCreateAgencyMutation,
  useListAgenciesQuery,
  useListUsersQuery,
  useRegisterMutation,
  useUpdateAgencyMutation,
} from "../../shared/api/baseApi";
import type { AgencyDto, UserSummary } from "../../shared/api/types";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Stat, useToast,
} from "../../shared/ui";

/**
 * SuperAdmin-only agency (call-center) management. Lists every tenant in the system,
 * lets the global admin provision new ones, toggle their active state, and assign a CEO.
 * The matching API is locked behind `agencies.manage` / `agencies.create` and the
 * route guard requires the SuperAdmin role.
 */
export function AgenciesPage() {
  const { data: agencies, isLoading } = useListAgenciesQuery({ includeInactive: true });
  const [createAgency, { isLoading: creating }] = useCreateAgencyMutation();
  const [updateAgency] = useUpdateAgencyMutation();
  const [assignCeo] = useAssignAgencyCeoMutation();
  const toast = useToast();

  const [name, setName] = useState("");
  const [code, setCode] = useState("");

  const [editing, setEditing] = useState<AgencyDto | null>(null);
  const [editName, setEditName] = useState("");
  const [editCode, setEditCode] = useState("");
  const [editActive, setEditActive] = useState(true);

  const [assigning, setAssigning] = useState<AgencyDto | null>(null);

  const stats = useMemo(() => {
    const list = agencies ?? [];
    return {
      total: list.length,
      active: list.filter((a) => a.isActive).length,
      withoutCeo: list.filter((a) => a.isActive && !a.ceoUserId).length,
      users: list.reduce((sum, a) => sum + a.userCount, 0),
    };
  }, [agencies]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    try {
      await createAgency({ name: trimmed, code: code.trim() || null }).unwrap();
      toast.success("Call center created", trimmed);
      setName("");
      setCode("");
    } catch (err: any) {
      toast.error("Couldn't create", err?.data?.detail ?? "Try again.");
    }
  }

  function startEdit(a: AgencyDto) {
    setEditing(a);
    setEditName(a.name);
    setEditCode(a.code ?? "");
    setEditActive(a.isActive);
  }

  async function handleUpdate() {
    if (!editing) return;
    try {
      await updateAgency({
        id: editing.id,
        name: editName.trim(),
        code: editCode.trim() || null,
        isActive: editActive,
      }).unwrap();
      toast.success("Updated", editing.name);
      setEditing(null);
    } catch (err: any) {
      toast.error("Couldn't update", err?.data?.detail ?? "Try again.");
    }
  }

  async function toggleActive(a: AgencyDto) {
    try {
      await updateAgency({
        id: a.id,
        name: a.name,
        code: a.code,
        isActive: !a.isActive,
      }).unwrap();
      toast.success(a.isActive ? "Disabled" : "Enabled", a.name);
    } catch (err: any) {
      toast.error("Couldn't update", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Administration"
        title="Call Centers"
        description="Provision new agencies and assign their CEO. Each call center is a fully isolated tenant."
        breadcrumbs={[{ label: "Admin" }, { label: "Call Centers" }]}
        badge={
          <Badge tone="brand" variant="soft">
            <Icon name="shield" size={11} className="-ml-0.5" /> SuperAdmin
          </Badge>
        }
      />

      {/* Top stats — at-a-glance health of the multi-tenant fleet */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 mb-5">
        <Stat
          label="Total"
          value={stats.total}
          icon={<Icon name="building" size={18} />}
          hint="Tenants in this environment"
          tone="brand"
        />
        <Stat
          label="Active"
          value={stats.active}
          icon={<Icon name="check" size={18} />}
          hint={`${stats.total - stats.active} disabled`}
          tone="success"
        />
        <Stat
          label="Total users"
          value={stats.users}
          icon={<Icon name="users" size={18} />}
          hint="Across all call centers"
          tone="accent"
        />
        <Stat
          label="Awaiting CEO"
          value={stats.withoutCeo}
          icon={<Icon name="flag" size={18} />}
          hint={stats.withoutCeo === 0 ? "All assigned" : "Action required"}
          tone={stats.withoutCeo === 0 ? "success" : "warning"}
        />
      </div>

      {/* Inline create form (matches AdminPage / verticals pattern) */}
      <Card accent="brand" className="mb-4">
        <CardHeader
          eyebrow="Provision"
          title="New call center"
          subtitle="Names must be unique. Optional short code (e.g. PHX) helps you spot the agency in logs."
          bordered
        />
        <CardBody>
          <form className="flex flex-wrap gap-2" onSubmit={handleCreate}>
            <Input
              leftIcon={<Icon name="building" size={14} />}
              placeholder="Call center name (e.g. Phoenix Operations)"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              containerClassName="flex-1 min-w-[260px]"
            />
            <Input
              placeholder="Short code"
              value={code}
              onChange={(e) => setCode(e.target.value.toUpperCase())}
              containerClassName="w-32"
            />
            <Button leftIcon={<Icon name="plus" size={14} />} loading={creating}>
              Create
            </Button>
          </form>
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          eyebrow="Tenants"
          title="Agencies"
          subtitle="Each row is one tenant. Click a row to edit, or use Assign CEO to set the agency owner."
          bordered
          action={
            agencies && agencies.length > 0 ? (
              <Badge tone="neutral" variant="soft">
                {agencies.length} {agencies.length === 1 ? "agency" : "agencies"}
              </Badge>
            ) : undefined
          }
        />
        <CardBody className="px-0 pt-0">
          {isLoading ? (
            <div className="px-5 py-4">
              <Skeleton className="h-24" />
            </div>
          ) : !agencies || agencies.length === 0 ? (
            <div className="px-5 py-4">
              <EmptyState
                icon={<Icon name="building" size={20} />}
                title="No call centers yet"
                description="Use the form above to create your first one."
              />
            </div>
          ) : (
            <ul className="divide-y hairline">
              {agencies.map((a) => (
                <li key={a.id} className="flex items-center gap-3 px-5 py-3">
                  <div
                    className={`h-10 w-10 rounded-xl grid place-items-center shrink-0 ${
                      a.isActive
                        ? "bg-brand-50 text-brand-600"
                        : "bg-ink-100 text-ink-400"
                    }`}
                  >
                    <Icon name="building" size={18} />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="font-medium text-ink-900 truncate">{a.name}</span>
                      {a.code && (
                        <code className="text-[11px] font-mono px-1.5 py-0.5 rounded bg-ink-100 text-ink-600">
                          {a.code}
                        </code>
                      )}
                      {a.isActive ? (
                        <Badge tone="success" variant="soft">Active</Badge>
                      ) : (
                        <Badge tone="neutral" variant="soft">Inactive</Badge>
                      )}
                    </div>
                    <div className="mt-1 flex items-center gap-3 text-xs text-ink-500">
                      <span className="inline-flex items-center gap-1">
                        <Icon name="users" size={12} />
                        {a.userCount} {a.userCount === 1 ? "user" : "users"}
                      </span>
                      <span className="inline-flex items-center gap-1">
                        <Icon name="shield" size={12} />
                        {a.ceoUserName ? (
                          <>CEO: <span className="text-ink-700">{a.ceoUserName}</span></>
                        ) : (
                          <span className="text-amber-600">CEO unassigned</span>
                        )}
                      </span>
                      <span className="inline-flex items-center gap-1">
                        <Icon name="clock" size={12} />
                        Created {new Date(a.createdAt).toLocaleDateString()}
                      </span>
                    </div>
                  </div>
                  <div className="flex gap-1.5 shrink-0">
                    <Button
                      variant="ghost"
                      size="sm"
                      leftIcon={<Icon name="users" size={13} />}
                      onClick={() => setAssigning(a)}
                    >
                      {a.ceoUserId ? "Change CEO" : "Assign CEO"}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      leftIcon={<Icon name="cog" size={13} />}
                      onClick={() => startEdit(a)}
                    >
                      Edit
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      className={a.isActive ? "text-rose-600 hover:bg-rose-50" : "text-emerald-600 hover:bg-emerald-50"}
                      onClick={() => toggleActive(a)}
                    >
                      {a.isActive ? "Disable" : "Enable"}
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>

      {/* Edit modal */}
      <Modal open={!!editing} onClose={() => setEditing(null)} title={`Edit ${editing?.name ?? ""}`}>
        <div className="space-y-3">
          <Input label="Name" value={editName} onChange={(e) => setEditName(e.target.value)} />
          <Input label="Short code" value={editCode} onChange={(e) => setEditCode(e.target.value.toUpperCase())} />
          <label className="flex items-center gap-2 text-sm text-ink-700">
            <input
              type="checkbox"
              checked={editActive}
              onChange={(e) => setEditActive(e.target.checked)}
              className="h-4 w-4 rounded border-ink-300"
            />
            Active
          </label>
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
            <Button onClick={handleUpdate} disabled={!editName.trim()}>Save</Button>
          </div>
        </div>
      </Modal>

      {/* Assign CEO modal */}
      <AssignCeoModal
        agency={assigning}
        onClose={() => setAssigning(null)}
        onAssign={async (userId) => {
          if (!assigning) return;
          try {
            await assignCeo({ id: assigning.id, userId }).unwrap();
            toast.success("CEO assigned", assigning.name);
            setAssigning(null);
          } catch (err: any) {
            toast.error("Couldn't assign", err?.data?.detail ?? "Try again.");
          }
        }}
      />
    </>
  );
}

function AssignCeoModal({
  agency,
  onClose,
  onAssign,
}: {
  agency: AgencyDto | null;
  onClose: () => void;
  onAssign: (userId: string) => Promise<void>;
}) {
  // Scope the candidate list to THIS agency on the server. SuperAdmins are the
  // only callers that see this modal so we can safely pass agencyId — backend
  // enforces tenant boundaries either way.
  const { data: users, isLoading } = useListUsersQuery(
    agency ? { agencyId: agency.id } : undefined,
    { skip: !agency },
  );
  // Global user list — only used for the "register new CEO" path so we can warn
  // about email/username collisions across agencies before the API rejects them.
  const { data: allUsers } = useListUsersQuery(undefined, { skip: !agency });
  const [register, { isLoading: registering }] = useRegisterMutation();
  const toast = useToast();

  // Two modes — pick from existing users in the agency, or register a brand-new
  // CEO inline. The first agency a SuperAdmin creates always has zero users, so
  // the "register" path needs to be inside this same modal — sending the user to
  // the global registration form would lose context.
  const [mode, setMode] = useState<"pick" | "register">("pick");
  const [selected, setSelected] = useState<string>("");
  const [reg, setReg] = useState({ userName: "", email: "" });

  // Server already filtered, but keep a defensive client-side filter in case
  // someone calls the endpoint without the parameter (e.g. an Admin bypass).
  const candidates: UserSummary[] = (users ?? []).filter((u) => u.agencyId === agency?.id);
  const current = candidates.find((u) => u.id === agency?.ceoUserId);

  // When the modal opens for a different agency, default to whichever mode makes
  // sense (register-first if there are no users yet) and clear stale form state.
  useEffect(() => {
    if (!agency) return;
    setMode(candidates.length === 0 ? "register" : "pick");
    setSelected("");
    setReg({ userName: "", email: "" });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [agency?.id, candidates.length === 0]);

  // Pre-flight collision detection — case-insensitive match on either field.
  // If we find a hit, the submit button is disabled and we render an inline
  // banner with the most actionable next step (promote, or pick a different email).
  const collision: { user: UserSummary; field: "email" | "userName"; sameAgency: boolean } | null = useMemo(() => {
    if (!agency) return null;
    const list = allUsers ?? [];
    const email = reg.email.trim().toLowerCase();
    const userName = reg.userName.trim().toLowerCase();
    for (const u of list) {
      const matchEmail = email && u.email.toLowerCase() === email;
      const matchName = userName && u.userName.toLowerCase() === userName;
      if (matchEmail || matchName) {
        return {
          user: u,
          field: matchEmail ? "email" : "userName",
          sameAgency: u.agencyId === agency.id,
        };
      }
    }
    return null;
  }, [allUsers, agency, reg.email, reg.userName]);

  async function handleRegisterAndAssign() {
    if (!agency) return;
    const userName = reg.userName.trim();
    const email = reg.email.trim();
    if (!userName || !email) return;

    // If they typed the email/username of an existing user — same agency OR
    // another agency — short-circuit the registration and just call assignCeo.
    // The backend now handles cross-agency moves: it relocates the user to this
    // tenant and promotes them. SuperAdmin is the only caller that can hit this.
    if (collision) {
      try {
        await onAssign(collision.user.id);
        return;
      } catch (err: any) {
        toast.error(
          collision.sameAgency ? "Couldn't promote" : "Couldn't move user",
          err?.data?.detail ?? "Try again.",
        );
        return;
      }
    }

    try {
      const created = await register({
        userName, email,
        password: null,         // server generates a strong temp password and emails the invite
        agencyId: agency.id,
        roles: ["CEO"],
      }).unwrap();
      await onAssign(created.id);
    } catch (err: any) {
      const status = err?.status ?? err?.data?.status;
      const detail: string = err?.data?.detail ?? err?.data?.title ?? "";
      if (status === 409 || /already exists/i.test(detail)) {
        toast.error(
          "User already exists",
          "Someone with that username or email is already registered. Try a different one or use 'Pick existing user'.",
        );
      } else if (status === 400) {
        toast.error("Invalid details", detail || "Check the username and email format.");
      } else {
        toast.error("Couldn't register CEO", detail || "Try again.");
      }
    }
  }

  const emailLooksValid = /\S+@\S+\.\S+/.test(reg.email.trim());
  const userNameLooksValid = reg.userName.trim().length >= 3;
  // We always allow submit when there's a collision — the action becomes "promote"
  // (same agency) or "move & assign" (different agency) instead of "register".
  const canSubmitRegister = collision !== null
    ? true
    : userNameLooksValid && emailLooksValid;

  return (
    <Modal open={!!agency} onClose={onClose} title={`Assign CEO — ${agency?.name ?? ""}`} size="lg">
      <div className="space-y-4">
        {current && (
          <div className="rounded-lg border border-brand-100 bg-brand-50/60 px-3 py-2.5 flex items-center gap-3">
            <Avatar name={current.userName} size={32} />
            <div className="flex-1 min-w-0">
              <div className="font-semibold text-ink-900 truncate">{current.userName}</div>
              <div className="text-xs text-ink-500 truncate">{current.email} · Current CEO</div>
            </div>
            <Badge tone="brand" variant="soft" size="sm">
              <Icon name="shield" size={11} className="mr-1" />Active
            </Badge>
          </div>
        )}

        {/* Mode toggle — only meaningful when there's at least one existing user to pick from. */}
        {candidates.length > 0 && (
          <div className="inline-flex rounded-lg bg-ink-100/70 p-1 ring-1 ring-ink-200">
            <button
              type="button"
              onClick={() => setMode("pick")}
              className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors ${
                mode === "pick" ? "bg-white text-ink-900 shadow-xs" : "text-ink-600 hover:text-ink-900"
              }`}
            >
              <Icon name="users" size={12} className="inline mr-1.5" />
              Pick existing user
            </button>
            <button
              type="button"
              onClick={() => setMode("register")}
              className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors ${
                mode === "register" ? "bg-white text-ink-900 shadow-xs" : "text-ink-600 hover:text-ink-900"
              }`}
            >
              <Icon name="userPlus" size={12} className="inline mr-1.5" />
              Register new CEO
            </button>
          </div>
        )}

        {isLoading ? (
          <Skeleton className="h-24" />
        ) : mode === "pick" && candidates.length > 0 ? (
          <Select
            label="Promote user to CEO"
            value={selected}
            onChange={(e) => setSelected(e.target.value)}
          >
            <option value="">— select user —</option>
            {candidates.map((u) => (
              <option key={u.id} value={u.id}>
                {u.userName} · {u.email}
              </option>
            ))}
          </Select>
        ) : (
          <div className="space-y-3 rounded-xl border hairline p-4 bg-gradient-to-b from-brand-50/40 to-white">
            <div className="flex items-start gap-3">
              <div className="h-10 w-10 rounded-lg grid place-items-center bg-brand-100 text-brand-700 ring-1 ring-inset ring-brand-200 shrink-0">
                <Icon name="userPlus" size={18} />
              </div>
              <div>
                <div className="text-sm font-semibold text-ink-900">
                  {candidates.length === 0 ? "First user for this agency" : "Create a new CEO"}
                </div>
                <div className="text-xs text-ink-500">
                  We'll email them a temporary password. They'll be prompted to change it on first sign-in.
                </div>
              </div>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Input
                label="Username" required
                placeholder="e.g. ceo.aurora"
                value={reg.userName}
                onChange={(e) => setReg((r) => ({ ...r, userName: e.target.value }))}
                hint={reg.userName && !userNameLooksValid ? "Use at least 3 characters." : undefined}
              />
              <Input
                label="Email" type="email" required
                placeholder="ceo@example.com"
                leftIcon={<Icon name="mail" size={14} />}
                value={reg.email}
                onChange={(e) => setReg((r) => ({ ...r, email: e.target.value }))}
                hint={reg.email && !emailLooksValid ? "Looks like an invalid email." : undefined}
              />
            </div>

            {/* Collision banner — same agency: offer promote; other agency: hard block. */}
            {collision && collision.sameAgency && (
              <div className="rounded-lg ring-1 ring-amber-200 bg-amber-50 p-3 flex items-start gap-3">
                <div className="h-8 w-8 rounded-md grid place-items-center bg-amber-100 text-amber-700 ring-1 ring-inset ring-amber-200 shrink-0">
                  <Icon name="info" size={14} />
                </div>
                <div className="flex-1 min-w-0 text-xs">
                  <div className="font-semibold text-amber-900">
                    This {collision.field === "email" ? "email" : "username"} already exists in this agency
                  </div>
                  <div className="text-amber-800 mt-0.5">
                    <span className="font-medium">{collision.user.userName}</span> ({collision.user.email}) is already a member.
                    Click <span className="font-semibold">Promote &amp; assign</span> to make them CEO instead of creating a new account.
                  </div>
                </div>
              </div>
            )}
            {collision && !collision.sameAgency && (
              <div className="rounded-lg ring-1 ring-amber-200 bg-amber-50 p-3 flex items-start gap-3">
                <div className="h-8 w-8 rounded-md grid place-items-center bg-amber-100 text-amber-700 ring-1 ring-inset ring-amber-200 shrink-0">
                  <Icon name="info" size={14} />
                </div>
                <div className="flex-1 min-w-0 text-xs">
                  <div className="font-semibold text-amber-900">
                    {collision.field === "email" ? "Email" : "Username"} already exists in another agency
                  </div>
                  <div className="text-amber-800 mt-0.5">
                    <span className="font-medium">{collision.user.userName}</span> ({collision.user.email}) currently belongs to a different tenant.
                    Click <span className="font-semibold">Move &amp; assign</span> to relocate them here as CEO. Their old team membership will be cleared.
                  </div>
                </div>
              </div>
            )}
          </div>
        )}

        <p className="text-xs text-ink-500 leading-relaxed">
          The chosen user keeps their existing roles and gets the{" "}
          <code className="px-1 py-0.5 rounded bg-ink-100 text-ink-700 font-mono text-[10.5px]">CEO</code> role added.
          Any other user in this agency will have their CEO role removed.
        </p>

        <div className="flex justify-end gap-2 pt-2">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          {mode === "pick" && candidates.length > 0 ? (
            <Button
              onClick={() => selected && onAssign(selected)}
              disabled={!selected}
              leftIcon={<Icon name="check" size={14} />}
            >
              Assign
            </Button>
          ) : (
            <Button
              onClick={handleRegisterAndAssign}
              disabled={!canSubmitRegister}
              loading={registering}
              leftIcon={
                <Icon
                  name={
                    collision?.sameAgency ? "check"
                    : collision ? "arrowRight"
                    : "userPlus"
                  }
                  size={14}
                />
              }
            >
              {collision?.sameAgency
                ? "Promote & assign"
                : collision
                  ? "Move & assign"
                  : "Register & assign"}
            </Button>
          )}
        </div>
      </div>
    </Modal>
  );
}
