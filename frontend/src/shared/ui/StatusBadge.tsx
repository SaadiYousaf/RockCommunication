import { Badge } from "./Badge";
import { InfoHint } from "./InfoHint";
import { STATUS } from "../constants/messages";

/**
 * One status treatment for agencies, call centres and users.
 *
 * WHY: the same idea was spelled four different ways — "Inactive" on agencies and call centres,
 * "Disabled" on the agency detail page, "Deactivated" on users, and a " (inactive)" text suffix in
 * dropdowns — while the tab strip above them said "Disabled". An admin had to work out that four
 * words meant one thing. They do not, now.
 *
 * "disabledByParent" is the third state this app actually has and never showed: a call centre or
 * user that is off only because its agency is off. Naming it is what makes re-enable predictable —
 * an admin can see which rows will come back when they turn the agency on, and which will not.
 */
export type StatusKind = "active" | "disabled" | "disabledByParent";

export function StatusBadge({
  status, size, className,
}: {
  status: StatusKind;
  size?: "sm";
  className?: string;
}) {
  if (status === "active") {
    return <Badge tone="success" variant="soft" size={size} className={className} dot>{STATUS.active}</Badge>;
  }

  if (status === "disabledByParent") {
    return (
      <span className="inline-flex items-center gap-1">
        <Badge tone="warning" variant="soft" size={size} className={className} dot>
          {STATUS.disabledWithAgency}
        </Badge>
        <InfoHint title={STATUS.disabledWithAgency} side="top">
          {STATUS.disabledWithAgencyHint}
        </InfoHint>
      </span>
    );
  }

  return <Badge tone="neutral" variant="soft" size={size} className={className} dot>{STATUS.disabled}</Badge>;
}

/** Maps the two flags the API returns onto the three states a person needs to see. */
export function statusOf(
  isActive: boolean | null | undefined,
  disabledWithAgency?: boolean | null,
): StatusKind {
  if (isActive ?? true) return "active";
  return disabledWithAgency ? "disabledByParent" : "disabled";
}
