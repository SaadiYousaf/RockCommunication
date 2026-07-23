import { useEffect, useState } from "react";
import { useSetValidatorStatusMutation, useValidatorQueueQuery } from "../../shared/api/baseApi";
import type { ValidatorQueueItem, ValidatorStatusValue } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";

const STATUSES: { value: ValidatorStatusValue; label: string }[] = [
  { value: "Completed", label: "Completed" },
  { value: "Approved", label: "Approved" },
  { value: "ActivePaid", label: "Active Paid" },
  { value: "NoUpdateInCommission", label: "No update in commission" },
  { value: "BadBank", label: "Bad Bank" },
  { value: "Nsf", label: "NSF" },
  { value: "Decline", label: "Decline" },
  { value: "ClientCancelled", label: "Client Cancelled" },
];

const LABEL: Record<ValidatorStatusValue, string> = Object.fromEntries(
  STATUSES.map((s) => [s.value, s.label]),
) as Record<ValidatorStatusValue, string>;

const TONE: Record<ValidatorStatusValue, "neutral" | "info" | "success" | "warning" | "danger"> = {
  Completed: "neutral",
  Approved: "info",
  ActivePaid: "success",
  NoUpdateInCommission: "warning",
  BadBank: "danger",
  Nsf: "danger",
  Decline: "danger",
  ClientCancelled: "neutral",
};

const money = (n: number | null | undefined) =>
  n == null ? "—" : n.toLocaleString(undefined, { style: "currency", currency: "USD" });

/** Validator queue — every submitted sale, worked through the eight validator statuses. */
export function ValidateQueuePage() {
  const { data: queue, isLoading } = useValidatorQueueQuery();
  const [active, setActive] = useState<ValidatorQueueItem | null>(null);

  return (
    <>
      <PageHeader
        title="Validator Queue"
        description="Sales submitted by closers. Set each one's validation status and record approval or decline details."
      />
      <Card>
        <CardHeader title="Submitted sales" subtitle={queue ? `${queue.length} sale(s)` : undefined} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !queue || queue.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="No sales to validate" description="Sales appear here as soon as a closer completes one." />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH>Customer</TH><TH>Carrier</TH><TH>Premium</TH><TH>Closer</TH>
                  <TH>Status</TH><TH>Sold</TH><TH></TH>
                </TR>
              </THead>
              <TBody>
                {queue.map((s) => (
                  <TR key={s.saleId}>
                    <TD>
                      <div className="font-medium text-ink-900">{s.leadName}</div>
                      <div className="font-mono text-xs text-ink-500">{s.leadPhone}</div>
                    </TD>
                    <TD className="text-sm">{s.carrier}</TD>
                    <TD className="text-sm">{money(s.monthlyPremium)}</TD>
                    <TD className="text-sm text-ink-600">{s.closerName ?? "—"}</TD>
                    <TD>
                      <Badge tone={TONE[s.status]} variant="soft">{LABEL[s.status]}</Badge>
                      {s.status === "Decline" && s.declineReason && (
                        <div className="text-xs text-ink-500 mt-0.5 max-w-[16rem] truncate" title={s.declineReason}>{s.declineReason}</div>
                      )}
                    </TD>
                    <TD className="text-sm text-ink-500">{new Date(s.soldAt).toLocaleDateString()}</TD>
                    <TD className="text-right">
                      <Button size="sm" variant="outline" leftIcon={<Icon name="check" size={14} />} onClick={() => setActive(s)}>
                        Update
                      </Button>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      {active && <UpdateModal sale={active} onClose={() => setActive(null)} />}
    </>
  );
}

function UpdateModal({ sale, onClose }: { sale: ValidatorQueueItem; onClose: () => void }) {
  const [save, { isLoading }] = useSetValidatorStatusMutation();
  const toast = useToast();
  const [status, setStatus] = useState<ValidatorStatusValue>(sale.status);
  const [carrierApproved, setCarrierApproved] = useState(sale.carrierApproved ?? sale.carrier ?? "");
  const [coverageApproved, setCoverageApproved] = useState(sale.coverageApproved?.toString() ?? "");
  const [premiumApproved, setPremiumApproved] = useState(sale.premiumApproved?.toString() ?? sale.monthlyPremium?.toString() ?? "");
  const [planApproved, setPlanApproved] = useState(sale.planApproved ?? sale.policyNumber ?? "");
  const [declineReason, setDeclineReason] = useState(sale.declineReason ?? "");

  // Reset editable fields whenever a different sale is opened.
  useEffect(() => {
    setStatus(sale.status);
    setCarrierApproved(sale.carrierApproved ?? sale.carrier ?? "");
    setCoverageApproved(sale.coverageApproved?.toString() ?? "");
    setPremiumApproved(sale.premiumApproved?.toString() ?? sale.monthlyPremium?.toString() ?? "");
    setPlanApproved(sale.planApproved ?? sale.policyNumber ?? "");
    setDeclineReason(sale.declineReason ?? "");
  }, [sale]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await save({
        saleId: sale.saleId,
        status,
        carrierApproved: status === "Approved" ? carrierApproved : undefined,
        coverageApproved: status === "Approved" ? parseFloat(coverageApproved) || 0 : undefined,
        premiumApproved: status === "Approved" ? parseFloat(premiumApproved) || 0 : undefined,
        planApproved: status === "Approved" ? planApproved : undefined,
        declineReason: status === "Decline" ? declineReason : undefined,
      }).unwrap();
      toast.success("Status updated", `${sale.leadName} → ${LABEL[status]}`);
      onClose();
    } catch (err: any) {
      toast.error("Couldn't update", err?.data?.detail ?? "Check the required fields and try again.");
    }
  }

  return (
    <Modal open onClose={onClose} title={`Validate — ${sale.leadName}`} description={`${sale.carrier} · ${money(sale.monthlyPremium)}/mo · closer ${sale.closerName ?? "—"}`} size="lg">
      <form onSubmit={submit} className="space-y-4">
        <Select label="Validator status" required value={status} onChange={(e) => setStatus(e.target.value as ValidatorStatusValue)}>
          {STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
        </Select>

        {status === "Approved" && (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 rounded-lg border border-ink-200 bg-ink-50/50 p-3">
            <Input label="Carrier Approved" required value={carrierApproved} onChange={(e) => setCarrierApproved(e.target.value)} />
            <Input label="Plan Approved" required value={planApproved} onChange={(e) => setPlanApproved(e.target.value)} />
            <Input label="Coverage Approved" type="number" min={0} step="0.01" required leftIcon={<Icon name="dollar" size={14} />} value={coverageApproved} onChange={(e) => setCoverageApproved(e.target.value)} />
            <Input label="Premium Approved" type="number" min={0} step="0.01" required leftIcon={<Icon name="dollar" size={14} />} value={premiumApproved} onChange={(e) => setPremiumApproved(e.target.value)} />
          </div>
        )}

        {status === "Decline" && (
          <Textarea label="Reason for decline" required value={declineReason} onChange={(e) => setDeclineReason(e.target.value)} placeholder="Why was the application declined?" />
        )}

        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
          <Button type="submit" loading={isLoading} leftIcon={<Icon name="check" size={16} />}>Save status</Button>
        </div>
      </form>
    </Modal>
  );
}
