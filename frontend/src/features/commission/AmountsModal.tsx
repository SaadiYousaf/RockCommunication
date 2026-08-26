import { useState } from "react";
import { getErrorDetail } from "../../shared/api/apiError";
import { useUpdateCommissionAmountMutation } from "../../shared/api/baseApi";
import type { CommissionAmount, CommissionSale } from "../../shared/api/types";
import { formatUsd } from "../../shared/lib/format";
import { Badge, Button, Icon, Input, Modal, Table, TBody, TD, TH, THead, TR } from "../../shared/ui";
import { COMMISSION_MSG } from "./messages";

/**
 * The money lines on a sale. Read-only normally — the commission engine owns them. Once the sale is
 * CHARGED BACK the unpaid lines become editable so the desk can reconcile the true clawback amounts;
 * already-paid lines stay locked because payroll has paid them out (they're history).
 */
export function AmountsModal({ sale, onClose, onSaved, onError }: {
  sale: CommissionSale;
  onClose: () => void;
  onSaved: () => void;
  onError: (detail?: string) => void;
}) {
  const editable = sale.status === "ChargedBack";
  const name = sale.customerName || `#${sale.saleNumber}`;

  return (
    <Modal open onClose={onClose} title={COMMISSION_MSG.amountsTitle(name)} description={COMMISSION_MSG.amountsIntro}>
      <div className="space-y-4">
        <div className={"rounded-xl border px-3.5 py-2.5 text-sm flex gap-2 " +
          (editable ? "border-rose-200 bg-rose-50/70 text-rose-800" : "border-ink-200 bg-ink-50 text-ink-600")}>
          <Icon name={editable ? "edit" : "lock"} size={15} className="shrink-0 mt-0.5" />
          <span>{editable ? COMMISSION_MSG.amountsEditable : COMMISSION_MSG.amountsLocked}</span>
        </div>

        {sale.amounts.length === 0 ? (
          <p className="text-sm text-ink-500">{COMMISSION_MSG.noAmounts}</p>
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{COMMISSION_MSG.colRule}</TH>
                <TH>{COMMISSION_MSG.colAgent}</TH>
                <TH numeric>{COMMISSION_MSG.colAmount}</TH>
                <TH></TH>
              </TR>
            </THead>
            <TBody>
              {sale.amounts.map((a) => (
                <AmountRow key={a.id} saleId={sale.saleId} amount={a} editable={editable}
                  onSaved={onSaved} onError={onError} />
              ))}
            </TBody>
          </Table>
        )}

        <div className="flex items-center justify-between pt-1">
          <span className="text-sm text-ink-500">
            Total: <span className={"font-semibold tabular-nums " + (sale.fundedAmount < 0 ? "text-rose-600" : "text-ink-900")}>
              {formatUsd(sale.fundedAmount)}
            </span>
          </span>
          <Button variant="ghost" onClick={onClose}>Close</Button>
        </div>
      </div>
    </Modal>
  );
}

function AmountRow({ saleId, amount, editable, onSaved, onError }: {
  saleId: string;
  amount: CommissionAmount;
  editable: boolean;
  onSaved: () => void;
  onError: (detail?: string) => void;
}) {
  const [update, { isLoading }] = useUpdateCommissionAmountMutation();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(String(amount.amount));

  const canEdit = editable && !amount.paid;

  async function save() {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) return;
    try {
      await update({ saleId, entryId: amount.id, amount: parsed }).unwrap();
      setEditing(false);
      onSaved();
    } catch (err: unknown) {
      onError(getErrorDetail(err) ?? undefined);
    }
  }

  return (
    <TR>
      <TD className="text-sm text-ink-800">{amount.ruleName}</TD>
      <TD className="text-sm text-ink-600">{amount.agentName ?? "—"}</TD>
      <TD numeric>
        {editing ? (
          <Input type="number" step="0.01" className="w-28 text-right" value={value}
            onChange={(e) => setValue(e.target.value)} autoFocus />
        ) : (
          <span className={"tabular-nums text-sm font-medium " + (amount.amount < 0 ? "text-rose-600" : "text-ink-800")}>
            {formatUsd(amount.amount)}
          </span>
        )}
      </TD>
      <TD className="text-right whitespace-nowrap">
        {amount.paid ? (
          <span title={COMMISSION_MSG.amountPaidLock}>
            <Badge tone="neutral" variant="soft">Paid</Badge>
          </span>
        ) : editing ? (
          <div className="inline-flex gap-1.5">
            <Button size="sm" variant="ghost" onClick={() => { setEditing(false); setValue(String(amount.amount)); }}>Cancel</Button>
            <Button size="sm" loading={isLoading} onClick={save}>Save</Button>
          </div>
        ) : canEdit ? (
          <Button size="sm" variant="outline" leftIcon={<Icon name="edit" size={13} />} onClick={() => setEditing(true)}>Edit</Button>
        ) : (
          <Icon name="lock" size={13} className="text-ink-300" />
        )}
      </TD>
    </TR>
  );
}
