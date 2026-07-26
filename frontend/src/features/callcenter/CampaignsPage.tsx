import type { Campaign, Skill } from "../../shared/api/types";
import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import {
  useListCampaignsQuery, useListLeadSourcesQuery, useListSkillsQuery, useListWrapUpCodesQuery,
  useUpsertCampaignMutation, useUpsertLeadSourceMutation, useUpsertSkillMutation, useUpsertWrapUpCodeMutation,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  Select, Skeleton, Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";
import { Can, Perm } from "../../shared/auth/permissions";

type TabKey = "campaigns" | "sources" | "skills" | "wrapup";

export function CampaignsPage() {
  const [tab, setTab] = useState<TabKey>("campaigns");
  const { data: campaigns } = useListCampaignsQuery();
  const { data: sources }   = useListLeadSourcesQuery();
  const { data: skills }    = useListSkillsQuery();
  const { data: wrapUps }   = useListWrapUpCodesQuery();

  return (
    <>
      <PageHeader
        title="Call-center configuration"
        description="Campaigns, lead sources, skills, and wrap-up dispositions — the building blocks of your dialer."
      />

      <Card className="mb-5 overflow-hidden">
        <div className="px-2">
          <Tabs<TabKey>
            value={tab} onChange={setTab}
            items={[
              { value: "campaigns", label: "Campaigns",      count: campaigns?.length ?? 0 },
              { value: "sources",   label: "Lead sources",   count: sources?.length ?? 0 },
              { value: "skills",    label: "Skills",         count: skills?.length ?? 0 },
              { value: "wrapup",    label: "Wrap-up codes",  count: wrapUps?.length ?? 0 },
            ]}
          />
        </div>
      </Card>

      {tab === "campaigns" && <CampaignsSection />}
      {tab === "sources"   && <LeadSourcesSection />}
      {tab === "skills"    && <SkillsSection />}
      {tab === "wrapup"    && <WrapUpCodesSection />}
    </>
  );
}

function CampaignsSection() {
  const { data: list, isLoading } = useListCampaignsQuery();
  const [upsert, { isLoading: saving }] = useUpsertCampaignMutation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({ id: null, code, name, verticalId: null, isActive: true, startsAt: null, endsAt: null }).unwrap();
      toast.success("Campaign saved", name);
      setCode(""); setName(""); setOpen(false);
    } catch (err: unknown) {
      toast.error("Couldn't save campaign", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function toggle(c: Campaign) {
    try { await upsert({ ...c, isActive: !c.isActive }).unwrap();
      toast.success(c.isActive ? "Campaign disabled" : "Campaign enabled");
    } catch (err: unknown) { toast.error("Couldn't update", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <Card>
      <CardHeader title="Campaigns" subtitle="Outbound dialer campaigns and verticals."
        action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New campaign</Button></Can>} />
      <CardBody className="pt-0 px-0">
        {isLoading ? (
          <div className="px-5 pb-5 space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-10" />)}</div>
        ) : !list || list.length === 0 ? (
          <div className="px-5 pb-5">
            <EmptyState
              icon={<Icon name="target" size={20} />}
              title="No campaigns yet"
              description="Create a campaign to group dialer activity and lead sources."
              action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New campaign</Button></Can>}
            />
          </div>
        ) : (
          <Table className="border-0 shadow-none rounded-none">
            <THead><TR><TH>Code</TH><TH>Name</TH><TH>Status</TH><TH className="text-right">Actions</TH></TR></THead>
            <TBody>
              {list.map((c) => (
                <TR key={c.id}>
                  <TD className="font-mono text-xs text-ink-700 whitespace-nowrap">{c.code}</TD>
                  <TD className="font-medium text-ink-900 max-w-[280px] truncate">{c.name}</TD>
                  <TD>
                    <Badge tone={c.isActive ? "success" : "neutral"} variant="soft" dot>
                      {c.isActive ? "Active" : "Inactive"}
                    </Badge>
                  </TD>
                  <TD>
                    <div className="flex justify-end">
                      <Can permission={Perm.CampaignsManage}>
                        <Button variant="ghost" size="sm" onClick={() => toggle(c)}>
                          {c.isActive ? "Disable" : "Enable"}
                        </Button>
                      </Can>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </CardBody>

      <Can permission={Perm.CampaignsManage}>
        <Modal open={open} onClose={() => setOpen(false)} title="New campaign" size="md"
          footer={<>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="camp-form" type="submit" loading={saving}>Create</Button>
          </>}>
          <form id="camp-form" onSubmit={submit} className="grid grid-cols-1 gap-3">
            <Input label="Code" required value={code} onChange={(e) => setCode(e.target.value)} placeholder="OEP-Q1" />
            <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="OEP Q1 Push" />
          </form>
        </Modal>
      </Can>
    </Card>
  );
}

function LeadSourcesSection() {
  const { data: list, isLoading } = useListLeadSourcesQuery();
  const { data: campaigns } = useListCampaignsQuery();
  const [upsert, { isLoading: saving }] = useUpsertLeadSourceMutation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [cost, setCost] = useState("0");
  const [campaignId, setCampaignId] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({
        id: null, code, name, campaignId: campaignId || null,
        costPerLead: parseFloat(cost) || 0, isActive: true,
      }).unwrap();
      toast.success("Lead source saved", name);
      setCode(""); setName(""); setCost("0"); setCampaignId(""); setOpen(false);
    } catch (err: unknown) { toast.error("Couldn't save", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <Card>
      <CardHeader title="Lead sources" subtitle="Where your leads come from and what they cost."
        action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New source</Button></Can>} />
      <CardBody className="pt-0 px-0">
        {isLoading ? (
          <div className="px-5 pb-5 space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-10" />)}</div>
        ) : !list || list.length === 0 ? (
          <div className="px-5 pb-5">
            <EmptyState icon={<Icon name="target" size={20} />} title="No lead sources" description="Track how leads enter your pipeline."
              action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New source</Button></Can>} />
          </div>
        ) : (
          <Table className="border-0 shadow-none rounded-none">
            <THead><TR><TH>Code</TH><TH>Name</TH><TH numeric><span className="inline-flex items-center gap-1">$ / Lead<InfoHint title="Cost per lead" side="top">What you pay this source for each lead it delivers.</InfoHint></span></TH><TH>Campaign</TH></TR></THead>
            <TBody>
              {list.map((s) => {
                const camp = campaigns?.find((c) => c.id === s.campaignId);
                return (
                  <TR key={s.id}>
                    <TD className="font-mono text-xs text-ink-700 whitespace-nowrap">{s.code}</TD>
                    <TD className="font-medium text-ink-900 max-w-[240px] truncate">{s.name}</TD>
                    <TD className="text-ink-700 text-right tabular-nums whitespace-nowrap">${s.costPerLead.toFixed(2)}</TD>
                    <TD>{camp ? <Badge tone="info" variant="soft">{camp.name}</Badge> : <span className="text-ink-400">—</span>}</TD>
                  </TR>
                );
              })}
            </TBody>
          </Table>
        )}
      </CardBody>

      <Can permission={Perm.CampaignsManage}>
        <Modal open={open} onClose={() => setOpen(false)} title="New lead source" size="md"
          footer={<>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="src-form" type="submit" loading={saving}>Create</Button>
          </>}>
          <form id="src-form" onSubmit={submit} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Input label="Code" required value={code} onChange={(e) => setCode(e.target.value)} placeholder="FB" />
            <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="Facebook Ads" />
            <Input label="$ per lead" type="number" step="0.01" value={cost} onChange={(e) => setCost(e.target.value)} />
            <Select label="Campaign" value={campaignId} onChange={(e) => setCampaignId(e.target.value)}>
              <option value="">— none —</option>
              {campaigns?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          </form>
        </Modal>
      </Can>
    </Card>
  );
}

function SkillsSection() {
  const { data: list, isLoading } = useListSkillsQuery();
  const [upsert, { isLoading: saving }] = useUpsertSkillMutation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({ id: null, code, name, isActive: true }).unwrap();
      toast.success("Skill saved", name);
      setCode(""); setName(""); setOpen(false);
    } catch (err: unknown) { toast.error("Couldn't save", getErrorDetail(err) ?? "Try again."); }
  }

  async function toggle(s: Skill) {
    try { await upsert({ ...s, isActive: !s.isActive }).unwrap(); }
    catch (err: unknown) { toast.error("Couldn't update", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <Card>
      <CardHeader title="Skills" subtitle="Capabilities used by skill-based call routing."
        action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New skill</Button></Can>} />
      <CardBody className="pt-0">
        {isLoading ? (
          <div className="space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-10" />)}</div>
        ) : !list || list.length === 0 ? (
          <EmptyState icon={<Icon name="star" size={20} />} title="No skills" description="Add skills to enable skill-based routing."
            action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New skill</Button></Can>} />
        ) : (
          <ul className="divide-y divide-ink-100">
            {list.map((s) => (
              <li key={s.id} className="py-2.5 flex items-center gap-3 transition-colors hover:bg-ink-50/50">
                <Badge tone="info" variant="soft" className="font-mono whitespace-nowrap">{s.code}</Badge>
                <span className="flex-1 min-w-0 truncate text-ink-800">{s.name}</span>
                {s.isActive
                  ? <Badge tone="success" variant="soft" dot>Active</Badge>
                  : <Badge tone="neutral" variant="soft">Inactive</Badge>}
                <Can permission={Perm.CampaignsManage}>
                  <Button variant="ghost" size="sm" onClick={() => toggle(s)}>
                    {s.isActive ? "Disable" : "Enable"}
                  </Button>
                </Can>
              </li>
            ))}
          </ul>
        )}
      </CardBody>

      <Can permission={Perm.CampaignsManage}>
        <Modal open={open} onClose={() => setOpen(false)} title="New skill" size="md"
          footer={<>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="skill-form" type="submit" loading={saving}>Create</Button>
          </>}>
          <form id="skill-form" onSubmit={submit} className="grid grid-cols-1 gap-3">
            <Input label="Code" required value={code} onChange={(e) => setCode(e.target.value)} placeholder="ES" />
            <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="Spanish" />
          </form>
        </Modal>
      </Can>
    </Card>
  );
}

