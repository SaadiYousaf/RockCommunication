import { useState } from "react";
import {
  useAddIpAllowlistMutation, useCreateVerticalMutation, useListCommissionConfigQuery,
  useListIpAllowlistQuery, useListVerticalsQuery, useRemoveIpAllowlistMutation,
  useUpdateVerticalMutation, useUpsertCommissionConfigMutation,
  useListHorizontalsQuery, useCreateHorizontalMutation, useUpdateHorizontalMutation,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, PageHeader,
  Skeleton, Tabs, useToast,
} from "../../shared/ui";

const RULES = ["closer-flat-rate", "jr-closer-split", "validator-bonus", "high-premium-kicker", "team-lead-override"];

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
              { value: "ip", label: "IP allowlist" },
              { value: "verticals", label: "Verticals" },
              { value: "horizontals", label: "Horizontals" },
              { value: "commissions", label: "Commission rules" },
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
      <CardHeader title="IP allowlist"
        subtitle="If empty, all IPs are allowed (loopback always permitted). Auth and Swagger paths bypass the check." />
      <CardBody>
        <form className="flex flex-wrap gap-2 mb-4" onSubmit={async (e) => {
          e.preventDefault();
          try {
            await add({ cidrOrIp: cidr, note: note || undefined }).unwrap();
            toast.success("Entry added");
            setCidr(""); setNote("");
          } catch (err: any) { toast.error("Couldn't add", err?.data?.detail ?? "Try again."); }
        }}>
          <Input leftIcon={<Icon name="shield" size={14} />} placeholder="IP or CIDR (e.g. 10.0.0.0/24)"
            value={cidr} onChange={(e) => setCidr(e.target.value)} required containerClassName="flex-1 min-w-[220px]" />
          <Input placeholder="Note (e.g. office)" value={note} onChange={(e) => setNote(e.target.value)}
            containerClassName="flex-1 min-w-[220px]" />
          <Button leftIcon={<Icon name="plus" size={14} />} loading={adding}>Add</Button>
        </form>

        {isLoading ? <Skeleton className="h-24" /> : !list || list.length === 0 ? (
          <EmptyState icon={<Icon name="shield" size={20} />}
            title="No entries — all IPs allowed"
            description="Add at least one CIDR/IP to start enforcing allowlisting." />
        ) : (
          <ul className="divide-y hairline">
            {list.map((e: any) => (
              <li key={e.id} className="flex items-center gap-3 py-2">
                <Icon name="shield" size={14} className="text-ink-500" />
                <code className="font-mono text-sm text-ink-800">{e.cidrOrIp}</code>
                <span className="text-xs text-ink-500 flex-1 truncate">{e.note ?? "—"}</span>
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
      <CardHeader title="Verticals" subtitle="Tag leads, teams, and campaigns with the line of business." />
      <CardBody>
        <form className="flex flex-wrap gap-2 mb-4" onSubmit={async (e) => {
          e.preventDefault();
          try {
            await create({ name, description: description || undefined }).unwrap();
            toast.success("Vertical created", name);
            setName(""); setDescription("");
          } catch (err: any) { toast.error("Couldn't create", err?.data?.detail ?? "Try again."); }
        }}>
          <Input placeholder="Vertical name (e.g. Health)" value={name}
            onChange={(e) => setName(e.target.value)} required containerClassName="w-56" />
          <Input placeholder="Description" value={description}
            onChange={(e) => setDescription(e.target.value)} containerClassName="flex-1 min-w-[220px]" />
          <Button leftIcon={<Icon name="plus" size={14} />} loading={creating}>Create</Button>
        </form>
        {isLoading ? <Skeleton className="h-24" /> : !verticals || verticals.length === 0 ? (
          <EmptyState icon={<Icon name="target" size={20} />}
            title="No verticals yet" description="Create the first one to tag leads and teams." />
        ) : (
          <ul className="divide-y hairline">
            {verticals.map((v: any) => (
              <li key={v.id} className="flex items-center gap-3 py-2">
                <Icon name="target" size={14} className="text-ink-500" />
                <span className="font-medium text-ink-900">{v.name}</span>
                <span className="text-xs text-ink-500 flex-1 truncate">{v.description ?? "—"}</span>
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
      <CardHeader title="Horizontals" subtitle="Cross-vertical dimensions (region, function, shared desk) for organising teams and campaigns." />
      <CardBody>
        <form className="flex flex-wrap gap-2 mb-4" onSubmit={async (e) => {
          e.preventDefault();
          try {
            await create({ name, description: description || undefined }).unwrap();
            toast.success("Horizontal created", name);
            setName(""); setDescription("");
          } catch (err: any) { toast.error("Couldn't create", err?.data?.detail ?? "Try again."); }
        }}>
          <Input placeholder="Horizontal name (e.g. East Region)" value={name}
            onChange={(e) => setName(e.target.value)} required containerClassName="w-56" />
          <Input placeholder="Description" value={description}
            onChange={(e) => setDescription(e.target.value)} containerClassName="flex-1 min-w-[220px]" />
          <Button leftIcon={<Icon name="plus" size={14} />} loading={creating}>Create</Button>
        </form>
        {isLoading ? <Skeleton className="h-24" /> : !horizontals || horizontals.length === 0 ? (
          <EmptyState icon={<Icon name="target" size={20} />}
            title="No horizontals yet" description="Create the first one to organise teams and campaigns across verticals." />
        ) : (
          <ul className="divide-y hairline">
            {horizontals.map((v: any) => (
              <li key={v.id} className="flex items-center gap-3 py-2">
                <Icon name="target" size={14} className="text-ink-500" />
                <span className="font-medium text-ink-900">{v.name}</span>
                <span className="text-xs text-ink-500 flex-1 truncate">{v.description ?? "—"}</span>
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
  const { data: rules } = useListCommissionConfigQuery();
  const [upsert] = useUpsertCommissionConfigMutation();
  const ruleByName = (n: string) => rules?.find((r: any) => r.ruleName === n);

  return (
    <Card>
      <CardHeader title="Commission rules" subtitle="Per-agency overrides for each rule. Empty = use system default." />
      <CardBody className="px-0 pt-0">
        <table className="w-full text-sm">
          <thead className="bg-ink-50/60 text-left">
            <tr>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold">Rule</th>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold">Amount</th>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold">Threshold</th>
              <th className="px-5 py-2 text-xs uppercase tracking-wide text-ink-500 font-semibold">Enabled</th>
              <th className="px-5 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {RULES.map((name) => {
              const r = ruleByName(name);
              return <RuleRow key={name} ruleName={name} initial={r}
                onSave={(v) => upsert(v).unwrap().catch(() => {})} />;
            })}
          </tbody>
        </table>
      </CardBody>
    </Card>
  );
}

function RuleRow({ ruleName, initial, onSave }: {
  ruleName: string;
  initial?: { amount: number | null; threshold: number | null; enabled: boolean };
  onSave: (v: { ruleName: string; amount: number | null; threshold: number | null; enabled: boolean }) => void;
}) {
  const [amount, setAmount] = useState(initial?.amount?.toString() ?? "");
  const [threshold, setThreshold] = useState(initial?.threshold?.toString() ?? "");
  const [enabled, setEnabled] = useState(initial?.enabled ?? true);
  const toast = useToast();

  return (
    <tr className="border-t hairline">
      <td className="px-5 py-2 font-mono text-xs">{ruleName}</td>
      <td className="px-5 py-2">
        <input type="number" className="border border-ink-200 rounded px-2 py-1 w-24 text-sm"
          value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="default" />
      </td>
      <td className="px-5 py-2">
        <input type="number" className="border border-ink-200 rounded px-2 py-1 w-24 text-sm"
          value={threshold} onChange={(e) => setThreshold(e.target.value)} placeholder="—" />
      </td>
      <td className="px-5 py-2">
        <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
      </td>
      <td className="px-5 py-2">
        <Button size="sm"
          onClick={() => {
            onSave({
              ruleName,
              amount: amount === "" ? null : parseFloat(amount),
              threshold: threshold === "" ? null : parseFloat(threshold),
              enabled,
            });
            toast.success("Rule saved", ruleName);
          }}>Save</Button>
      </td>
    </tr>
  );
}
