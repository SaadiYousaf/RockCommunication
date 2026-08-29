import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { ADMIN_MSG } from "./messages";
import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import {
  useGetAgencyQuery, useAgencyCallCentersQuery, useListUsersQuery, useSetUserCallCenterMutation,
} from "../../shared/api/baseApi";
import { roleLabel } from "../../shared/constants/roles";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import {
  Avatar, Badge, BulkActionBar, Card, CardBody, Checkbox, EmptyState, ErrorState, Icon, InfoHint, PageHeader, Select, Skeleton,
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
  const { data: people, isLoading: peopleLoading, isError, error, refetch } =
    useListUsersQuery({ agencyId }, { skip: !agencyId });
  const [setUserCc] = useSetUserCallCenterMutation();
  const toast = useToast();

  const callCenter = useMemo(
    () => (callCenters ?? []).find((c) => c.id === callCenterId),
    [callCenters, callCenterId]);
  const title = isAgencyWide ? "Agency-wide (no call centre)" : callCenter?.name ?? "Call centre";

  const staff = useMemo(
    () => (people ?? []).filter((u) => isAgencyWide ? !u.callCenterId : u.callCenterId === callCenterId),
    [people, callCenterId, isAgencyWide]);

  const sel = useRowSelection(staff.map((u) => u.id));

  function exportSelected() {
    const chosen = staff.filter((u) => sel.isSelected(u.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (u) => u.userName },
      { header: "Email", value: (u) => u.email },
      { header: "Roles", value: (u) => u.roles.map((r) => roleLabel(r)).join("; ") },
    ], `staff-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(ADMIN_MSG.common.exportReady, ADMIN_MSG.common.exportReadyDesc(chosen.length));
  }

  async function assignCallCentre(userId: string, value: string) {
    try {
      await setUserCc({ userId, callCenterId: value || null }).unwrap();
      toast.success(ADMIN_MSG.callCenterDetail.assignmentSaved, value ? ADMIN_MSG.callCenterDetail.pinnedDesc : ADMIN_MSG.callCenterDetail.agencyWideDesc);
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.common.assignFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
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
          {/* The loading test is the query's own flag, not `!people` — otherwise a failed request
              would sit on the skeleton forever and never reach the error state below. */}
          {peopleLoading ? <Skeleton className="h-32" /> : isError ? (
            // A failed request must never read as "nobody works here".
            <ErrorState error={error} resource={ADMIN_MSG.callCenterDetail.resourceName} onRetry={refetch} />
          ) : staff.length === 0 ? (
            <EmptyState icon={<Icon name="users" size={20} />} title={ADMIN_MSG.callCenterDetail.noStaffTitle}
              description={isAgencyWide
                ? ADMIN_MSG.callCenterDetail.noStaffDescAgencyWide
                : ADMIN_MSG.callCenterDetail.noStaffDesc} />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <THead><TR><TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH><TH>User</TH><TH><span className="inline-flex items-center gap-1">Roles<InfoHint title="Roles" side="top">What the user is allowed to do in the system. A user can hold more than one role, and the roles together decide which pages and actions they see.</InfoHint></span></TH><TH><span className="inline-flex items-center gap-1">Call centre<InfoHint title="Call centre" side="left">Pin a user to one call centre to limit their pipeline to just that centre; set agency-wide to let them see the whole agency.</InfoHint></span></TH></TR></THead>
                <TBody>
                  {staff.map((u) => (
                    <TR key={u.id} className={sel.isSelected(u.id) ? "bg-brand-50/40" : undefined}>
                      <TD><Checkbox aria-label={`Select ${u.userName}`} {...sel.checkboxProps(u.id)} /></TD>
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
              <BulkActionBar
                count={sel.selectedCount} itemNoun="staff" onClear={sel.clear}
                actions={[
                  { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
                ]}
              />
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