function WrapUpCodesSection() {
  const { data: list, isLoading } = useListWrapUpCodesQuery();
  const [upsert, { isLoading: saving }] = useUpsertWrapUpCodeMutation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState("");
  const [label, setLabel] = useState("");
  const [isSale, setIsSale] = useState(false);
  const [isContact, setIsContact] = useState(true);
  const [isRetry, setIsRetry] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({ id: undefined, code, label, isSale, isContact, isRetry, isActive: true }).unwrap();
      toast.success("Wrap-up code saved", label);
      setCode(""); setLabel(""); setOpen(false);
    } catch (err: unknown) { toast.error("Couldn't save", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <Card>
      <CardHeader title="Wrap-up codes" subtitle="Dispositions agents pick after every call."
        action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New code</Button></Can>} />
      <CardBody className="pt-0 px-0">
        {isLoading ? (
          <div className="px-5 pb-5 space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-10" />)}</div>
        ) : !list || list.length === 0 ? (
          <div className="px-5 pb-5">
            <EmptyState icon={<Icon name="check" size={20} />} title="No wrap-up codes" description="Add codes for agents to use after calls."
              action={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New code</Button></Can>} />
          </div>
        ) : (
          <Table className="border-0 shadow-none rounded-none">
            <THead><TR><TH>Code</TH><TH>Label</TH><TH><span className="inline-flex items-center gap-1">Flags<InfoHint title="Flags" side="left">Sale counts the call as a sale, Contact as a live contact reached, and Retry queues the lead to be dialed again.</InfoHint></span></TH></TR></THead>
            <TBody>
              {list.map((w) => (
                <TR key={w.id}>
                  <TD className="font-mono text-xs text-ink-700 whitespace-nowrap">{w.code}</TD>
                  <TD className="font-medium text-ink-900 max-w-[280px] truncate">{w.label}</TD>
                  <TD>
                    <div className="flex flex-wrap gap-1">
                      {w.isSale &&    <Badge tone="success" variant="soft">Sale</Badge>}
                      {w.isContact && <Badge tone="info"    variant="soft">Contact</Badge>}
                      {w.isRetry &&   <Badge tone="warning" variant="soft">Retry</Badge>}
                      {!w.isSale && !w.isContact && !w.isRetry && <span className="text-ink-400">—</span>}
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </CardBody>

      <Can permission={Perm.CampaignsManage}>
        <Modal open={open} onClose={() => setOpen(false)} title="New wrap-up code" size="md"
          footer={<>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="wu-form" type="submit" loading={saving}>Create</Button>
          </>}>
          <form id="wu-form" onSubmit={submit} className="grid grid-cols-1 gap-3">
            <Input label="Code" required value={code} onChange={(e) => setCode(e.target.value)} placeholder="SALE" />
            <Input label="Label" required value={label} onChange={(e) => setLabel(e.target.value)} placeholder="Closed sale" />
            <div className="flex flex-wrap gap-3">
              {([
                ["Sale",    isSale,    setIsSale],
                ["Contact", isContact, setIsContact],
                ["Retry",   isRetry,   setIsRetry],
              ] as [string, boolean, (v: boolean) => void][]).map(([lbl, val, setter]) => (
                <label key={lbl} className="inline-flex items-center gap-2 text-sm text-ink-700">
                  <input type="checkbox" className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
                    checked={val} onChange={(e) => setter(e.target.checked)} />
                  {lbl}
                </label>
              ))}
            </div>
          </form>
        </Modal>
      </Can>
    </Card>
  );
}
