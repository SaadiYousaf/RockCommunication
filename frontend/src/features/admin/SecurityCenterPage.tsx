import { roleLabel } from "../../shared/constants/roles";
import {
  Badge, Card, CardBody, CardHeader, Icon, PageHeader, Table, TBody, TD, TH, THead, TR,
} from "../../shared/ui";
import type { BadgeTone } from "../../shared/ui";

/**
 * Security Center — a read-only overview of the access model: which level each role sits at,
 * what data it can reach, and the isolation boundaries between tenants. It documents the live
 * rules enforced by the backend (role → permission grants + the two-level tenant query filter);
 * to CHANGE a role's permissions use Role Management.
 */

type Tier = "Company" | "Agency" | "Call centre" | "Floor";

interface RoleRow {
  role: string;
  scope: string;         // what data this role can reach
  can: string;           // key capabilities, plain language
  twoFactor?: boolean;
}

const TIERS: { tier: Tier; tone: BadgeTone; blurb: string; roles: RoleRow[] }[] = [
  {
    tier: "Company", tone: "danger",
    blurb: "Cross-agency. The only roles that can see more than one agency.",
    roles: [
      { role: "SuperAdmin", scope: "Every agency — full platform administration", can: "Create/manage agencies, submission agents, roles & permissions, and all settings", twoFactor: true },
      { role: "Validator",  scope: "Submission queue + closing applications across all agencies (central agents only)", can: "Validate & approve submitted sales, assign each to a License Agent", twoFactor: true },
    ],
  },
  {
    tier: "Agency", tone: "warning",
    blurb: "One agency and all of its call centres. Never another agency's data.",
    roles: [
      { role: "CEO",            scope: "Own agency (all its call centres)", can: "Create/manage the agency's call centres, users, campaigns, workflows & reports", twoFactor: true },
      { role: "Admin",          scope: "Own agency (all its call centres)", can: "Administer users, call centres, integrations, audit — but NOT roles/permissions or other agencies" },
      { role: "ProgramManager", scope: "Own agency", can: "Operations, campaigns, workflows and team management" },
      { role: "LicenseAgent",   scope: "Own agency — sales assigned to them", can: "View their assigned sales and the commission they earn" },
    ],
  },
  {
    tier: "Call centre", tone: "info",
    blurb: "Scoped to a single call centre. Cannot see sibling call centres' pipeline.",
    roles: [
      { role: "CallCenterAdmin", scope: "Own call centre", can: "Manage its users and edit its profile" },
      { role: "TeamLead",        scope: "Own call centre / team", can: "Supervise the floor, coach agents, view sales & QA" },
      { role: "QAManager",       scope: "Own agency — QA", can: "Run QA reviews and scorecards, view supervision" },
    ],
  },
  {
    tier: "Floor", tone: "neutral",
    blurb: "Operational agents. See only their own call centre's pipeline; no admin or management access.",
    roles: [
      { role: "Fronter",       scope: "Own call centre pipeline", can: "Work and front new leads" },
      { role: "Verifier",      scope: "Own call centre pipeline", can: "Verify fronted leads" },
      { role: "JrCloser",      scope: "Own call centre pipeline", can: "Assist on closes; record sales" },
      { role: "Closer",        scope: "Own call centre pipeline", can: "Close verified leads into sales" },
      { role: "SelfValidator", scope: "Own call centre pipeline", can: "Record and validate their own sales" },
      { role: "Followups",     scope: "Own call centre pipeline", can: "Work follow-up & callback leads" },
      { role: "Correspondence",scope: "Own call centre pipeline", can: "Handle customer correspondence" },
      { role: "Winbacks",      scope: "Own call centre pipeline", can: "Re-engage lost & win-back leads" },
    ],
  },
];

const ISOLATION = [
  { icon: "building", title: "Agencies never share data", body: "Every agency is a fully isolated tenant. A user in one agency can never read another agency's leads, sales, customers, users or reports — enforced by a fail-closed database filter, not just the UI." },
  { icon: "shield", title: "Call centres are isolated within an agency", body: "A user pinned to a call centre sees only that call centre's pipeline (leads, sales, calls, commissions). Agency-level roles (CEO/Admin) see all call centres in their agency." },
  { icon: "users", title: "Only Super Admin crosses agencies", body: "Cross-agency access exists solely for the global Super Admin and the central Submission Agents (for sale validation). Roles & permissions are global and only the Super Admin can change them." },
];

export function SecurityCenterPage() {
  return (
    <>
      <PageHeader
        eyebrow="Administration"
        title="Security Center"
        description="How access is governed across the platform — each role's level, the data it can reach, and the boundaries between tenants."
        badge={<Badge tone="brand" variant="soft"><Icon name="shield" size={11} className="-ml-0.5" /> Access overview</Badge>}
      />

      <Card className="mb-5">
        <CardHeader eyebrow="Data isolation" title="Boundaries" subtitle="Who can see whose data — enforced server-side." bordered />
        <CardBody>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {ISOLATION.map((b) => (
              <div key={b.title} className="rounded-lg border border-ink-200 bg-ink-50/40 p-4">
                <div className="flex items-center gap-2 mb-1.5">
                  <span className="text-brand-600"><Icon name={b.icon as never} size={16} /></span>
                  <span className="font-semibold text-sm text-ink-900">{b.title}</span>
                </div>
                <p className="text-xs text-ink-600 leading-relaxed">{b.body}</p>
              </div>
            ))}
          </div>
        </CardBody>
      </Card>

      {TIERS.map((t) => (
        <Card key={t.tier} className="mb-4">
          <CardHeader
            title={`${t.tier} level`}
            subtitle={t.blurb}
            bordered
            action={<Badge tone={t.tone} variant="soft">{t.roles.length} role{t.roles.length === 1 ? "" : "s"}</Badge>}
          />
          <CardBody>
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <TR><TH>Role</TH><TH>Data it can access</TH><TH>Key capabilities</TH><TH>2FA</TH></TR>
                </THead>
                <TBody>
                  {t.roles.map((r) => (
                    <TR key={r.role}>
                      <TD><Badge tone={t.tone} variant="soft">{roleLabel(r.role)}</Badge></TD>
                      <TD className="text-sm text-ink-700">{r.scope}</TD>
                      <TD className="text-sm text-ink-600">{r.can}</TD>
                      <TD>{r.twoFactor
                        ? <Badge tone="success" variant="soft">Required</Badge>
                        : <span className="text-xs text-ink-400">Optional</span>}</TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            </div>
          </CardBody>
        </Card>
      ))}

      <p className="text-xs text-ink-500 mt-2">
        This overview documents the enforced rules. To change what a role can do, use <strong>Role Management</strong>;
        to move a user between agency-level and a specific call centre, use <strong>User Management</strong>.
      </p>
    </>
  );
}
