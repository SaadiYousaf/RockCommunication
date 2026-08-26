import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import {
  useCreatePublicEndpointMutation,
  useListPublicEndpointsQuery,
  useListQueuesQuery, useListVoicemailsQuery,
  useUpsertQueueMutation, useUpsertVoicemailMutation,
} from "../../shared/api/baseApi";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { MESSAGES } from "../../shared/constants/messages";
import { QUEUES_MSG } from "./messages";

export function QueuesPage() {
  return (
    <>
      <PageHeader
        eyebrow="Operations"
        title="Telephony"
        description="Configure inbound queues, voicemail drops, and public web-form lead capture endpoints."
      />
      <div className="space-y-6">
        <QueueSection />
        <VoicemailSection />
        <PublicEndpointsSection />
      </div>
    </>
  );
}

function QueueSection() {
  const { data: queues, isLoading } = useListQueuesQuery();
  const [upsert, { isLoading: saving }] = useUpsertQueueMutation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [skill, setSkill] = useState("");

  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return queues ?? [];
    return (queues ?? []).filter((row) =>
      [row.name, row.phoneNumber, row.requiredSkillCode, row.strategy]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [queues, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered);
  const sel = useRowSelection(sorted.map((q) => q.id));

  function exportSelected() {
    const chosen = sorted.filter((q) => sel.isSelected(q.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (q) => q.name },
      { header: "Type", value: (q) => q.strategy },
      { header: "Skill", value: (q) => q.requiredSkillCode ?? "" },
      { header: "Max wait (s)", value: (q) => q.maxWaitSeconds },
      { header: "Status", value: (q) => (q.isActive ? "Active" : "Inactive") },
    ], `queues-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(QUEUES_MSG.exportReadyTitle, QUEUES_MSG.exportReadyDesc(chosen.length));
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({
        id: null, name, phoneNumber: phone || null, requiredSkillCode: skill || null,
        campaignId: null, strategy: "longest-idle", maxWaitSeconds: 120,
        overflowQueueId: null, voicemailAssetId: null, isActive: true,
      }).unwrap();
      toast.success(QUEUES_MSG.queueCreated, QUEUES_MSG.queueCreatedDesc(name));
      setName(""); setPhone(""); setSkill(""); setOpen(false);
    } catch (err: unknown) {
      toast.error(QUEUES_MSG.createQueueFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <Card>
      <CardHeader
        title={<span className="flex items-center gap-2"><Icon name="phone" size={18} /><span className="inline-flex items-center gap-1">Inbound queues (ACD)<InfoHint title="ACD — Automatic Call Distribution" side="right">Inbound calls are routed automatically to the longest-idle available agent who holds the queue's required skill.</InfoHint></span></span>}
        subtitle="When a configured number rings, the routing engine picks the longest-idle agent matching the required skill."
        action={
          <div className="flex items-center gap-2">
            <SearchInput value={search} onChange={setSearch}
              placeholder={QUEUES_MSG.queueSearchPlaceholder} className="w-64" />
            <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New queue</Button>
          </div>
        }
      />
      <CardBody className="pt-0 px-0">
        {isLoading ? (
          <div className="px-5 pb-5 space-y-2">
            {[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}
          </div>
        ) : !queues || queues.length === 0 ? (
          <div className="px-5 pb-5">
            <EmptyState
              icon={<Icon name="phone" size={20} />}
              title={QUEUES_MSG.noQueuesTitle}
              description={QUEUES_MSG.noQueuesDesc}
              action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New queue</Button>}
            />
          </div>
        ) : sorted.length === 0 ? (
          <div className="px-5 pb-5">
            <EmptyState
              icon={<Icon name="search" size={20} />}
              title={QUEUES_MSG.noQueuesMatchTitle}
              description={QUEUES_MSG.noMatchesDesc}
            />
          </div>
        ) : (
          <>
          <Table className="border-0 shadow-none rounded-none">
            <THead>
              <TR>
                <TH className="w-10"><Checkbox aria-label="Select all queues" {...sel.allCheckboxProps} /></TH>
                <TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Name</TH>
                <TH sortDir={dirFor("phoneNumber")} onClick={() => toggle("phoneNumber")}>Phone</TH>
                <TH sortDir={dirFor("requiredSkillCode")} onClick={() => toggle("requiredSkillCode")}><span className="inline-flex items-center gap-1">Skill<InfoHint title="Required skill" side="bottom">Only agents tagged with this skill code (e.g. ES for Spanish) receive this queue's calls. Blank means any available agent qualifies.</InfoHint></span></TH>
                <TH sortDir={dirFor("strategy")} onClick={() => toggle("strategy")}><span className="inline-flex items-center gap-1">Strategy<InfoHint title="Routing strategy" side="bottom">How the queue picks which available agent gets the next call (e.g. longest-idle = the agent who has waited longest since their last call).</InfoHint></span></TH>
                <TH sortDir={dirFor("maxWaitSeconds")} onClick={() => toggle("maxWaitSeconds")}><span className="inline-flex items-center gap-1">Max wait<InfoHint title="Max wait" side="bottom">The longest a caller waits in this queue before overflow handling takes over.</InfoHint></span></TH>
                <TH sortDir={dirFor("isActive")} onClick={() => toggle("isActive")}>Status</TH>
              </TR>
            </THead>
            <TBody>
              {sorted.map((q) => (
                <TR key={q.id} className={sel.isSelected(q.id) ? "bg-brand-50/40" : undefined}>
                  <TD>
                    <Checkbox aria-label={`Select ${q.name}`} {...sel.checkboxProps(q.id)} />
                  </TD>
                  <TD className="font-medium text-ink-900">{q.name}</TD>
                  <TD className="font-mono text-ink-700 text-xs">{q.phoneNumber || <span className="text-ink-400">—</span>}</TD>
                  <TD>{q.requiredSkillCode ? <Badge tone="info" variant="soft">{q.requiredSkillCode}</Badge> : <span className="text-ink-400">—</span>}</TD>
                  <TD className="text-ink-700">{q.strategy}</TD>
                  <TD className="text-ink-700 tabular-nums whitespace-nowrap">{q.maxWaitSeconds}s</TD>
                  <TD>
                    <Badge tone={q.isActive ? "success" : "neutral"} variant="soft" dot>
                      {q.isActive ? "Active" : "Inactive"}
                    </Badge>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
          <BulkActionBar
            count={sel.selectedCount} itemNoun="queue" onClear={sel.clear}
            actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
          />
          </>
        )}
      </CardBody>

      <Modal
        open={open} onClose={() => setOpen(false)}
        title="New inbound queue" size="lg"
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="queue-form" type="submit" loading={saving}>Create queue</Button>
          </>
        }
      >
        <form id="queue-form" onSubmit={submit} className="grid grid-cols-1 gap-3">
          <Input label="Queue name" required value={name} onChange={(e) => setName(e.target.value)}
            placeholder="e.g. ACA Inbound" />
          <Input label="Phone number" value={phone} onChange={(e) => setPhone(e.target.value)}
            placeholder="+1XXXXXXXXXX"
            leftIcon={<Icon name="phone" size={14} />} />
          <Input label="Required skill" value={skill} onChange={(e) => setSkill(e.target.value)}
            placeholder="ES (optional)"
            hint="Only agents with this skill code will receive calls." />
        </form>
      </Modal>
    </Card>
  );
}

function VoicemailSection() {
  const { data: vms, isLoading } = useListVoicemailsQuery();
  const [upsert, { isLoading: saving }] = useUpsertVoicemailMutation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [url, setUrl] = useState("");
  const [duration, setDuration] = useState("30");

  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return vms ?? [];
    return (vms ?? []).filter((v) => (v.name ?? "").toLowerCase().includes(q));
  }, [vms, search]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({
        id: null, name, url, durationSeconds: parseInt(duration) || 30,
        campaignId: null, isActive: true,
      }).unwrap();
      toast.success(QUEUES_MSG.voicemailSaved);
      setName(""); setUrl(""); setDuration("30"); setOpen(false);
    } catch (err: unknown) {
      toast.error(QUEUES_MSG.saveVoicemailFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <Card>
      <CardHeader
        title={<span className="flex items-center gap-2"><Icon name="chat" size={18} /> Voicemail drops</span>}
        subtitle="Pre-recorded messages used when an answering machine is detected."
        action={
          <div className="flex items-center gap-2">
            <SearchInput value={search} onChange={setSearch}
              placeholder={QUEUES_MSG.voicemailSearchPlaceholder} className="w-64" />
            <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New asset</Button>
          </div>
        }
      />
      <CardBody className="pt-0">
        {isLoading ? (
          <div className="space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-12" />)}</div>
        ) : !vms || vms.length === 0 ? (
          <EmptyState
            icon={<Icon name="mic" size={20} />}
            title={QUEUES_MSG.noVoicemailsTitle}
            description={QUEUES_MSG.noVoicemailsDesc}
            action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New asset</Button>}
          />
        ) : filtered.length === 0 ? (
          <EmptyState
            icon={<Icon name="search" size={20} />}
            title={QUEUES_MSG.noVoicemailsMatchTitle}
            description={QUEUES_MSG.noMatchesDesc}
          />
        ) : (
          <ul className="divide-y divide-ink-100">
            {filtered.map((v) => (
              <li key={v.id} className="py-3 px-2 -mx-2 rounded-lg flex items-center gap-3 hover:bg-ink-50/50 transition-colors">
                <div className="h-9 w-9 rounded-lg bg-brand-50 text-brand-600 grid place-items-center shrink-0">
                  <Icon name="chat" size={16} />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="font-medium text-ink-900 truncate">{v.name}</div>
                  <div className="text-xs text-ink-500 tabular-nums">{v.durationSeconds}s</div>
                </div>
                <audio controls src={v.url} className="h-9 max-w-xs" />
              </li>
            ))}
          </ul>
        )}
      </CardBody>

      <Modal
        open={open} onClose={() => setOpen(false)}
        title="New voicemail asset" size="lg"
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="vm-form" type="submit" loading={saving}>Save asset</Button>
          </>
        }
      >
        <form id="vm-form" onSubmit={submit} className="grid grid-cols-1 gap-3">
          <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <Input label="Audio URL" required value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://..."
            hint="A publicly reachable link to the recording (.mp3 or .wav)." />
          <Input label="Duration (seconds)" type="number" value={duration} onChange={(e) => setDuration(e.target.value)} />
        </form>
      </Modal>
    </Card>
  );
}

function PublicEndpointsSection() {
  const { data: endpoints, isLoading } = useListPublicEndpointsQuery();
  const [create, { isLoading: creating }] = useCreatePublicEndpointMutation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [slug, setSlug] = useState("");
  const [revealedSecret, setRevealedSecret] = useState<string | null>(null);

  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return endpoints ?? [];
    return (endpoints ?? []).filter((e) => (e.slug ?? "").toLowerCase().includes(q));
  }, [endpoints, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const result = await create({ slug }).unwrap();
      setRevealedSecret(result.secret);
      setSlug("");
      toast.success(QUEUES_MSG.endpointCreated, QUEUES_MSG.endpointCreatedDesc(result.slug));
    } catch (err: unknown) {
      toast.error(QUEUES_MSG.createEndpointFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <Card>
      <CardHeader
        title={<span className="flex items-center gap-2"><Icon name="target" size={18} /> Public lead-capture endpoints</span>}
        subtitle={
          <>Create a slug, get a one-time secret. Embed a form that POSTs to{" "}
            <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">/api/public/leads/&lt;slug&gt;</code>{" "}
            with header <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">X-Signature: hmac-sha256(secret, body)</code>.
          </>
        }
        action={
          <div className="flex items-center gap-2">
            <SearchInput value={search} onChange={setSearch}
              placeholder={QUEUES_MSG.endpointSearchPlaceholder} className="w-64" />
            <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => { setRevealedSecret(null); setOpen(true); }}>Generate</Button>
          </div>
        }
      />
      <CardBody className="pt-0 px-0">
        {revealedSecret && (
          <div className="mx-5 mb-4 bg-amber-50 border border-amber-300 rounded-xl p-4">
            <div className="flex items-start gap-3">
              <div className="h-8 w-8 rounded-lg bg-amber-100 text-amber-700 grid place-items-center shrink-0">
                <Icon name="shield" size={16} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-sm font-semibold text-amber-900">Save this secret now — it won't be shown again</div>
                <div className="font-mono text-xs break-all mt-1.5 bg-white/70 rounded p-2 text-amber-950">{revealedSecret}</div>
                <div className="flex gap-2 mt-2">
                  <Button size="sm" variant="outline"
                    onClick={() => navigator.clipboard.writeText(revealedSecret)
                      .then(() => toast.success(QUEUES_MSG.copiedTitle, QUEUES_MSG.copiedDesc))
                      .catch(() => toast.error(QUEUES_MSG.copyFailedTitle, QUEUES_MSG.copyFailedDesc))}>
                    Copy secret
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => setRevealedSecret(null)}>Dismiss</Button>
                </div>
              </div>
            </div>
          </div>
        )}

        {isLoading ? (
          <div className="px-5 pb-5 space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-10" />)}</div>
        ) : !endpoints || endpoints.length === 0 ? (
          <div className="px-5 pb-5">
            <EmptyState
              icon={<Icon name="target" size={20} />}
              title={QUEUES_MSG.noEndpointsTitle}
              description={QUEUES_MSG.noEndpointsDesc}
              action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>Generate</Button>}
            />
          </div>
        ) : sorted.length === 0 ? (
          <div className="px-5 pb-5">
            <EmptyState
              icon={<Icon name="search" size={20} />}
              title={QUEUES_MSG.noEndpointsMatchTitle}
              description={QUEUES_MSG.noMatchesDesc}
            />
          </div>
        ) : (
          <Table className="border-0 shadow-none rounded-none">
            <THead>
              <TR>
                <TH sortDir={dirFor("slug")} onClick={() => toggle("slug")}>Slug</TH>
                <TH sortDir={dirFor("leadCount")} onClick={() => toggle("leadCount")}><span className="inline-flex items-center gap-1">Leads captured<InfoHint title="Leads captured" side="bottom">How many leads have arrived through this endpoint's web form so far.</InfoHint></span></TH>
                <TH sortDir={dirFor("isActive")} onClick={() => toggle("isActive")}>Status</TH>
              </TR>
            </THead>
            <TBody>
              {sorted.map((e) => (
                <TR key={e.id}>
                  <TD className="font-mono text-xs text-ink-800 whitespace-nowrap">/api/public/leads/{e.slug}</TD>
                  <TD className="font-semibold text-ink-900 tabular-nums">{e.leadCount}</TD>
                  <TD>
                    <Badge tone={e.isActive ? "success" : "neutral"} variant="soft" dot>
                      {e.isActive ? "Active" : "Inactive"}
                    </Badge>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </CardBody>

      <Modal
        open={open} onClose={() => setOpen(false)}
        title="Generate public endpoint" size="md"
        description="The slug becomes part of the URL. Choose lowercase letters, numbers, and hyphens."
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="ep-form" type="submit" loading={creating} disabled={!slug.trim()}>Generate secret</Button>
          </>
        }
      >
        <form id="ep-form" onSubmit={(e) => { submit(e); setOpen(false); }} className="grid grid-cols-1 gap-3">
          <Input label="Slug" required value={slug} onChange={(e) => setSlug(e.target.value)}
            placeholder="contact-form-2024" autoFocus />
        </form>
      </Modal>
    </Card>
  );
}
