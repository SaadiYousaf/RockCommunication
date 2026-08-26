import { useMemo, useState } from "react";
import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { useListCommissionConfigQuery, useUpsertCommissionConfigMutation } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, PageHeader,
  SearchInput, Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { COMMISSION_RATES_MSG } from "./messages";
import { COMMISSION_RATES, type CommissionRateMeta } from "./rates";

/** The stored per-agency override for one rule (absent = the platform default is in force). */
type RateOverride = { amount: number | null; threshold: number | null; enabled: boolean };

/**
 * Commission Rates — the finance desk's home for what each role earns on a sale. Every row is one
 * rule the commission engine runs; an agency can override its amount, its threshold, or switch it
 * off. Rules are keyed internally by code, but only the friendly label is ever shown.
 */
export function CommissionRatesPage() {
  const { data: stored, isLoading } = useListCommissionConfigQuery();
  const [search, setSearch] = useState("");

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return COMMISSION_RATES;
    return COMMISSION_RATES.filter((r) =>
      [r.label, r.earnedBy, r.description].some((v) => v.toLowerCase().includes(q)));
  }, [search]);

  return (
    <>
      <PageHeader
        eyebrow={COMMISSION_RATES_MSG.eyebrow}
        title={COMMISSION_RATES_MSG.title}
        description={COMMISSION_RATES_MSG.description}
      />

      <Card>
        <CardHeader
          title={COMMISSION_RATES_MSG.title}
          subtitle={COMMISSION_RATES_MSG.changesNote}
          action={<SearchInput value={search} onChange={setSearch}
            placeholder={COMMISSION_RATES_MSG.searchPlaceholder} className="w-64" />}
        />
        <CardBody>
          {isLoading ? (
            <Skeleton className="h-64" />
          ) : rows.length === 0 ? (
            <EmptyState icon={<Icon name="search" size={20} />} title={COMMISSION_RATES_MSG.noMatchTitle}
              description={COMMISSION_RATES_MSG.noMatchDesc} />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <TR>
                    <TH>{COMMISSION_RATES_MSG.colRate}</TH>
                    <TH>{COMMISSION_RATES_MSG.colEarnedBy}</TH>
                    <TH>
                      <span className="inline-flex items-center gap-1">
                        {COMMISSION_RATES_MSG.colAmount}
                        <InfoHint title={COMMISSION_RATES_MSG.colAmount} side="top">{COMMISSION_RATES_MSG.amountHint}</InfoHint>
                      </span>
                    </TH>
                    <TH>
                      <span className="inline-flex items-center gap-1">
                        {COMMISSION_RATES_MSG.colThreshold}
                        <InfoHint title={COMMISSION_RATES_MSG.colThreshold} side="top">{COMMISSION_RATES_MSG.thresholdHint}</InfoHint>
                      </span>
                    </TH>
                    <TH>
                      <span className="inline-flex items-center gap-1">
                        {COMMISSION_RATES_MSG.colEnabled}
                        <InfoHint title={COMMISSION_RATES_MSG.colEnabled} side="top">{COMMISSION_RATES_MSG.enabledHint}</InfoHint>
                      </span>
                    </TH>
                    <TH></TH>
                  </TR>
                </THead>
                <TBody>
                  {rows.map((meta) => (
                    // Keyed by the stored value too, so the row re-seeds its inputs once data lands
                    // (otherwise a row mounted before the fetch would show blanks and a save would
                    // wipe a real override back to the default).
                    <RateRow
                      key={meta.key + (stored ? "-loaded" : "")}
                      meta={meta}
                      stored={stored?.find((s) => s.ruleName === meta.key)}
                    />
                  ))}
                </TBody>
              </Table>
            </div>
          )}
        </CardBody>
      </Card>
    </>
  );
}

function RateRow({ meta, stored }: { meta: CommissionRateMeta; stored?: RateOverride }) {
  const [upsert, { isLoading }] = useUpsertCommissionConfigMutation();
  const toast = useToast();

  const [amount, setAmount] = useState(stored?.amount?.toString() ?? "");
  const [threshold, setThreshold] = useState(stored?.threshold?.toString() ?? "");
  const [enabled, setEnabled] = useState(stored?.enabled ?? true);

  const dirty =
    amount !== (stored?.amount?.toString() ?? "") ||
    threshold !== (stored?.threshold?.toString() ?? "") ||
    enabled !== (stored?.enabled ?? true);

  async function save() {
    try {
      await upsert({
        ruleName: meta.key,
        // Blank means "fall back to the platform default", so send null rather than 0.
        amount: amount.trim() === "" ? null : Number(amount),
        threshold: threshold.trim() === "" ? null : Number(threshold),
        enabled,
      }).unwrap();
      toast.success(COMMISSION_RATES_MSG.saved, COMMISSION_RATES_MSG.savedDesc(meta.label));
    } catch (err: unknown) {
      toast.error(COMMISSION_RATES_MSG.saveFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <TR className={enabled ? undefined : "opacity-60"}>
      <TD>
        <div className="font-medium text-ink-900">{meta.label}</div>
        <div className="text-xs text-ink-500 max-w-[22rem]">{meta.description}</div>
      </TD>
      <TD><Badge tone="brand" variant="soft">{meta.earnedBy}</Badge></TD>
      <TD>
        <Input type="number" min={0} step="0.01" className="w-28 tabular-nums" value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder={String(meta.defaultAmount)} aria-label={`${meta.label} amount`} />
        {amount.trim() === "" && (
          <div className="text-[11px] text-ink-400 mt-1">{COMMISSION_RATES_MSG.usingDefault(meta.defaultAmount)}</div>
        )}
      </TD>
      <TD>
        {meta.supportsThreshold ? (
          <Input type="number" min={0} step="0.01" className="w-28 tabular-nums" value={threshold}
            onChange={(e) => setThreshold(e.target.value)}
            placeholder={String(meta.defaultThreshold ?? "")} aria-label={`${meta.label} threshold`} />
        ) : (
          <span className="text-sm text-ink-400">{COMMISSION_RATES_MSG.noThreshold}</span>
        )}
      </TD>
      <TD>
        <label className="inline-flex items-center gap-2 cursor-pointer select-none">
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)}
            className="rounded border-ink-300 text-brand-600 focus:ring-2 focus:ring-brand-500/40"
            aria-label={`${meta.label} active`} />
        </label>
      </TD>
      <TD className="text-right">
        <Button size="sm" variant={dirty ? "primary" : "outline"} loading={isLoading} disabled={!dirty}
          leftIcon={<Icon name="save" size={13} />} onClick={save}>
          {COMMISSION_RATES_MSG.save}
        </Button>
      </TD>
    </TR>
  );
}
