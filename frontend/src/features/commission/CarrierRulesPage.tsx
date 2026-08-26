import { useMemo, useState } from "react";
import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import {
  useListCarrierRulesQuery, useUpsertCarrierRuleMutation, useDeleteCarrierRuleMutation,
} from "../../shared/api/baseApi";
import type { CarrierRule } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  SearchInput, Skeleton, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";
import { CARRIER_RULES_MSG } from "./messages";

/**
 * Carrier advancing rules — the single place these are edited. The Commission Desk joins them into
 * its sales list read-only, so a rule change here immediately reflects on every matching sale.
 */
export function CarrierRulesPage() {
  const toast = useToast();
  const { data: rules, isLoading } = useListCarrierRulesQuery();
  const [remove] = useDeleteCarrierRuleMutation();
  const [editing, setEditing] = useState<CarrierRule | null>(null);
  const [creating, setCreating] = useState(false);

  // Client-side search over the already-loaded rules (carrier / notes).
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return rules ?? [];
    return (rules ?? []).filter((r) =>
      [r.carrier, r.notes].some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [rules, search]);

  async function del(rule: CarrierRule) {
    if (!window.confirm(CARRIER_RULES_MSG.confirmDelete(rule.carrier))) return;
    try {
      await remove(rule.id).unwrap();
      toast.success(CARRIER_RULES_MSG.deleted);
    } catch (err: unknown) {
      toast.error(CARRIER_RULES_MSG.deleteFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <>
      <PageHeader
        eyebrow={CARRIER_RULES_MSG.eyebrow}
        title={CARRIER_RULES_MSG.title}
        description={CARRIER_RULES_MSG.description}
        actions={<Button leftIcon={<Icon name="plus" size={15} />} onClick={() => setCreating(true)}>
          {CARRIER_RULES_MSG.newRule}
        </Button>}
      />

      <Card>
        <CardHeader title={CARRIER_RULES_MSG.title}
          subtitle={rules ? `${rules.length} rule${rules.length === 1 ? "" : "s"}` : undefined}
          action={<SearchInput value={search} onChange={setSearch}
            placeholder={CARRIER_RULES_MSG.searchPlaceholder} className="w-64" />} />
        <CardBody>
          {isLoading ? (
            <Skeleton className="h-40" />
          ) : !rules || rules.length === 0 ? (
            <EmptyState icon={<Icon name="doc" size={20} />} title={CARRIER_RULES_MSG.emptyTitle}
              description={CARRIER_RULES_MSG.emptyDesc}
              action={<Button size="sm" leftIcon={<Icon name="plus" size={14} />} onClick={() => setCreating(true)}>
                {CARRIER_RULES_MSG.newRule}</Button>} />
          ) : filtered.length === 0 ? (
            <EmptyState icon={<Icon name="search" size={20} />} title={CARRIER_RULES_MSG.noMatchTitle}
              description={CARRIER_RULES_MSG.noMatchDesc} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH>{CARRIER_RULES_MSG.carrier}</TH>
                  <TH numeric>{CARRIER_RULES_MSG.rate}</TH>
                  <TH numeric>{CARRIER_RULES_MSG.months}</TH>
                  <TH>{CARRIER_RULES_MSG.notes}</TH>
                  <TH>Status</TH>
                  <TH></TH>
                </TR>
              </THead>
              <TBody>
                {filtered.map((r) => (
                  <TR key={r.id} className="hover:bg-ink-50/60 transition-colors">
                    <TD className="font-medium text-ink-900">{r.carrier}</TD>
                    <TD numeric className="tabular-nums">{r.commissionRate}%</TD>
                    <TD numeric className="tabular-nums">{r.advancedMonths}</TD>
                    <TD className="text-sm text-ink-500 max-w-[16rem] truncate" title={r.notes ?? undefined}>{r.notes || "—"}</TD>
                    <TD>{r.isActive
                      ? <Badge tone="success" variant="soft">Active</Badge>
                      : <Badge tone="neutral" variant="soft">Inactive</Badge>}</TD>
                    <TD className="text-right whitespace-nowrap">
                      <div className="inline-flex gap-1.5">
                        <Button size="sm" variant="outline" leftIcon={<Icon name="edit" size={13} />}
                          onClick={() => setEditing(r)}>Edit</Button>
                        <Button size="sm" variant="ghost" onClick={() => del(r)}>Delete</Button>
                      </div>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      {(creating || editing) && (
        <RuleModal rule={editing} onClose={() => { setCreating(false); setEditing(null); }} />
      )}
    </>
  );
}

function RuleModal({ rule, onClose }: { rule: CarrierRule | null; onClose: () => void }) {
  const toast = useToast();
  const [save, { isLoading }] = useUpsertCarrierRuleMutation();
  const [carrier, setCarrier] = useState(rule?.carrier ?? "");
  const [rate, setRate] = useState(String(rule?.commissionRate ?? 80));
  const [months, setMonths] = useState(String(rule?.advancedMonths ?? 6));
  const [notes, setNotes] = useState(rule?.notes ?? "");
  const [isActive, setIsActive] = useState(rule?.isActive ?? true);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await save({
        id: rule?.id ?? null,
        carrier: carrier.trim(),
        commissionRate: Number(rate),
        advancedMonths: Number(months),
        notes: notes.trim() || null,
        isActive,
      }).unwrap();
      toast.success(CARRIER_RULES_MSG.saved);
      onClose();
    } catch (err: unknown) {
      toast.error(CARRIER_RULES_MSG.saveFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <Modal open onClose={onClose} title={rule ? CARRIER_RULES_MSG.editRule : CARRIER_RULES_MSG.newRule}>
      <form onSubmit={submit} className="space-y-3">
        <Input label={CARRIER_RULES_MSG.carrier} required value={carrier}
          placeholder={CARRIER_RULES_MSG.carrierPlaceholder}
          onChange={(e) => setCarrier(e.target.value)} />
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input label={CARRIER_RULES_MSG.rate} type="number" min={0} max={200} step="0.01" required
            value={rate} onChange={(e) => setRate(e.target.value)} />
          <Input label={CARRIER_RULES_MSG.months} type="number" min={0} max={120} required
            value={months} onChange={(e) => setMonths(e.target.value)} />
        </div>
        <Textarea label={CARRIER_RULES_MSG.notes} rows={2} maxLength={1000} value={notes}
          onChange={(e) => setNotes(e.target.value)} />
        <label className="flex items-center gap-2 text-sm text-ink-700 cursor-pointer select-none">
          <input type="checkbox" className="rounded border-ink-300 text-brand-600 focus:ring-2 focus:ring-brand-500/40"
            checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          {CARRIER_RULES_MSG.active}
        </label>
        {!isActive && <p className="text-xs text-ink-500 -mt-1">{CARRIER_RULES_MSG.inactiveHint}</p>}
        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
          <Button type="submit" loading={isLoading} leftIcon={<Icon name="save" size={15} />}>Save</Button>
        </div>
      </form>
    </Modal>
  );
}
