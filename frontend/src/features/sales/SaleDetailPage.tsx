import { Link, useParams } from "react-router-dom";
import type { ReactNode } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useSaleDetailQuery, useAgencyLicenseAgentsQuery, useAssignSaleLicenseAgentMutation,
  type SaleDetail,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint,
  PageHeader, Select, Skeleton, Stat, useToast,
} from "../../shared/ui";
import type { BadgeTone } from "../../shared/ui";
import { usePermission, Perm } from "../../shared/auth/permissions";
import { getErrorDetail } from "../../shared/api/apiError";

const statusTone: Record<string, BadgeTone> = {
  Funded: "success", Validated: "info", Pending: "warning",
};

const money = (n: number) =>
  `$${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
const dateTime = (s: string | null | undefined) =>
  s ? new Date(s).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }) : "—";

function bankingMeaning(code: number): { label: string; tone: BadgeTone } {
  if (code === 103) return { label: "103 · cleared", tone: "success" };
  if (code === 198) return { label: "198 · flagged (recording required)", tone: "warning" };
  return { label: `${code} · blocked`, tone: "danger" };
}

export function SaleDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const { data: sale, isLoading, isError } = useSaleDetailQuery(id, { skip: !id });

  if (isLoading) {
    return (
      <>
        <PageHeader breadcrumbs={[{ label: "Sales", to: "/sales" }]} title="Sale" />
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
          {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-20" />)}
        </div>
        <Skeleton className="h-64" />
      </>
    );
  }

  if (isError || !sale) {
    return (
      <>
        <PageHeader breadcrumbs={[{ label: "Sales", to: "/sales" }]} title="Sale" />
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="briefcase" size={20} />}
            title="Sale not found"
            description="It may have been removed, or you may not have access to it."
          />
        </CardBody></Card>
      </>
    );
  }

  const b = bankingMeaning(sale.bankingCode);

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: "Sales", to: "/sales" }]}
        eyebrow={`Sale #${sale.saleNumber}`}
        title={
          <span className="inline-flex items-center gap-2">
            {sale.leadName}
          </span>
        }
        description={`${sale.carrier}${sale.policyNumber ? ` · ${sale.policyNumber}` : ""} · sold ${dateTime(sale.soldAt)}`}
        badge={
          <span className="inline-flex items-center gap-1.5">
            <Badge tone={statusTone[sale.status] ?? "neutral"} variant="soft" dot>{sale.status}</Badge>
            {sale.isInternalSale && <Badge tone="brand" variant="soft">internal</Badge>}
          </span>
        }
      />

      {/* Headline numbers */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Monthly premium" value={money(sale.monthlyPremium)} icon={<Icon name="dollar" size={16} />} tone="brand" />
        <Stat label="Annual premium"  value={money(sale.annualPremium)}  icon={<Icon name="chart" size={16} />}  tone="accent"
              hint="What the policy costs over a full year (12 × the monthly premium)." />
        <Stat label="Total commission" value={money(sale.totalCommission)} icon={<Icon name="card" size={16} />} tone="success"
              hint={sale.licenseAgentName ? `License agent: ${sale.licenseAgentName}` : "No license agent"} />
        <Stat label="Validator status" value={sale.validatorStatus} icon={<Icon name="shield" size={16} />} tone="warning"
              hint="Where the sale sits in QA review before it's submitted to the carrier." />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Customer */}
        <Card>
          <CardHeader title="Customer" subtitle="The lead this policy was sold to" bordered />
          <CardBody className="space-y-1">
            <Field label="Name" value={
              <Link to={`/leads/${sale.leadId}`} className="text-brand-700 hover:underline font-medium inline-flex items-center gap-1">
                {sale.leadName} <Icon name="externalLink" size={12} />
              </Link>
            } />
            <Field label="Phone" value={sale.leadPhone || "—"} />
            <Field label="Email" value={sale.leadEmail ?? "—"} />
            <Field label="State" value={sale.leadState ?? "—"} />
          </CardBody>
        </Card>

        {/* People */}
        <Card>
          <CardHeader title="People" subtitle="Who worked this sale" bordered />
          <CardBody className="space-y-2.5">
            <Person role="Closer" name={sale.closerName}
              hint="The agent who closed this sale with the customer." />
            <Person role="Validator" name={sale.validatorName}
              hint="The QA agent who reviews and approves the sale before it's submitted to the carrier." />
            <LicenseAgentRow sale={sale} />
            <Field label="Call center" value={sale.callCenterName ?? "—"} />
          </CardBody>
        </Card>

        {/* Policy & approval */}
        <Card>
          <CardHeader
            title="Policy & approval"
            subtitle="Sold terms and validator-approved outcome"
            bordered
            action={
              <InfoHint title="Carrier-approved terms" side="left">
                What the carrier actually approved after submission — the plan, coverage, and premium
                can differ from what was originally sold.
              </InfoHint>
            }
          />
          <CardBody className="space-y-1">
            <Field label="Carrier" value={sale.carrier} />
            <Field label="Policy #" value={sale.policyNumber ?? "—"} mono />
            <Field label="Monthly / annual" value={`${money(sale.monthlyPremium)} · ${money(sale.annualPremium)}/yr`} />
            <div className="my-2 border-t hairline" />
            <Field label="Approved carrier" value={sale.carrierApproved ?? "—"} />
            <Field label="Approved plan" value={sale.planApproved ?? "—"} />
            <Field label="Approved coverage" value={sale.coverageApproved != null ? money(sale.coverageApproved) : "—"} />
            <Field label="Approved premium" value={sale.premiumApproved != null ? money(sale.premiumApproved) : "—"} />
            {sale.declineReason && <Field label="Decline reason" value={<span className="text-rose-700">{sale.declineReason}</span>} />}
          </CardBody>
        </Card>

        {/* Bank validation */}
        <Card>
          <CardHeader
            title="Bank validation"
            subtitle="Lyons account check captured at submission"
            bordered
            action={
              <InfoHint title="Banking code" side="left">
                103 = cleared (sale proceeds); 198 = flagged (a verification recording is attached);
                any other code blocks submission.
              </InfoHint>
            }
          />
          <CardBody className="space-y-1">
            <Field label="Banking code" value={<Badge tone={b.tone} variant="soft" dot>{b.label}</Badge>} />
            <Field label="Bank" value={sale.bankName ?? "—"} />
            <Field label="Routing #" value={sale.bankRoutingNumber ?? "—"} mono />
            <Field label="Account" mono value={
              <span className="inline-flex items-center gap-1">
                {sale.bankAccountLast4 ? `••••${sale.bankAccountLast4}` : "—"}
                <InfoHint title="Bank account" side="left">
                  For security, only the last four digits of the account number are shown here — the full
                  number is not retained after Lyons validates it.
                </InfoHint>
              </span>
            } />
            <Field label="Lyons reference" value={sale.lyonsReference ?? "—"} mono />
            <Field label="Verification recording" value={
              sale.hasRecording
                ? <span className="inline-flex items-center gap-1 text-emerald-700"><Icon name="success" size={13} /> attached</span>
                : "—"
            } />
            {sale.bankingNote && <Field label="Note" value={sale.bankingNote} />}
          </CardBody>
        </Card>
      </div>

      {/* Commissions */}
      <Card className="mt-4">
        <CardHeader
          title={
            <span className="inline-flex items-center gap-1">
              Commissions
              <InfoHint title="Commission lines" side="right">
                Each line is one commission earned on this sale, tagged with the rule that generated it.
                Paid = included in a processed payroll run; Unpaid = accrued but not yet paid out.
              </InfoHint>
            </span>
          }
          subtitle="Every commission line generated by this sale"
          bordered
          action={<span className="text-sm font-semibold text-ink-900 tabular-nums">{money(sale.totalCommission)} total</span>}
        />
        <CardBody className={sale.commissions.length === 0 ? "" : "p-0"}>
          {sale.commissions.length === 0 ? (
            <EmptyState
              icon={<Icon name="card" size={20} />}
              title="No commission lines yet"
              description="These are created when the sale is approved and funded."
            />
          ) : (
            <div className="divide-y hairline">
              {sale.commissions.map((c) => (
                <div key={c.id} className="flex items-center justify-between gap-3 px-5 py-3 transition-colors hover:bg-ink-50/60">
                  <div className="flex items-center gap-3 min-w-0">
                    <Avatar name={c.agentName ?? "?"} size={28} />
                    <div className="min-w-0">
                      <div className="font-medium text-ink-900 truncate">{c.agentName ?? "Unknown agent"}</div>
                      <div className="text-xs text-ink-500 truncate">{c.ruleName} · <span className="tabular-nums whitespace-nowrap">{dateTime(c.createdAt)}</span></div>
                    </div>
                  </div>
                  <div className="flex items-center gap-3 shrink-0">
                    <span className="font-semibold text-ink-900 tabular-nums">{money(c.amount)}</span>
                    <Badge tone={c.paid ? "success" : "warning"} variant="soft" dot>{c.paid ? "paid" : "unpaid"}</Badge>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardBody>
      </Card>

      {/* Timeline */}
      <Card className="mt-4">
        <CardHeader title="Timeline" bordered />
        <CardBody>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <TimelineItem icon="plus"    label="Created"   at={dateTime(sale.createdAt)} done />
            <TimelineItem icon="briefcase" label="Sold"    at={dateTime(sale.soldAt)}    done />
            <TimelineItem icon="shield"  label="Validated" at={dateTime(sale.validatedAt)} done={!!sale.validatedAt} />
            <TimelineItem icon="success" label="Funded"    at={dateTime(sale.fundedAt)}   done={!!sale.fundedAt} />
          </div>
        </CardBody>
      </Card>
    </>
  );
}

function Field({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1">
      <span className="text-xs uppercase tracking-wide text-ink-500 shrink-0">{label}</span>
      <span className={`text-sm text-ink-900 text-right ${mono ? "font-mono tabular-nums" : ""}`}>{value}</span>
    </div>
  );
}

function Person({ role, name, hint }: { role: string; name: string | null; hint?: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <span className="inline-flex items-center gap-1 text-xs uppercase tracking-wide text-ink-500">
        {role}
        {hint && <InfoHint title={role} side="right">{hint}</InfoHint>}
      </span>
      {name ? (
        <span className="inline-flex items-center gap-2 min-w-0">
          <Avatar name={name} size={24} />
          <span className="text-sm text-ink-900 truncate">{name}</span>
        </span>
      ) : <span className="text-sm text-ink-400">— unassigned</span>}
    </div>
  );
}

/** License agent as an editable dropdown for anyone who can validate sales; a read-only name otherwise. */
function LicenseAgentRow({ sale }: { sale: SaleDetail }) {
  const toast = useToast();
  const canAssign = usePermission(Perm.SalesValidate);
  const canAddAgents = useSelector((s: RootState) =>
    (s.auth.user?.roles ?? []).some((r) => ["SuperAdmin", "Admin", "CEO"].includes(r)));
  const { data: agents, isLoading } = useAgencyLicenseAgentsQuery(sale.agencyId, { skip: !canAssign || !sale.agencyId });
  const [assign, { isLoading: saving }] = useAssignSaleLicenseAgentMutation();

  async function pick(userId: string) {
    try {
      await assign({ id: sale.id, licenseAgentUserId: userId || null }).unwrap();
      toast.success(userId ? "License agent assigned" : "License agent cleared");
    } catch (e) {
      toast.error("Couldn't update license agent", getErrorDetail(e) ?? "Try again.");
    }
  }

  const licenseAgentHint = "The licensed agent of record filed with the carrier — typically earns the approval commission on this sale.";

  if (!canAssign) return <Person role="License agent" name={sale.licenseAgentName} hint={licenseAgentHint} />;

  return (
    <div className="flex items-center justify-between gap-3">
      <span className="inline-flex items-center gap-1 text-xs uppercase tracking-wide text-ink-500 shrink-0">
        License agent
        <InfoHint title="License agent" side="right">{licenseAgentHint}</InfoHint>
      </span>
      <div className="min-w-0 flex-1 max-w-[15rem]">
        {isLoading ? (
          <Skeleton className="h-9" />
        ) : agents && agents.length > 0 ? (
          <Select value={sale.licenseAgentUserId ?? ""} disabled={saving}
            title={saving ? "Saving your change…" : undefined}
            onChange={(e) => pick(e.target.value)} className="text-sm">
            <option value="">— unassigned —</option>
            {agents.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </Select>
        ) : (
          <div className="text-xs text-ink-500 text-right">
            No license agents in this agency.{" "}
            {canAddAgents
              ? <Link to={`/admin/agencies/${sale.agencyId}`} className="text-brand-700 hover:underline font-medium">Add one</Link>
              : "Ask an admin to add one."}
          </div>
        )}
      </div>
    </div>
  );
}

function TimelineItem({ icon, label, at, done }: { icon: string; label: string; at: string; done?: boolean }) {
  return (
    <div
      className={`rounded-lg border hairline p-3 ${done ? "bg-ink-50/50" : "opacity-60"}`}
      title={done ? undefined : `Not ${label.toLowerCase()} yet`}
    >
      <div className="flex items-center gap-1.5 text-xs font-medium text-ink-700 mb-1">
        <Icon name={icon} size={13} className={done ? "text-brand-600" : "text-ink-400"} /> {label}
      </div>
      <div className="text-sm text-ink-900 tabular-nums whitespace-nowrap">{at}</div>
    </div>
  );
}
