import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import { Link } from "react-router-dom";
import { useCaptureCloserLeadMutation, useCloserQueueQuery } from "../../shared/api/baseApi";
import type { IntakeLeadInput } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { IntakeLeadForm } from "./IntakeLeadForm";

/** Closer work queue — verified leads awaiting a closing application. */
export function CloseQueuePage() {
  const { data: queue, isLoading } = useCloserQueueQuery();
  const [addLead, { isLoading: adding }] = useCaptureCloserLeadMutation();
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState("");
  const toast = useToast();
  const filtered = (queue ?? []).filter((l) =>
    !q.trim() || `${l.firstName} ${l.lastName} ${l.phoneNumber} ${l.city ?? ""} ${l.state ?? ""}`.toLowerCase().includes(q.trim().toLowerCase()));

  async function onAdd(input: IntakeLeadInput) {
    try {
      const r = await addLead(input).unwrap();
      toast.success("Lead added", `${r.firstName} ${r.lastName} → your closer queue`);
      setOpen(false);
      return true;
    } catch (err: unknown) {
      toast.error("Couldn't add lead", getErrorDetail(err) ?? "Check the required fields and try again.");
      return false;
    }
  }

  return (
    <>
      <PageHeader
        title="Closer Queue"
        description="Verified leads ready to close. Open a lead to complete the application."
        actions={
          <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>
            Add lead
          </Button>
        }
      />
      <Card>
        <CardHeader title="Ready to close" subtitle={queue ? `${filtered.length} of ${queue.length} lead(s)` : undefined}
          action={<Input placeholder="Search this queue…" leftIcon={<Icon name="search" size={14} />} value={q} onChange={(e) => setQ(e.target.value)} className="w-56" />} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !filtered || filtered.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="No verified leads" description={q ? "No matches in this queue." : "Verified leads will appear here. Use “Add lead” to start one yourself."} />
          ) : (
            <Table>
              <THead>
                <TR><TH>Name</TH><TH>Phone</TH><TH>Location</TH><TH>Age</TH><TH>Application</TH><TH></TH></TR>
              </THead>
              <TBody>
                {filtered.map((l) => (
                  <TR key={l.id}>
                    <TD className="font-medium text-ink-900">{l.firstName} {l.lastName}</TD>
                    <TD className="font-mono text-xs">{l.phoneNumber}</TD>
                    <TD className="text-sm text-ink-600">{[l.city, l.state].filter(Boolean).join(", ") || "—"}</TD>
                    <TD className="text-sm">{l.ageYears ?? "—"}</TD>
                    <TD>{l.hasApplication ? <Badge tone="info" variant="soft">Started</Badge> : <Badge tone="neutral" variant="soft">New</Badge>}</TD>
                    <TD className="text-right">
                      <Link to={`/close-queue/${l.id}`}>
                        <Button size="sm" leftIcon={<Icon name="briefcase" size={14} />}>Open</Button>
                      </Link>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Get Yourself Protected — Add Lead"
        description="Capture the prospect's details. All fields are required and must be typed (no paste). The lead lands in your closer queue."
        size="lg"
      >
        <IntakeLeadForm onSubmit={onAdd} isLoading={adding} submitLabel="Add to closer queue" />
      </Modal>
    </>
  );
}
