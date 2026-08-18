import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { ADMIN_MSG } from "./messages";
import { useState } from "react";
import {
  useAddIpAllowlistMutation, useCreateVerticalMutation, useListCommissionConfigQuery,
  useListIpAllowlistQuery, useListVerticalsQuery, useRemoveIpAllowlistMutation,
  useUpdateVerticalMutation, useUpsertCommissionConfigMutation,
  useListHorizontalsQuery, useCreateHorizontalMutation, useUpdateHorizontalMutation,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, PageHeader,
  Skeleton, Tabs, useToast,
} from "../../shared/ui";

const RULES = ["closer-flat-rate", "jr-closer-split", "validator-bonus", "high-premium-kicker", "team-lead-override"];

/** Plain-language explanation of each commission rule, shown as an info-hint next to its code. */
const RULE_HINTS: Record<string, string> = {
  "closer-flat-rate":   "A fixed amount paid to the closer for every closed sale.",
  "jr-closer-split":    "The amount a junior closer earns for assisting on a close.",
  "validator-bonus":    "A bonus paid to the validator for each sale they review and approve.",
  "high-premium-kicker":"Extra commission added when a sale's premium is at or above the threshold.",
  "team-lead-override": "An override the team lead earns on top of sales closed by their team.",
};

export function AdminPage() {
  const [tab, setTab] = useState<"ip" | "verticals" | "horizontals" | "commissions">("ip");
  return (
    <>
      <PageHeader title="Admin" description="System-level configuration: IP whitelist, verticals, horizontals, commission rules." />
      <Card className="mb-4">
        <div className="px-2 pt-2 pb-1">
          <Tabs<typeof tab>
            value={tab} onChange={setTab}
            items={[
              { value: "ip", label: "IP allowlist", icon: <Icon name="shield" size={14} /> },
              { value: "verticals", label: "Verticals", icon: <Icon name="target" size={14} /> },
              { value: "horizontals", label: "Horizontals", icon: <Icon name="layers" size={14} /> },
              { value: "commissions", label: "Commission rules", icon: <Icon name="dollar" size={14} /> },
            ]}
          />
        </div>
      </Card>
      {tab === "ip" && <IpAllowlistSection />}
      {tab === "verticals" && <VerticalsSection />}
      {tab === "horizontals" && <HorizontalsSection />}
      {tab === "commissions" && <CommissionConfigSection />}
    </>
  );
}

