import { useState } from "react";
import { useVerifierQueueQuery, useSetVerifierStatusMutation } from "../../shared/api/baseApi";
import type { IntakeQueueItem, VerifierStatusValue } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, PageHeader, Select,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

const STATUSES: { value: VerifierStatusValue; label: string }[] = [
  { value: "Verified", label: "Verified" },
  { value: "NotInterested", label: "Not interested" },
  { value: "Dnc", label: "DNC" },
  { value: "Busy", label: "Busy" },
  { value: "CallBack", label: "Call Back" },
  { value: "DeadAir", label: "Dead Air" },
];

/** Verifier work queue — fronted leads awaiting a verification status. */
export function VerifyQueuePage() {
  const { data: queue, isLoading } = useVerifierQueueQuery();
  return (
    <>
      <PageHeader title="Verifier Queue" description="Leads captured by fronters. Set a status — 'Verified' sends the lead to the closer queue." />
      <Card>
        <CardHeader title="Awaiting verification" subtitle={queue ? `${queue.length} lead(s)` : undefined} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !queue || queue.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="Queue is empty" description="New fronted leads will appear here." />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH>Name</TH><TH>Phone</TH><TH>Location</TH><TH>Age</TH><TH>Received</TH><TH>Verifier status</TH>
                </TR>
              </THead>
              <TBody>
                {queue.map((l) => <VerifyRow key={l.id} lead={l} />)}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>
    </>
  );
}

function VerifyRow({ lead }: { lead: IntakeQueueItem }) {
  const [setStatus, { isLoading }] = useSetVerifierStatusMutation();
  const toast = useToast();
  const [status, setStatusVal] = useState<VerifierStatusValue | "">("");

  async function apply() {
    if (!status) { toast.error("Pick a status"); return; }
    try {
      const r = await setStatus({ leadId: lead.id, status }).unwrap();
      toast.success("Status saved", r.status === "Verified" ? "Lead sent to closer queue" : `Marked ${r.status}`);
    } catch (err: any) {
      toast.error("Couldn't save", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <TR>
      <TD className="font-medium text-ink-900">{lead.firstName} {lead.lastName}</TD>
      <TD className="font-mono text-xs">{lead.phoneNumber}</TD>
      <TD className="text-sm text-ink-600">{[lead.city, lead.state].filter(Boolean).join(", ") || "—"}</TD>
      <TD className="text-sm">{lead.ageYears ?? "—"}</TD>
      <TD className="text-xs text-ink-500">{new Date(lead.createdAt).toLocaleString()}</TD>
      <TD>
        <div className="flex items-center gap-2">
          <Select className="h-9 w-40 text-sm" value={status} onChange={(e) => setStatusVal(e.target.value as VerifierStatusValue)}>
            <option value="">Select…</option>
            {STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </Select>
          <Button size="sm" loading={isLoading} onClick={apply}>Save</Button>
          {lead.verifierStatus !== "None" && <Badge tone="neutral" variant="soft">{lead.verifierStatus}</Badge>}
        </div>
      </TD>
    </TR>
  );
}
