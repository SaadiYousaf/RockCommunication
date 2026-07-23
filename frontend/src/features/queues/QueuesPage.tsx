import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import {
  useCreatePublicEndpointMutation,
  useListPublicEndpointsQuery,
  useListQueuesQuery, useListVoicemailsQuery,
  useUpsertQueueMutation, useUpsertVoicemailMutation,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

export function QueuesPage() {
  return (
    <>
      <PageHeader
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

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({
        id: null, name, phoneNumber: phone || null, requiredSkillCode: skill || null,
        campaignId: null, strategy: "longest-idle", maxWaitSeconds: 120,
        overflowQueueId: null, voicemailAssetId: null, isActive: true,
      }).unwrap();
      toast.success("Queue created", `${name} is ready to receive calls.`);
      setName(""); setPhone(""); setSkill(""); setOpen(false);
    } catch (err: unknown) {
      toast.error("Couldn't create queue", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <Card>
      <CardHeader
        title={<span className="flex items-center gap-2"><Icon name="phone" size={18} /> Inbound queues (ACD)</span>}
        subtitle="When a configured number rings, the routing engine picks the longest-idle agent matching the required skill."
        action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New queue</Button>}
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
              title="No queues yet"
              description="Create an inbound queue to start routing customer calls."
              action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New queue</Button>}
            />
          </div>
        ) : (
          <Table className="border-0 shadow-none rounded-none">
            <THead>
              <TR>
                <TH>Name</TH><TH>Phone</TH><TH>Skill</TH><TH>Strategy</TH><TH>Max wait</TH><TH>Status</TH>
              </TR>
            </THead>
            <TBody>
              {queues.map((q: any) => (
                <TR key={q.id}>
                  <TD className="font-medium text-ink-900">{q.name}</TD>
                  <TD className="font-mono text-ink-700 text-xs">{q.phoneNumber || <span className="text-ink-400">—</span>}</TD>
                  <TD>{q.requiredSkillCode ? <Badge tone="info" variant="soft">{q.requiredSkillCode}</Badge> : <span className="text-ink-400">—</span>}</TD>
                  <TD className="text-ink-700">{q.strategy}</TD>
                  <TD className="text-ink-700">{q.maxWaitSeconds}s</TD>
                  <TD>
                    <Badge tone={q.isActive ? "success" : "neutral"} variant="soft" dot>
                      {q.isActive ? "Active" : "Inactive"}
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

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({
        id: null, name, url, durationSeconds: parseInt(duration) || 30,
        campaignId: null, isActive: true,
      }).unwrap();
      toast.success("Voicemail saved");
      setName(""); setUrl(""); setDuration("30"); setOpen(false);
    } catch (err: unknown) {
      toast.error("Couldn't save voicemail", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <Card>
      <CardHeader
        title={<span className="flex items-center gap-2"><Icon name="chat" size={18} /> Voicemail drops</span>}
        subtitle="Pre-recorded messages used when an answering machine is detected."
        action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New asset</Button>}
      />
      <CardBody className="pt-0">
        {isLoading ? (
          <div className="space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-12" />)}</div>
        ) : !vms || vms.length === 0 ? (
          <EmptyState
            icon={<Icon name="chat" size={20} />}
            title="No voicemail assets"
            description="Upload a recording URL so agents can drop messages on no-answer."
          />
        ) : (
          <ul className="divide-y divide-ink-100">
            {vms.map((v: any) => (
              <li key={v.id} className="py-3 flex items-center gap-3">
                <div className="h-9 w-9 rounded-lg bg-brand-50 text-brand-600 grid place-items-center">
                  <Icon name="chat" size={16} />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="font-medium text-ink-900">{v.name}</div>
                  <div className="text-xs text-ink-500">{v.durationSeconds}s</div>
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
          <Input label="Audio URL" required value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://..." />
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

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const result = await create({ slug }).unwrap();
      setRevealedSecret(result.secret);
      setSlug("");
      toast.success("Endpoint created", `Slug: ${result.slug}`);
    } catch (err: unknown) {
      toast.error("Couldn't create endpoint", getErrorDetail(err) ?? "Try again.");
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
        action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => { setRevealedSecret(null); setOpen(true); }}>Generate</Button>}
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
                    onClick={() => navigator.clipboard.writeText(revealedSecret).then(() => toast.success("Copied"))}>
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
              title="No endpoints yet"
              description="Generate one to capture leads from your website forms."
              action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>Generate</Button>}
            />
          </div>
        ) : (
          <Table className="border-0 shadow-none rounded-none">
            <THead>
              <TR>
                <TH>Slug</TH><TH>Leads captured</TH><TH>Status</TH>
              </TR>
            </THead>
            <TBody>
              {endpoints.map((e: any) => (
                <TR key={e.id}>
                  <TD className="font-mono text-xs text-ink-800">/api/public/leads/{e.slug}</TD>
                  <TD className="font-semibold text-ink-900">{e.leadCount}</TD>
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