function IpAllowlistSection() {
  const { data: list, isLoading } = useListIpAllowlistQuery();
  const [add, { isLoading: adding }] = useAddIpAllowlistMutation();
  const [remove] = useRemoveIpAllowlistMutation();
  const toast = useToast();
  const [cidr, setCidr] = useState("");
  const [note, setNote] = useState("");

  return (
    <Card>
      <CardHeader eyebrow="Security" bordered
        title={<span className="inline-flex items-center gap-1.5">IP allowlist<InfoHint title="IP / CIDR" side="right">Only these network addresses may reach the API. A single IP (e.g. 203.0.113.4) allows one machine; CIDR notation (e.g. 10.0.0.0/24) allows a whole range in one entry.</InfoHint></span>}
        subtitle="If empty, all IPs are allowed (loopback always permitted). Auth and Swagger paths bypass the check." />
      <CardBody>
        <form className="flex flex-wrap gap-2 mb-4" onSubmit={async (e) => {
          e.preventDefault();
          try {
            await add({ cidrOrIp: cidr, note: note || undefined }).unwrap();
            toast.success(ADMIN_MSG.system.ipAdded);
            setCidr(""); setNote("");
          } catch (err: unknown) { toast.error(ADMIN_MSG.system.ipAddFailed, getErrorDetail(err) ?? MESSAGES.tryAgain); }
        }}>
          <Input leftIcon={<Icon name="shield" size={14} />} placeholder="IP or CIDR (e.g. 10.0.0.0/24)"
            value={cidr} onChange={(e) => setCidr(e.target.value)} required containerClassName="flex-1 min-w-[220px]" />
          <Input placeholder="Note (e.g. office)" value={note} onChange={(e) => setNote(e.target.value)}
            containerClassName="flex-1 min-w-[220px]" />
          <Button leftIcon={<Icon name="plus" size={14} />} loading={adding}>Add</Button>
        </form>

        {isLoading ? <Skeleton className="h-24" /> : !list || list.length === 0 ? (
          <EmptyState icon={<Icon name="shield" size={20} />}
            title={ADMIN_MSG.system.ipEmptyTitle}
            description={ADMIN_MSG.system.ipEmptyDesc} />
        ) : (
          <ul className="divide-y hairline">
            {list.map((e) => (
              <li key={e.id} className="flex items-center gap-3 py-2 px-2 -mx-2 rounded-lg hover:bg-ink-50/60 transition-colors">
                <Icon name="shield" size={14} className="text-ink-500 shrink-0" />
                <code className="font-mono text-sm text-ink-800 tabular-nums whitespace-nowrap">{e.cidrOrIp}</code>
                <span className="text-xs text-ink-500 flex-1 truncate min-w-0">{e.note ?? "—"}</span>
                <Button variant="ghost" size="sm" className="text-rose-600 hover:bg-rose-50"
                  leftIcon={<Icon name="x" size={14} />}
                  onClick={() => remove(e.id)}>Remove</Button>
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

function VerticalsSection() {
  const { data: verticals, isLoading } = useListVerticalsQuery();
  const [create, { isLoading: creating }] = useCreateVerticalMutation();
  const [update] = useUpdateVerticalMutation();
  const toast = useToast();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  return (
    <Card>
      <CardHeader eyebrow="Taxonomy" bordered title="Verticals" subtitle="Tag leads, teams, and campaigns with the line of business." />
      <CardBody>
        <form className="flex flex-wrap gap-2 mb-4" onSubmit={async (e) => {
          e.preventDefault();
          try {
            await create({ name, description: description || undefined }).unwrap();
            toast.success(ADMIN_MSG.system.verticalCreated, name);
            setName(""); setDescription("");
          } catch (err: unknown) { toast.error(ADMIN_MSG.common.createFailed, getErrorDetail(err) ?? MESSAGES.tryAgain); }
        }}>
          <Input placeholder="Vertical name (e.g. Health)" value={name}
            onChange={(e) => setName(e.target.value)} required containerClassName="w-56" />
          <Input placeholder="Description" value={description}
            onChange={(e) => setDescription(e.target.value)} containerClassName="flex-1 min-w-[220px]" />
          <Button leftIcon={<Icon name="plus" size={14} />} loading={creating}>Create</Button>
        </form>
        {isLoading ? <Skeleton className="h-24" /> : !verticals || verticals.length === 0 ? (
          <EmptyState icon={<Icon name="target" size={20} />}
            title={ADMIN_MSG.system.verticalsEmptyTitle} description={ADMIN_MSG.system.verticalsEmptyDesc} />
        ) : (
          <ul className="divide-y hairline">
            {verticals.map((v) => (
              <li key={v.id} className="flex items-center gap-3 py-2 px-2 -mx-2 rounded-lg hover:bg-ink-50/60 transition-colors">
                <Icon name="target" size={14} className="text-ink-500 shrink-0" />
                <span className="font-medium text-ink-900 truncate">{v.name}</span>
                <span className="text-xs text-ink-500 flex-1 truncate min-w-0">{v.description ?? "—"}</span>
                {v.isActive
                  ? <Badge tone="success" variant="soft">Active</Badge>
                  : <Badge tone="neutral" variant="soft">Inactive</Badge>}
                <Button variant="ghost" size="sm"
                  onClick={() => update({ id: v.id, name: v.name, description: v.description ?? undefined, isActive: !v.isActive })}>
                  {v.isActive ? "Disable" : "Enable"}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

function HorizontalsSection() {
  const { data: horizontals, isLoading } = useListHorizontalsQuery();
  const [create, { isLoading: creating }] = useCreateHorizontalMutation();
  const [update] = useUpdateHorizontalMutation();
  const toast = useToast();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  return (
    <Card>
      <CardHeader eyebrow="Taxonomy" bordered title="Horizontals" subtitle="Cross-vertical dimensions (region, function, shared desk) for organising teams and campaigns." />
      <CardBody>
        <form className="flex flex-wrap gap-2 mb-4" onSubmit={async (e) => {
          e.preventDefault();
          try {
            await create({ name, description: description || undefined }).unwrap();
            toast.success(ADMIN_MSG.system.horizontalCreated, name);
            setName(""); setDescription("");
          } catch (err: unknown) { toast.error(ADMIN_MSG.common.createFailed, getErrorDetail(err) ?? MESSAGES.tryAgain); }
        }}>
          <Input placeholder="Horizontal name (e.g. East Region)" value={name}
            onChange={(e) => setName(e.target.value)} required containerClassName="w-56" />
          <Input placeholder="Description" value={description}
            onChange={(e) => setDescription(e.target.value)} containerClassName="flex-1 min-w-[220px]" />
          <Button leftIcon={<Icon name="plus" size={14} />} loading={creating}>Create</Button>
        </form>
        {isLoading ? <Skeleton className="h-24" /> : !horizontals || horizontals.length === 0 ? (
          <EmptyState icon={<Icon name="target" size={20} />}
            title={ADMIN_MSG.system.horizontalsEmptyTitle} description={ADMIN_MSG.system.horizontalsEmptyDesc} />
        ) : (
          <ul className="divide-y hairline">
            {horizontals.map((v) => (
              <li key={v.id} className="flex items-center gap-3 py-2 px-2 -mx-2 rounded-lg hover:bg-ink-50/60 transition-colors">
                <Icon name="layers" size={14} className="text-ink-500 shrink-0" />
                <span className="font-medium text-ink-900 truncate">{v.name}</span>
                <span className="text-xs text-ink-500 flex-1 truncate min-w-0">{v.description ?? "—"}</span>
                {v.isActive
                  ? <Badge tone="success" variant="soft">Active</Badge>
                  : <Badge tone="neutral" variant="soft">Inactive</Badge>}
                <Button variant="ghost" size="sm"
                  onClick={() => update({ id: v.id, name: v.name, description: v.description ?? undefined, isActive: !v.isActive })}>
                  {v.isActive ? "Disable" : "Enable"}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

function CommissionConfigSection() {
  // Gate the rows on load: RuleRow seeds its inputs from `initial` on FIRST mount only, so if a row
  // mounts before the override data arrives it shows a blank "default" and a Save would wipe the real
  // stored override to null. Rendering rows only after data loads makes the seed read the real values.
  const { data: rules, isLoading } = useListCommissionConfigQuery();
  const [upsert] = useUpsertCommissionConfigMutation();
  const ruleByName = (n: string) => rules?.find((r) => r.ruleName === n);

  return (
    <Card>
      <CardHeader eyebrow="Payouts" bordered title="Commission rules" subtitle="Per-agency overrides for each rule. Empty = use system default." />
      <CardBody className="px-0 pt-0">
        {isLoading ? (
          <div className="px-5 py-4"><Skeleton className="h-24" /></div>
        ) : (
        <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-ink-50/60 text-left">
            <tr>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold whitespace-nowrap">Rule</th>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold whitespace-nowrap">
                <span className="inline-flex items-center gap-1">Amount<InfoHint title="Amount" side="top">The payout for this rule for your agency. Leave blank to fall back to the platform default.</InfoHint></span>
              </th>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold whitespace-nowrap">
                <span className="inline-flex items-center gap-1">Threshold<InfoHint title="Threshold" side="top">Some rules only pay above a minimum value (e.g. a premium amount). Leave blank if the rule always applies.</InfoHint></span>
              </th>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold whitespace-nowrap">Enabled</th>
              <th className="px-5 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {RULES.map((name) => {
              const r = ruleByName(name);
              return <RuleRow key={name} ruleName={name} initial={r}
                onSave={(v) => upsert(v).unwrap()} />;
            })}
          </tbody>
        </table>
        </div>
        )}
      </CardBody>
    </Card>
  );
}

function RuleRow({ ruleName, initial, onSave }: {
  ruleName: string;
  initial?: { amount: number | null; threshold: number | null; enabled: boolean };
  onSave: (v: { ruleName: string; amount: number | null; threshold: number | null; enabled: boolean }) => Promise<unknown>;
}) {
  const [amount, setAmount] = useState(initial?.amount?.toString() ?? "");
  const [threshold, setThreshold] = useState(initial?.threshold?.toString() ?? "");
  const [enabled, setEnabled] = useState(initial?.enabled ?? true);
  const toast = useToast();

  return (
    <tr className="border-t hairline hover:bg-ink-50/40 transition-colors">
      <td className="px-5 py-2 whitespace-nowrap">
        <span className="inline-flex items-center gap-1.5">
          <span className="font-mono text-xs">{ruleName}</span>
          {RULE_HINTS[ruleName] && <InfoHint title={ruleName} side="right">{RULE_HINTS[ruleName]}</InfoHint>}
        </span>
      </td>
      <td className="px-5 py-2">
        <input type="number" className="border border-ink-200 rounded-lg px-2 py-1 w-24 text-sm tabular-nums transition-colors focus:outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-400/30"
          value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="default" />
      </td>
      <td className="px-5 py-2">
        <input type="number" className="border border-ink-200 rounded-lg px-2 py-1 w-24 text-sm tabular-nums transition-colors focus:outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-400/30"
          value={threshold} onChange={(e) => setThreshold(e.target.value)} placeholder="—" />
      </td>
      <td className="px-5 py-2">
        <input type="checkbox" className="h-4 w-4 rounded accent-brand-600 cursor-pointer focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
          checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
      </td>
      <td className="px-5 py-2">
        <Button size="sm"
          onClick={async () => {
            try {
              await onSave({
                ruleName,
                amount: amount === "" ? null : parseFloat(amount),
                threshold: threshold === "" ? null : parseFloat(threshold),
                enabled,
              });
              toast.success(ADMIN_MSG.system.ruleSaved, ruleName);
            } catch {
              toast.error(ADMIN_MSG.system.ruleSaveFailed, ruleName);
            }
          }}>Save</Button>
      </td>
    </tr>
  );
}
