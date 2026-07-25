import { Link, useParams } from "react-router-dom";
import type { ReactNode } from "react";
import { useSaleDetailQuery } from "../../shared/api/baseApi";
import {
  Avatar, Badge, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint,
  PageHeader, Skeleton, Stat,
} from "../../shared/ui";
import type { BadgeTone } from "../../shared/ui";

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
        <Stat label="Annual premium"  value={money(sale.annualPremium)}  icon={<Icon name="chart" size={16} />}  tone="accent" />
        <Stat label="License-agent commission" value={money(sale.totalCommission)} icon={<Icon name="card" size={16} />} tone="success"
              hint={sale.licenseAgentName ? `to ${sale.licenseAgentName}` : "not assigned"} />
        <Stat label="Validator status" value={sale.validatorStatus} icon={<Icon name="shield" size={16} />} tone="warning" />
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
            <Person role="Closer" name={sale.closerName} />
            <Person role="Validator" name={sale.validatorName} />
            <Person role="License agent" name={sale.licenseAgentName} />
            <Field label="Call center" value={sale.callCenterName ?? "—"} />
          </CardBody>
        </Card>

        {/* Policy & approval */}
        <Card>
          <CardHeader title="Policy & approval" subtitle="Sold terms and validator-approved outcome" bordered />
          <CardBody className="space-y-1">
            <Field label="Carrier" value={sale.carrier} />
            <Field label="Policy #" value={sale.policyNumber ?? "—"} mono />
            <Field label="Monthly / annual" value={`${money(sale.monthlyPremium)} · ${money(sale.annualPremium)}/yr`} />
            <div className="my-2 border-t hairline" />
            <Field label="Approved carrier" value={sale.carrierApproved ?? "—"} />
            <Field label="Approved plan" value={sale.planApproved ?? "—"} />
            <Field label="Approved coverage" value={sale.coverageApproved != null ? money(sale.coverageApproved) : "—"} />
            <Field label="Approved premium" value={sale.premiumApproved != null ? money(sale.premiumApproved) : "—"} />
            {sale.declineReason && <Field label="Decline reason" value={<span className="text-danger-700">{sale.declineReason}</span>} />}
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
            <Field label="Account" value={sale.bankAccountLast4 ? `••••${sale.bankAccountLast4}` : "—"} mono />
            <Field label="Lyons reference" value={sale.lyonsReference ?? "—"} mono />
            <Field label="Verification recording" value={
              sale.hasRecording
                ? <span className="inline-flex items-center gap-1 text-success-700"><Icon name="success" size={13} /> attached</span>
                : "—"
            } />
            {sale.bankingNote && <Field label="Note" value={sale.bankingNote} />}
          </CardBody>
        </Card>
      </div>

      {/* Commissions */}
      <Card className="mt-4">
        <CardHeader
          title="Commissions"
          subtitle="Every commission line generated by this sale"
          bordered
          action={<span className="text-sm font-semibold text-ink-900">{money(sale.totalCommission)} total</span>}
        />
        <CardBody className={sale.commissions.length === 0 ? "" : "p-0"}>
          {sale.commissions.length === 0 ? (
            <p className="text-sm text-ink-500">No commission lines yet — these are created when the sale is approved and funded.</p>
          ) : (
            <div className="divide-y hairline">
              {sale.commissions.map((c) => (
                <div key={c.id} className="flex items-center justify-between px-5 py-3">
                  <div className="flex items-center gap-3 min-w-0">
                    <Avatar name={c.agentName ?? "?"} size={28} />
                    <div className="min-w-0">
                      <div className="font-medium text-ink-900 truncate">{c.agentName ?? "Unknown agent"}</div>
                      <div className="text-xs text-ink-500">{c.ruleName} · {dateTime(c.createdAt)}</div>
                    </div>
                  </div>
                  <div className="flex items-center gap-3 shrink-0">
                    <span className="font-semibold text-ink-900">{money(c.amount)}</span>
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
      <span className={`text-sm text-ink-900 text-right ${mono ? "font-mono" : ""}`}>{value}</span>
    </div>
  );
}

function Person({ role, name }: { role: string; name: string | null }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <span className="text-xs uppercase tracking-wide text-ink-500">{role}</span>
      {name ? (
        <span className="inline-flex items-center gap-2">
          <Avatar name={name} size={24} />
          <span className="text-sm text-ink-900">{name}</span>
        </span>
      ) : <span className="text-sm text-ink-400">— unassigned</span>}
    </div>
  );
}

function TimelineItem({ icon, label, at, done }: { icon: string; label: string; at: string; done?: boolean }) {
  return (
    <div className={`rounded-lg border hairline p-3 ${done ? "bg-ink-50/50" : "opacity-60"}`}>
      <div className="flex items-center gap-1.5 text-xs font-medium text-ink-700 mb-1">
        <Icon name={icon} size={13} className={done ? "text-brand-600" : "text-ink-400"} /> {label}
      </div>
      <div className="text-sm text-ink-900">{at}</div>
    </div>
  );
}
