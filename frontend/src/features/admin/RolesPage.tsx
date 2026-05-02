import { useEffect, useMemo, useState } from "react";
import {
  useCreateRoleMutation, useDeleteRoleMutation, useListModulesQuery,
  useListRolesQuery, useRenameRoleMutation, useSetRoleModulesMutation,
  useListPermissionsQuery, useRolePermissionsQuery, useSetRolePermissionsMutation,
} from "../../shared/api/baseApi";
import type { AppModuleDto, RoleDto } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Stat, useToast, cn,
} from "../../shared/ui";
import { usePermission, Perm } from "../../shared/auth/permissions";

/**
 * Role Management.
 * Admin creates roles with arbitrary names, ticks modules each role can access,
 * renames or deletes them. Users who hold the role automatically gain visibility
 * of those modules in their sidebar and route guards.
 */
export function RolesPage() {
  const { data: roles, isLoading: rolesLoading } = useListRolesQuery();
  const { data: modules, isLoading: modulesLoading } = useListModulesQuery();
  const [createRole, { isLoading: creating }] = useCreateRoleMutation();
  const [renameRole] = useRenameRoleMutation();
  const [setRoleModules, { isLoading: saving }] = useSetRoleModulesMutation();
  const [deleteRole] = useDeleteRoleMutation();
  const toast = useToast();

  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState("");
  const [newModules, setNewModules] = useState<string[]>([]);

  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const [draftModules, setDraftModules] = useState<string[]>([]);
  const [editingName, setEditingName] = useState("");
  const [search, setSearch] = useState("");

  const [confirmDelete, setConfirmDelete] = useState<RoleDto | null>(null);

  const canEditModules = usePermission(Perm.RolesManage);
  const canEditPermissions = usePermission(Perm.PermissionsManage);

  // Group modules by their Group field for a tidy checklist
  const grouped = useMemo(() => {
    const map = new Map<string, AppModuleDto[]>();
    for (const m of modules ?? []) {
      const arr = map.get(m.group) ?? [];
      arr.push(m);
      map.set(m.group, arr);
    }
    return Array.from(map.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [modules]);

  const selected = useMemo(
    () => (roles ?? []).find((r) => r.id === selectedRoleId) ?? null,
    [roles, selectedRoleId]
  );

  // Sync draft when selection or fetched data changes
  useEffect(() => {
    if (selected) {
      setDraftModules(selected.modules);
      setEditingName(selected.name);
    } else {
      setDraftModules([]);
      setEditingName("");
    }
  }, [selected?.id, selected?.modules.join("|")]);

  const dirty = useMemo(() => {
    if (!selected) return false;
    const a = [...selected.modules].sort().join("|");
    const b = [...draftModules].sort().join("|");
    return a !== b;
  }, [selected, draftModules]);

  const filteredRoles = useMemo(() => {
    const q = search.trim().toLowerCase();
    const list = roles ?? [];
    if (!q) return list;
    return list.filter((r) => r.name.toLowerCase().includes(q));
  }, [roles, search]);

  const stats = useMemo(() => {
    const list = roles ?? [];
    return {
      total: list.length,
      system: list.filter((r) => r.isSystem).length,
      custom: list.filter((r) => !r.isSystem).length,
      modules: modules?.length ?? 0,
    };
  }, [roles, modules]);

  function toggleModule(list: string[], setList: (l: string[]) => void, code: string) {
    setList(list.includes(code) ? list.filter((c) => c !== code) : [...list, code]);
  }

  function toggleGroup(list: string[], setList: (l: string[]) => void, group: AppModuleDto[]) {
    const codes = group.map((m) => m.code);
    const allOn = codes.every((c) => list.includes(c));
    setList(allOn ? list.filter((c) => !codes.includes(c)) : Array.from(new Set([...list, ...codes])));
  }

  async function handleCreate() {
    if (!newName.trim()) return;
    try {
      const created = await createRole({ name: newName.trim(), moduleCodes: newModules }).unwrap();
      toast.success("Role created", `${created.name} is ready to assign.`);
      setShowCreate(false);
      setNewName("");
      setNewModules([]);
      setSelectedRoleId(created.id);
    } catch (err: any) {
      toast.error("Could not create role", err?.data?.detail ?? "");
    }
  }

  async function handleSaveModules() {
    if (!selected) return;
    try {
      await setRoleModules({ id: selected.id, moduleCodes: draftModules }).unwrap();
      toast.success("Saved", `Module access updated for ${selected.name}.`);
    } catch (err: any) {
      toast.error("Save failed", err?.data?.detail ?? "");
    }
  }

  async function handleRename() {
    if (!selected || editingName.trim() === selected.name) return;
    try {
      await renameRole({ id: selected.id, name: editingName.trim() }).unwrap();
      toast.success("Renamed");
    } catch (err: any) {
      toast.error("Rename failed", err?.data?.detail ?? "");
    }
  }

  async function handleDelete() {
    if (!confirmDelete) return;
    try {
      await deleteRole(confirmDelete.id).unwrap();
      toast.success("Role deleted", confirmDelete.name);
      setConfirmDelete(null);
      if (selectedRoleId === confirmDelete.id) setSelectedRoleId(null);
    } catch (err: any) {
      toast.error("Delete failed", err?.data?.detail ?? "");
    }
  }

  return (
    <>
      <PageHeader
        title="Role management"
        description="Create roles with any name and grant access to specific modules. Users with the role automatically see only the modules you tick."
        actions={
          canEditModules ? (
            <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setShowCreate(true)}>
              New role
            </Button>
          ) : undefined
        }
      />

      {/* KPI strip */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Total roles" value={stats.total} icon={<Icon name="shield" size={16} />} tone="brand" />
        <Stat label="System roles" value={stats.system} icon={<Icon name="lock" size={16} />} tone="neutral" hint="Built-in templates, immutable" />
        <Stat label="Custom roles" value={stats.custom} icon={<Icon name="userCog" size={16} />} tone="accent" hint="Created by admins" />
        <Stat label="Modules" value={stats.modules} icon={<Icon name="layers" size={16} />} tone="success" hint="Assignable to roles" />
      </div>

      <div className="grid xl:grid-cols-[400px_1fr] gap-5">
        {/* ── Roles list ────────────────────────────────────────────────── */}
        <Card className="self-start xl:sticky xl:top-4">
          <div className="px-4 pt-4 pb-3 border-b hairline">
            <Input
              leftIcon={<Icon name="search" size={15} />}
              placeholder="Search roles…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <CardBody className="p-2">
            {rolesLoading ? (
              <div className="space-y-2 p-2">
                {[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-14 rounded-lg" />)}
              </div>
            ) : filteredRoles.length === 0 ? (
              <div className="py-10">
                <EmptyState
                  icon={<Icon name="search" size={20} />}
                  title={search ? "No matching roles" : "No roles yet"}
                  description={search ? "Try a different name." : "Create one to get started."}
                />
              </div>
            ) : (
              <div className="space-y-1">
                {filteredRoles.map((r) => {
                  const active = selectedRoleId === r.id;
                  const coverage = stats.modules > 0 ? Math.round((r.modules.length / stats.modules) * 100) : 0;
                  return (
                    <button
                      key={r.id}
                      onClick={() => setSelectedRoleId(r.id)}
                      className={cn(
                        "w-full text-left flex items-center gap-3 p-3 rounded-xl transition-all",
                        active
                          ? "bg-brand-50 ring-1 ring-brand-200 shadow-[0_1px_3px_0_rgba(14,165,233,0.06)]"
                          : "hover:bg-ink-50/80 ring-1 ring-transparent",
                      )}
                    >
                      <div
                        className={cn(
                          "h-9 w-9 rounded-lg grid place-items-center shrink-0 ring-1 ring-inset transition-colors",
                          r.isSystem
                            ? active ? "bg-brand-100 text-brand-700 ring-brand-200" : "bg-ink-100 text-ink-600 ring-ink-200"
                            : active ? "bg-violet-100 text-violet-700 ring-violet-200" : "bg-violet-50 text-violet-600 ring-violet-100",
                        )}
                      >
                        <Icon name={r.isSystem ? "shield" : "userCog"} size={16} />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className={cn("font-semibold truncate", active ? "text-brand-900" : "text-ink-900")}>
                            {r.name}
                          </span>
                          {r.isSystem && (
                            <Badge tone="neutral" variant="soft" size="sm">system</Badge>
                          )}
                        </div>
                        <div className="mt-1.5 flex items-center gap-2">
                          <div className="flex-1 h-1 bg-ink-100 rounded-full overflow-hidden">
                            <div
                              className={cn(
                                "h-full rounded-full transition-all",
                                active ? "bg-brand-500" : r.isSystem ? "bg-ink-400" : "bg-violet-500",
                              )}
                              style={{ width: `${coverage}%` }}
                            />
                          </div>
                          <span className="text-[11px] tabular-nums text-ink-500 font-medium">
                            {r.modules.length}<span className="text-ink-400">/{stats.modules}</span>
                          </span>
                        </div>
                      </div>
                      <Icon
                        name="chevronRight"
                        size={14}
                        className={cn("shrink-0 transition-all", active ? "text-brand-600 translate-x-0.5" : "text-ink-300")}
                      />
                    </button>
                  );
                })}
              </div>
            )}
          </CardBody>
        </Card>

        {/* ── Editor ────────────────────────────────────────────────────── */}
        {!selected ? (
          <Card>
            <CardBody className="py-16">
              <EmptyState
                icon={<Icon name="shield" size={24} />}
                title="Pick a role to manage"
                description="Select a role on the left to edit its module access and permissions."
                action={
                  canEditModules ? (
                    <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setShowCreate(true)}>
                      Create new role
                    </Button>
                  ) : undefined
                }
              />
            </CardBody>
          </Card>
        ) : (
          <div className="space-y-5">
            {/* Detail header */}
            <Card>
              <CardBody className="p-5">
                <div className="flex items-start gap-4 flex-wrap">
                  <div
                    className={cn(
                      "h-12 w-12 rounded-xl grid place-items-center shrink-0 ring-1 ring-inset",
                      selected.isSystem
                        ? "bg-brand-50 text-brand-600 ring-brand-100"
                        : "bg-violet-50 text-violet-600 ring-violet-100",
                    )}
                  >
                    <Icon name={selected.isSystem ? "shield" : "userCog"} size={22} />
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <h2 className="text-xl font-semibold text-ink-900">{selected.name}</h2>
                      {selected.isSystem
                        ? <Badge tone="neutral" variant="soft"><Icon name="lock" size={11} className="mr-1" />System role</Badge>
                        : <Badge tone="accent" variant="soft">Custom role</Badge>}
                    </div>
                    <div className="text-sm text-ink-500 mt-1">
                      {selected.modules.length} module{selected.modules.length === 1 ? "" : "s"} · {stats.modules > 0 ? Math.round((selected.modules.length / stats.modules) * 100) : 0}% coverage
                    </div>
                  </div>

                  {!selected.isSystem && (
                    <Button
                      variant="ghost"
                      leftIcon={<Icon name="trash" size={15} />}
                      onClick={() => setConfirmDelete(selected)}
                      disabled={!canEditModules}
                      className="text-rose-600 hover:bg-rose-50"
                    >
                      Delete role
                    </Button>
                  )}
                </div>

                {!selected.isSystem && (
                  <div className="flex items-end gap-2 mt-4 pt-4 border-t hairline">
                    <Input
                      label="Role name"
                      value={editingName}
                      onChange={(e) => setEditingName(e.target.value)}
                      disabled={selected.isSystem}
                      className="flex-1"
                    />
                    <Button
                      variant="outline"
                      onClick={handleRename}
                      disabled={!canEditModules || selected.isSystem || editingName.trim() === selected.name || !editingName.trim()}
                    >
                      Save name
                    </Button>
                  </div>
                )}
              </CardBody>
            </Card>

            {/* Modules access */}
            <Card>
              <CardBody className="p-0">
                <div className="flex items-start justify-between gap-4 p-5 border-b hairline flex-wrap">
                  <div className="flex items-start gap-3">
                    <div className="h-9 w-9 rounded-lg grid place-items-center bg-brand-50 text-brand-600 ring-1 ring-inset ring-brand-100 shrink-0">
                      <Icon name="grid" size={16} />
                    </div>
                    <div>
                      <div className="text-base font-semibold text-ink-900">Module access</div>
                      <div className="text-xs text-ink-500 mt-0.5 max-w-xl">
                        Tick the modules this role can see. Users will only get sidebar entries and routes for ticked modules.
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    {dirty && <Badge tone="warning" variant="soft" dot>Unsaved changes</Badge>}
                    <Button
                      onClick={handleSaveModules}
                      disabled={!dirty || !canEditModules}
                      loading={saving}
                      leftIcon={<Icon name="save" size={15} />}
                    >
                      Save modules
                    </Button>
                  </div>
                </div>

                {!canEditModules && (
                  <div className="px-5 py-2 text-xs text-amber-700 bg-amber-50 border-b border-amber-100">
                    <Icon name="lock" size={12} className="inline-block mr-1" />
                    Read-only — you don't have <code className="font-mono">roles.manage</code>.
                  </div>
                )}

                {modulesLoading ? (
                  <div className="p-5"><Skeleton className="h-48" /></div>
                ) : (
                  <div className="p-5 space-y-3">
                    {grouped.map(([group, items]) => {
                      const allOn = items.every((m) => draftModules.includes(m.code));
                      const someOn = !allOn && items.some((m) => draftModules.includes(m.code));
                      const onCount = items.filter((m) => draftModules.includes(m.code)).length;
                      return (
                        <div key={group} className="rounded-xl border hairline overflow-hidden bg-white">
                          <div className="flex items-center justify-between px-4 py-2.5 bg-gradient-to-b from-ink-50/60 to-white border-b hairline">
                            <div className="flex items-center gap-2">
                              <div className="text-[11px] font-semibold uppercase tracking-[0.08em] text-ink-700">
                                {group}
                              </div>
                              <Badge tone={onCount === items.length ? "brand" : onCount === 0 ? "neutral" : "info"} variant="soft" size="sm">
                                {onCount}/{items.length}
                              </Badge>
                            </div>
                            <label className={cn(
                              "text-xs inline-flex items-center gap-2 select-none",
                              canEditModules ? "text-ink-600 cursor-pointer hover:text-ink-900" : "text-ink-400 cursor-not-allowed",
                            )}>
                              <input
                                type="checkbox"
                                checked={allOn}
                                ref={(el) => { if (el) el.indeterminate = someOn; }}
                                onChange={() => canEditModules && toggleGroup(draftModules, setDraftModules, items)}
                                disabled={!canEditModules}
                                className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
                              />
                              <span>Toggle all</span>
                            </label>
                          </div>
                          <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-1 p-2">
                            {items.map((m) => {
                              const checked = draftModules.includes(m.code);
                              return (
                                <label
                                  key={m.code}
                                  className={cn(
                                    "flex items-center gap-2.5 px-2.5 py-2 rounded-lg transition-colors",
                                    canEditModules ? "hover:bg-brand-50/60 cursor-pointer" : "opacity-70",
                                    checked && "bg-brand-50/40",
                                  )}
                                >
                                  <input
                                    type="checkbox"
                                    checked={checked}
                                    onChange={() => canEditModules && toggleModule(draftModules, setDraftModules, m.code)}
                                    disabled={!canEditModules}
                                    className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
                                  />
                                  <span className="text-sm text-ink-800 truncate flex-1">{m.name}</span>
                                  <span className="text-[10px] text-ink-400 font-mono shrink-0">{m.code}</span>
                                </label>
                              );
                            })}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </CardBody>
            </Card>

            {/* Permissions */}
            <RolePermissionsPanel roleId={selected.id} canEdit={canEditPermissions} />
          </div>
        )}
      </div>

      {/* Create modal */}
      <Modal open={showCreate} onClose={() => setShowCreate(false)} title="New role">
        <div className="space-y-4">
          <Input
            label="Name"
            placeholder="e.g. Senior Closer"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            autoFocus
          />
          <div>
            <div className="text-sm font-medium text-ink-800 mb-2">Modules</div>
            <div className="max-h-72 overflow-auto space-y-3 pr-1 rounded-lg border hairline p-3 bg-ink-50/40">
              {grouped.map(([group, items]) => (
                <div key={group}>
                  <div className="text-[11px] font-semibold uppercase tracking-[0.08em] text-ink-600 mb-1.5">{group}</div>
                  <div className="grid grid-cols-2 gap-1">
                    {items.map((m) => (
                      <label
                        key={m.code}
                        className="flex items-center gap-2 px-2 py-1 rounded hover:bg-white cursor-pointer text-sm"
                      >
                        <input
                          type="checkbox"
                          checked={newModules.includes(m.code)}
                          onChange={() => toggleModule(newModules, setNewModules, m.code)}
                          className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
                        />
                        <span className="truncate">{m.name}</span>
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="ghost" onClick={() => setShowCreate(false)}>Cancel</Button>
            <Button onClick={handleCreate} loading={creating} disabled={!newName.trim()} leftIcon={<Icon name="plus" size={15} />}>
              Create role
            </Button>
          </div>
        </div>
      </Modal>

      {/* Confirm delete */}
      <Modal open={!!confirmDelete} onClose={() => setConfirmDelete(null)} title="Delete role">
        <div className="space-y-4">
          <div className="flex items-start gap-3 p-3 rounded-lg bg-rose-50 ring-1 ring-rose-100">
            <div className="h-9 w-9 rounded-lg grid place-items-center bg-rose-100 text-rose-600 ring-1 ring-inset ring-rose-200 shrink-0">
              <Icon name="alert" size={16} />
            </div>
            <div className="text-sm text-rose-900">
              Delete <span className="font-semibold">{confirmDelete?.name}</span>? Users assigned to this role will lose
              its module access immediately. This cannot be undone.
            </div>
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setConfirmDelete(null)}>Cancel</Button>
            <Button variant="danger" onClick={handleDelete} leftIcon={<Icon name="trash" size={15} />}>
              Delete role
            </Button>
          </div>
        </div>
      </Modal>
    </>
  );
}

/**
 * Permissions matrix for a role.
 */
function RolePermissionsPanel({ roleId, canEdit }: { roleId: string; canEdit: boolean }) {
  const { data: catalog, isLoading: catLoading } = useListPermissionsQuery();
  const { data: granted, isLoading: grantedLoading } = useRolePermissionsQuery(roleId);
  const [setRolePermissions, { isLoading: saving }] = useSetRolePermissionsMutation();
  const toast = useToast();

  const [draft, setDraft] = useState<string[]>([]);
  useEffect(() => { setDraft(granted ?? []); }, [roleId, (granted ?? []).join("|")]);

  const groupedPerms = useMemo(() => {
    const map = new Map<string, { code: string; group: string }[]>();
    for (const p of catalog ?? []) {
      const arr = map.get(p.group) ?? [];
      arr.push(p);
      map.set(p.group, arr);
    }
    return Array.from(map.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [catalog]);

  const dirty = useMemo(() => {
    const a = [...(granted ?? [])].sort().join("|");
    const b = [...draft].sort().join("|");
    return a !== b;
  }, [granted, draft]);

  function toggle(code: string) {
    setDraft((d) => d.includes(code) ? d.filter((c) => c !== code) : [...d, code]);
  }
  function toggleGroup(items: { code: string }[]) {
    const codes = items.map((i) => i.code);
    const allOn = codes.every((c) => draft.includes(c));
    setDraft(allOn ? draft.filter((c) => !codes.includes(c)) : Array.from(new Set([...draft, ...codes])));
  }

  async function handleSave() {
    try {
      await setRolePermissions({ roleId, permissionCodes: draft }).unwrap();
      toast.success("Permissions saved", "Role grants updated.");
    } catch (err: any) {
      toast.error("Save failed", err?.data?.detail ?? "");
    }
  }

  return (
    <Card>
      <CardBody className="p-0">
        <div className="flex items-start justify-between gap-4 p-5 border-b hairline flex-wrap">
          <div className="flex items-start gap-3">
            <div className="h-9 w-9 rounded-lg grid place-items-center bg-violet-50 text-violet-600 ring-1 ring-inset ring-violet-100 shrink-0">
              <Icon name="key" size={16} />
            </div>
            <div>
              <div className="text-base font-semibold text-ink-900">Permissions</div>
              <div className="text-xs text-ink-500 mt-0.5 max-w-xl">
                Tick the actions this role can perform. <code className="font-mono">*.write</code> codes gate every mutating button across the app.
                Without the write grant, the user only sees read-only.
              </div>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {dirty && <Badge tone="warning" variant="soft" dot>Unsaved changes</Badge>}
            <Button
              onClick={handleSave}
              disabled={!dirty || !canEdit}
              loading={saving}
              leftIcon={<Icon name="save" size={15} />}
            >
              Save permissions
            </Button>
          </div>
        </div>

        {!canEdit && (
          <div className="px-5 py-2 text-xs text-amber-700 bg-amber-50 border-b border-amber-100">
            <Icon name="lock" size={12} className="inline-block mr-1" />
            Read-only — you don't have <code className="font-mono">permissions.manage</code>.
          </div>
        )}

        {catLoading || grantedLoading ? (
          <div className="p-5"><Skeleton className="h-48" /></div>
        ) : (
          <div className="p-5 space-y-3">
            {groupedPerms.map(([group, items]) => {
              const allOn = items.every((p) => draft.includes(p.code));
              const someOn = !allOn && items.some((p) => draft.includes(p.code));
              const onCount = items.filter((p) => draft.includes(p.code)).length;
              return (
                <div key={group} className="rounded-xl border hairline overflow-hidden bg-white">
                  <div className="flex items-center justify-between px-4 py-2.5 bg-gradient-to-b from-violet-50/40 to-white border-b hairline">
                    <div className="flex items-center gap-2">
                      <div className="text-[11px] font-semibold uppercase tracking-[0.08em] text-ink-700">{group}</div>
                      <Badge tone={onCount === items.length ? "accent" : onCount === 0 ? "neutral" : "info"} variant="soft" size="sm">
                        {onCount}/{items.length}
                      </Badge>
                    </div>
                    <label className={cn(
                      "text-xs inline-flex items-center gap-2 select-none",
                      canEdit ? "text-ink-600 cursor-pointer hover:text-ink-900" : "text-ink-400 cursor-not-allowed",
                    )}>
                      <input
                        type="checkbox"
                        checked={allOn}
                        ref={(el) => { if (el) el.indeterminate = someOn; }}
                        onChange={() => canEdit && toggleGroup(items)}
                        disabled={!canEdit}
                        className="rounded border-ink-300 text-violet-600 focus:ring-violet-500"
                      />
                      <span>Toggle all</span>
                    </label>
                  </div>
                  <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-1 p-2">
                    {items.map((p) => {
                      const checked = draft.includes(p.code);
                      return (
                        <label
                          key={p.code}
                          className={cn(
                            "flex items-center gap-2.5 px-2.5 py-2 rounded-lg transition-colors",
                            canEdit ? "hover:bg-violet-50/60 cursor-pointer" : "opacity-70",
                            checked && "bg-violet-50/40",
                          )}
                        >
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={() => canEdit && toggle(p.code)}
                            disabled={!canEdit}
                            className="rounded border-ink-300 text-violet-600 focus:ring-violet-500"
                          />
                          <span className="text-sm text-ink-800 font-mono truncate">{p.code}</span>
                        </label>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </CardBody>
    </Card>
  );
}
