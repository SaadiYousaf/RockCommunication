import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import {
  useGetAgencyQuery, useAgencyCallCentersQuery, useListUsersQuery, useSetUserCallCenterMutation,
} from "../../shared/api/baseApi";
import { roleLabel } from "../../shared/constants/roles";
import {
  Avatar, Badge, Card, CardBody, EmptyState, Icon, InfoHint, PageHeader, Select, Skeleton,
  Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

/**
 * Drill-in for a single call centre (or the agency-wide bucket, id === "none"). Shows ONLY
 * that call centre's staff — the third level of Agencies → agency → call centre → staff.
 */
export function CallCenterDetailPage() {
  const { agencyId = "", callCenterId = "" } = useParams();
  const isAgencyWide = callCenterId === "none";

  const { data: agency } = useGetAgencyQuery(agencyId, { skip: !agencyId });
  const { data: callCenters } = useAgencyCallCentersQuery(agencyId, { skip: !agencyId });
  const { data: people } = useListUsersQuery({ agencyId }, { skip: !agencyId });
  const [setUserCc] = useSetUserCallCenterMutation();
  const toast = useToast();

  const callCenter = useMemo(
    () => (callCenters ?? []).find((c) => c.id === callCenterId),
    [callCenters, callCenterId]);
  const title = isAgencyWide ? "Agency-wide (no call centre)" : callCenter?.name ?? "Call centre";

  const staff = useMemo(
    () => (people ?? []).filter((u) => isAgencyWide ? !u.callCenterId : u.callCenterId === callCenterId),
    [people, callCenterId, isAgencyWide]);

  async function assignCallCentre(userId: string, value: string) {
    try {
      await setUserCc({ userId, callCenterId: value || null }).unwrap();
      toast.success("Assignment saved", value ? "Pinned to the call centre." : "Set to agency-wide.");
    } catch (err: unknown) {
      toast.error("Couldn't assign", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Call centre"
        breadcrumbs={[
          { label: "Agencies", to: "/admin/agencies" },
          { label: agency?.name ?? "Agency", to: `/admin/agencies/${agencyId}` },
          { label: title },
        ]}
        title={title}
        description={isAgencyWide
          ? "Users in this agency who aren't pinned to any call centre."
          : `Staff assigned to ${title}.`}
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-5">
        <Stat label="Staff" value={staff.length} icon={<Icon name="users" size={16} />} tone="brand"
          hint={staff.length === 1 ? "person" : "people"} />
        {!isAgencyWide && callCenter && (
          <Stat label="Leads" value={callCenter.leadCount} icon={<Icon name="list" size={16} />} tone="accent" />
        )}
        {!isAgencyWide && callCenter && (
          <Stat label="Status" value={callCenter.isActive ? "Active" : "Disabled"}
            icon={<Icon name="building" size={16} />} tone={callCenter.isActive ? "success" : "neutral"} />
        )}
      </div>

      <Card>
        <CardBody>
          {!people ? <Skeleton className="h-32" /> : staff.length === 0 ? (
            <EmptyState icon={<Icon name="users" size={20} />} title="No staff here"
              description={isAgencyWide
                ? "Every user is pinned to a call centre."
                : "No one is assigned to this call centre yet — pick it from a user's dropdown to move them here."} />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <THead><TR><TH>User</TH><TH><span className="inline-flex items-center gap-1">Roles<InfoHint title="Roles" side="top">What the user is allowed to do in the system. A user can hold more than one role, and the roles together decide which pages and actions they see.</InfoHint></span></TH><TH><span className="inline-flex items-center gap-1">Call centre<InfoHint title="Call centre" side="left">Pin a user to one call centre to limit their pipeline to just that centre; set agency-wide to let them see the whole agency.</InfoHint></span></TH></TR></THead>
                <TBody>
                  {staff.map((u) => (
                    <TR key={u.id}>
                      <TD>
                        <div className="flex items-center gap-2.5 min-w-0">
                          <Avatar name={u.userName} size={30} />
                          <div className="leading-tight min-w-0">
                            <div className="font-medium text-ink-900 truncate" title={u.userName}>{u.userName}</div>
                            <div className="text-xs text-ink-500 truncate" title={u.email}>{u.email}</div>
                          </div>
                        </div>
                      </TD>
                      <TD>
                        <div className="flex flex-wrap gap-1">
                          {u.roles.map((r) => <Badge key={r} tone="neutral" variant="soft">{roleLabel(r)}</Badge>)}
                        </div>
                      </TD>
                      <TD>
                        <Select aria-label={`Call centre for ${u.userName}`} className="w-52"
                          value={u.callCenterId ?? ""} onChange={(e) => assignCallCentre(u.id, e.target.value)}>
                          <option value="">Agency-wide (no call centre)</option>
                          {(callCenters ?? []).map((c) => (
                            <option key={c.id} value={c.id}>{c.name}{c.isActive ? "" : " (disabled)"}</option>
                          ))}
                        </Select>
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            </div>
          )}
        </CardBody>
      </Card>

      <div className="mt-4">
        <Link to={`/admin/agencies/${agencyId}`} className="text-sm text-brand-600 hover:underline inline-flex items-center gap-1">
          <Icon name="chevronLeft" size={14} /> Back to {agency?.name ?? "agency"}
        </Link>
      </div>
    </>
  );
}
