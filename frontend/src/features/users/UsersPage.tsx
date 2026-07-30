import { roleLabel } from "../../shared/constants/roles";
import { useMemo, useState } from "react";
import { useListUsersQuery } from "../../shared/api/baseApi";
import { useTableSort } from "../../shared/hooks/useTableSort";
import {
  Avatar, Badge, Card, CardBody, EmptyState, Icon, InfoHint, PageHeader,
  SearchInput, Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR,
} from "../../shared/ui";

const roleTones: Record<string, "brand" | "info" | "success" | "warning" | "danger" | "neutral"> = {
  Admin: "danger", ProgramManager: "danger", TeamLead: "warning",
  Closer: "success", JrCloser: "success", SelfValidator: "success",
  Validator: "info", Fronter: "brand", Verifier: "brand",
  Followups: "neutral", Correspondence: "neutral", Winbacks: "neutral",
};

export function UsersPage() {
  const { data: users, isLoading, error } = useListUsersQuery();
  const [search, setSearch] = useState("");
  const [filterRole, setFilterRole] = useState<string>("");

  const allRoles = useMemo(() => {
    const set = new Set<string>();
    users?.forEach((u) => u.roles.forEach((r) => set.add(r)));
    return Array.from(set).sort();
  }, [users]);

  const filtered = useMemo(() => {
    if (!users) return [];
    const q = search.trim().toLowerCase();
    return users.filter((u) => {
      if (filterRole && !u.roles.includes(filterRole)) return false;
      if (!q) return true;
      return u.userName.toLowerCase().includes(q) ||
        u.email.toLowerCase().includes(q) ||
        u.roles.some((r) => r.toLowerCase().includes(q));
    });
  }, [users, search, filterRole]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    key: "userName",
    accessors: { role: (u) => u.roles[0] ?? "" },
  });

  const stats = useMemo(() => {
    const items = users ?? [];
    return {
      total: items.length,
      managers: items.filter((u) =>
        u.roles.includes("Admin") || u.roles.includes("ProgramManager") || u.roles.includes("TeamLead")
      ).length,
      agents: items.filter((u) =>
        u.roles.some((r) => ["Fronter", "Verifier", "JrCloser", "Closer", "Validator"].includes(r))
      ).length,
      retention: items.filter((u) =>
        u.roles.some((r) => ["Followups", "Correspondence", "Winbacks"].includes(r))
      ).length,
    };
  }, [users]);

  return (
    <>
      <PageHeader
        title="Users"
        description="Everyone with access to this CRM. Use User Management to edit roles and reset passwords."
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Total users" value={stats.total}     icon={<Icon name="users" size={16} />}     tone="brand" />
        <Stat label="Managers"    value={stats.managers}  icon={<Icon name="shield" size={16} />}    tone="danger" hint="Admin / PM / TL" />
        <Stat label="Agents"      value={stats.agents}    icon={<Icon name="phoneCall" size={16} />} tone="success" hint="Front / Verify / Close" />
        <Stat label="Retention"   value={stats.retention} icon={<Icon name="refresh" size={16} />}   tone="accent" hint="Followup / Winback" />
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
          <Select
            value={filterRole} onChange={(e) => setFilterRole(e.target.value)}
            className="w-44"
          >
            <option value="">All roles</option>
            {allRoles.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}
          </Select>
          {users && (
            <Badge tone="neutral" variant="soft">
              {filtered.length} of {users.length}
            </Badge>
          )}
        </CardBody>
      </Card>

      {error ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="x" size={20} />}
            title="Couldn't load users"
            description="Please refresh the page or contact your admin."
          />
        </CardBody></Card>
      ) : isLoading ? (
        <Card><CardBody>
          {[0, 1, 2, 3, 4].map((i) => (
            <div key={i} className="flex items-center gap-3 py-3 border-b hairline last:border-0">
              <Skeleton className="h-9 w-9 rounded-full" />
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-4 w-56 ml-auto" />
            </div>
          ))}
        </CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="users" size={20} />}
            title={users && users.length === 0 ? "No users yet" : "No users match"}
            description={users && users.length === 0
              ? "Users will appear here once created."
              : "Try a different search or role filter."}
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH sortDir={dirFor("userName")} onClick={() => toggle("userName")}>User</TH>
              <TH sortDir={dirFor("email")} onClick={() => toggle("email")}>Email</TH>
              <TH sortDir={dirFor("role")} onClick={() => toggle("role")}><span className="inline-flex items-center gap-1">Roles<InfoHint title="Roles" side="top">A person's roles decide which modules they can open and what they're allowed to do. Someone with no roles can sign in but can't access anything.</InfoHint></span></TH>
            </TR>
          </THead>
          <TBody>
            {sorted.map((u) => (
              <TR key={u.id}>
                <TD>
                  <div className="flex items-center gap-3">
                    <Avatar name={u.userName} size={36} />
                    <div className="font-medium text-ink-900">{u.userName}</div>
                  </div>
                </TD>
                <TD className="text-ink-600">{u.email}</TD>
                <TD>
                  <div className="flex flex-wrap gap-1">
                    {u.roles.length === 0
                      ? <span className="text-xs text-ink-400">No roles</span>
                      : u.roles.map((r) => (
                        <Badge key={r} tone={roleTones[r] ?? "neutral"} variant="soft">{roleLabel(r)}</Badge>
                      ))}
                  </div>
                </TD>
              </TR>
            ))}
          </TBody>
        </Table>
      )}
    </>
  );
}
