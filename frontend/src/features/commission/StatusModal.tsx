import { useState } from "react";
import { getErrorDetail } from "../../shared/api/apiError";
import { useSetCommissionStatusMutation } from "../../shared/api/baseApi";
import type { CommissionSale } from "../../shared/api/types";
import { Badge, Button, Icon, Modal, Select, Textarea } from "../../shared/ui";
import {
  COMMISSION_MSG, COMMISSION_SETTABLE_STATUSES, commissionStatusLabel, commissionStatusTone,
  goesToRetention, needsConfirmation,
} from "./messages";

/**
 * Set a sale's financial status. Financially significant outcomes (charge-back, or anything that
 * hands the policy to Retention) show an explicit warning and require a note before they can be
 * confirmed — the backend enforces the same rule.
 */
export function StatusModal({ sale, onClose, onDone, onError }: {
  sale: CommissionSale;
  onClose: () => void;
  onDone: (customerName: string, statusLabel: string) => void;
  onError: (detail?: string) => void;
}) {
  const [setStatus, { isLoading }] = useSetCommissionStatusMutation();
  const [status, setStatus_] = useState<string>("ActivePaid");
  const [note, setNote] = useState("");
  const [noteError, setNoteError] = useState(false);

  const name = sale.customerName || `#${sale.saleNumber}`;
  const toRetention = goesToRetention(status);
  const isChargeback = status === "ChargedBack";
  const noteMandatory = toRetention;

  async function submit() {
    if (noteMandatory && !note.trim()) { setNoteError(true); return; }
    try {
      await setStatus({ saleId: sale.saleId, status, note: note.trim() || null }).unwrap();
      onDone(name, commissionStatusLabel(status));
    } catch (err: unknown) {
      onError(getErrorDetail(err) ?? undefined);
    }
  }

  return (
    <Modal open onClose={onClose} title={COMMISSION_MSG.statusTitle(name)}
      description={`#${sale.saleNumber} · ${sale.carrierApproved || sale.carrier}`}>
      <div className="space-y-4">
        <div className="flex items-center gap-2 text-sm">
          <span className="text-ink-500">{COMMISSION_MSG.currentStatus}:</span>
          <Badge tone={commissionStatusTone(sale.status)} variant="soft">{commissionStatusLabel(sale.status)}</Badge>
        </div>

        <Select label={COMMISSION_MSG.newStatus} value={status}
          onChange={(e) => { setStatus_(e.target.value); setNoteError(false); }}>
          {COMMISSION_SETTABLE_STATUSES.map((s) => (
            <option key={s} value={s}>{commissionStatusLabel(s)}</option>
          ))}
        </Select>

        {/* Explicit warning for anything financially significant. */}
        {needsConfirmation(status) && (
          <div className={"rounded-xl border px-3.5 py-2.5 text-sm flex gap-2 " +
            (isChargeback ? "border-rose-200 bg-rose-50/70 text-rose-800" : "border-amber-200 bg-amber-50/70 text-amber-900")}>
            <Icon name="alert" size={15} className="shrink-0 mt-0.5" />
            <span>{isChargeback ? COMMISSION_MSG.chargebackWarning : COMMISSION_MSG.retentionWarning}</span>
          </div>
        )}

        <div>
          <Textarea label={COMMISSION_MSG.noteLabel} value={note} rows={3} maxLength={1000}
            placeholder={COMMISSION_MSG.notePlaceholder}
            onChange={(e) => { setNote(e.target.value); if (e.target.value.trim()) setNoteError(false); }} />
          <p className={"text-xs mt-1 " + (noteError ? "text-rose-600" : "text-ink-500")}>
            {noteError ? COMMISSION_MSG.noteRequired : COMMISSION_MSG.noteHelp}
          </p>
        </div>

        <div className="flex justify-end gap-2 pt-1">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button loading={isLoading} onClick={submit}
            variant={needsConfirmation(status) ? "danger" : "primary"}
            leftIcon={<Icon name="check" size={15} />}>
            {COMMISSION_MSG.confirmCta}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
