import { getErrorDetail } from "../../shared/api/apiError";
import { formatPhone } from "../../shared/lib/format";
import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useCaptureCloserLeadMutation, useCloserQueueQuery } from "../../shared/api/baseApi";
import type { IntakeLeadInput } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Modal, PageHeader, SearchInput,
  Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { IntakeLeadForm } from "./IntakeLeadForm";
import { timeAgoShort, waitTone } from "../../shared/lib/time";
import { useTableSort } from "../../shared/hooks/useTableSort";

/** Closer work queue — verified leads awaiting a closing application. */
export function CloseQueuePage() {
  // Poll: leads flow in as verifiers promote them, so keep the pool live without a manual reload.
  const { data: queue, isLoading } = useCloserQueueQuery(undefined, { pollingInterval: 30_000 });
  const [addLead, { isLoading: adding }] = useCaptureCloserLeadMutation();
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState("");
  const toast = useToast();
  const navigate = useNavigate();
  const filtered = (queue ?? []).filter((l) =>
    !q.trim() || `${l.firstName} ${l.lastName} ${l.phoneNumber} ${l.city ?? ""} ${l.state ?? ""}`.toLowerCase().includes(q.trim().toLowerCase()));
  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    accessors: {
      name: (l) => `${l.firstName} ${l.lastName}`,
      location: (l) => [l.city, l.state].filter(Boolean).join(", "),
      application: (l) => (l.hasApplication ? "Started" : "New"),
    },
  });
  const total = queue?.length ?? 0;
  const started = (queue ?? []).filter((l) => l.hasApplication).length;
  const stale = (queue ?? []).filter((l) => waitTone(l.createdAt) === "danger").length;

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
        eyebrow="Closer"
        title="Closer Queue"
        description="Verified leads ready to close. Open a lead to complete the application."
        actions={
          <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>
            Add lead
          </Button>
        }
      />
      {queue && total > 0 && (
        <div className="grid grid-cols-2 md:grid-cols-3 gap-4 mb-5">
          <Stat label="Ready to close" value={total} icon={<Icon name="inbox" size={16} />} tone="brand" hint="Verified leads waiting" />
          <Stat label="Started" value={started} icon={<Icon name="briefcase" size={16} />} tone="accent" hint="Application begun" />
          <Stat label="Going stale" value={stale} icon={<Icon name="clock" size={16} />} tone="danger" hint="Waiting 24h or more" />
        </div>
      )}
      <Card>
        <CardHeader title="Ready to close" subtitle={queue ? <span className="tabular-nums">{filtered.length} of {queue.length} {queue.length === 1 ? "lead" : "leads"}</span> : undefined}
          action={<SearchInput value={q} onChange={setQ} placeholder="Search this queue…" className="w-56" />} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !filtered || filtered.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="No verified leads" description={q ? "No matches in this queue." : "Verified leads will appear here. Use “Add lead” to start one yourself."}
              action={!q ? <Button size="sm" leftIcon={<Icon name="plus" size={14} />} onClick={() => setOpen(true)}>Add lead</Button> : undefined} />
          ) : (
            <Table>
              <THead>
                <TR><TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Name</TH><TH sortDir={dirFor("phoneNumber")} onClick={() => toggle("phoneNumber")}>Phone</TH><TH sortDir={dirFor("location")} onClick={() => toggle("location")}>Location</TH><TH sortDir={dirFor("ageYears")} onClick={() => toggle("ageYears")}>Age</TH>
                  <TH sortDir={dirFor("createdAt")} onClick={() => toggle("createdAt")}>
                    <span className="inline-flex items-center gap-1">Waiting
                      <InfoHint title="Waiting time" side="bottom">How long this verified lead has waited for a closer — red means it's going stale. Work the oldest first.</InfoHint>
                    </span>
                  </TH>
                  <TH sortDir={dirFor("score")} onClick={() => toggle("score")}>
                    <span className="inline-flex items-center gap-1">Priority
                      <InfoHint title="Lead priority score" side="bottom">The lead's likelihood-to-convert score — higher is hotter.</InfoHint>
                    </span>
                  </TH>
                  <TH sortDir={dirFor("application")} onClick={() => toggle("application")}>
                  <span className="inline-flex items-center gap-1">
                    Application
                    <InfoHint title="Application" side="bottom">
                      Whether a closing application has been started for this lead. “New” means none has been begun yet; “Started” means the closer has begun (but not finished) it.
                    </InfoHint>
                  </span>
                </TH><TH></TH></TR>
              </THead>
              <TBody>
                {sorted.map((l) => (
                  <TR key={l.id} className="cursor-pointer" onClick={() => navigate(`/close-queue/${l.id}`)}>
                    <TD className="font-medium text-ink-900 whitespace-nowrap">{l.firstName} {l.lastName}</TD>
                    <TD className="font-mono text-xs whitespace-nowrap tabular-nums">{formatPhone(l.phoneNumber)}</TD>
                    <TD className="text-sm text-ink-600 max-w-[14rem] truncate">{[l.city, l.state].filter(Boolean).join(", ") || "—"}</TD>
                    <TD className="text-sm tabular-nums">{l.ageYears ?? "—"}</TD>
                    <TD className="whitespace-nowrap">
                      <span title={new Date(l.createdAt).toLocaleString()}>
                        <Badge tone={waitTone(l.createdAt)} variant="soft">{timeAgoShort(l.createdAt)}</Badge>
                      </span>
                    </TD>
                    <TD className="text-sm tabular-nums font-medium text-ink-800">{Math.round(l.score)}</TD>
                    <TD>{l.hasApplication ? <Badge tone="info" variant="soft">Started</Badge> : <Badge tone="neutral" variant="soft">New</Badge>}</TD>
                    <TD className="text-right whitespace-nowrap">
                      <Link to={`/close-queue/${l.id}`} onClick={(e) => e.stopPropagation()}>
                        <Button size="sm" leftIcon={<Icon name="briefcase" size={14} />} title="Open the closing application for this lead">Open</Button>
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
