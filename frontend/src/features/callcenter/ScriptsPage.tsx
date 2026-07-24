import { roleLabel } from "../../shared/constants/roles";
import type { Script } from "../../shared/api/types";
import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { useListCampaignsQuery, useListScriptsQuery, useUpsertScriptMutation } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Textarea, useToast,
} from "../../shared/ui";
import { STAGE_TONE as stageTone } from "../../shared/constants/leadStage";

const STAGES = ["New", "Fronted", "Verified", "JrClosed", "Closed", "Validated", "Funded", "Followup", "Winback", "Lost"];
const ROLES  = ["Fronter", "Verifier", "JrCloser", "Closer", "Validator", "Followups", "Winbacks"];


export function ScriptsPage() {
  const { data: scripts, isLoading } = useListScriptsQuery();
  const { data: campaigns } = useListCampaignsQuery();
  const [upsert, { isLoading: saving }] = useUpsertScriptMutation();
  const toast = useToast();

  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<any | null>(null);

  function openNew() {
    setEditing({ id: null, name: "", stage: "", role: "", campaignId: "", body: "", isActive: true });
  }

  const filtered = useMemo(() => {
    if (!scripts) return [];
    const q = search.trim().toLowerCase();
    if (!q) return scripts;
    return scripts.filter((s) =>
      s.name.toLowerCase().includes(q) ||
      s.body?.toLowerCase().includes(q) ||
      s.stage?.toLowerCase().includes(q) ||
      s.role?.toLowerCase().includes(q),
    );
  }, [scripts, search]);

  async function handleSave() {
    if (!editing) return;
    try {
      await upsert({
        id: editing.id, name: editing.name,
        stage: editing.stage || null, role: editing.role || null,
        campaignId: editing.campaignId || null,
        body: editing.body, isActive: editing.isActive,
      }).unwrap();
      toast.success(editing.id ? "Script updated" : "Script created", editing.name);
      setEditing(null);
    } catch (err: unknown) {
      toast.error("Couldn't save script", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function toggle(s: Script) {
    try { await upsert({ ...s, isActive: !s.isActive }).unwrap();
      toast.success(s.isActive ? "Script disabled" : "Script enabled");
    } catch (err: unknown) { toast.error("Couldn't update", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <>
      <PageHeader
        title="Scripts"
        description="Reusable call scripts agents see in the dialer based on stage, role, and campaign."
        actions={<Button leftIcon={<Icon name="plus" size={16} />} onClick={openNew}>New script</Button>}
      />

      <Card className="mb-4">
        <CardBody>
          <Input
            leftIcon={<Icon name="search" size={16} />}
            placeholder="Search scripts by name, content, stage, or role..."
            value={search} onChange={(e) => setSearch(e.target.value)}
          />
        </CardBody>
      </Card>

      {isLoading ? (
        <div className="space-y-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-32" />)}</div>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="doc" size={20} />}
            title={search ? "No scripts match" : "No scripts yet"}
            description={search ? "Try a different search." : "Create call scripts your agents can use during calls."}
            action={!search ? <Button leftIcon={<Icon name="plus" size={16} />} onClick={openNew}>New script</Button> : undefined}
          />
        </CardBody></Card>
      ) : (
        <div className="space-y-4">
          {filtered.map((s) => {
            const camp = campaigns?.find((c) => c.id === s.campaignId);
            return (
              <Card key={s.id}>
                <CardHeader
                  title={
                    <span className="flex items-center gap-2 flex-wrap">
                      {s.name}
                      <Badge tone="neutral" variant="soft">v{s.version}</Badge>
                      {!s.isActive && <Badge tone="danger" variant="soft">Disabled</Badge>}
                    </span>
                  }
                  subtitle={
                    <div className="flex flex-wrap items-center gap-1.5 mt-1">
                      {s.stage && <Badge tone={stageTone[s.stage] ?? "neutral"} variant="soft" dot>{s.stage}</Badge>}
                      {s.role && <Badge tone="info" variant="soft">{s.role}</Badge>}
                      {camp && <Badge tone="brand" variant="soft">{camp.name}</Badge>}
                      {!s.stage && !s.role && !camp && <span className="text-xs text-ink-400">Universal</span>}
                    </div>
                  }
                  action={
                    <div className="flex gap-1.5">
                      <Button variant="ghost" size="sm" leftIcon={<Icon name="cog" size={14} />}
                        onClick={() => setEditing(s)}>Edit</Button>
                      <Button variant="ghost" size="sm" onClick={() => toggle(s)}>
                        {s.isActive ? "Disable" : "Enable"}
                      </Button>
                    </div>
                  }
                />
                <CardBody className="pt-0">
                  <pre className="whitespace-pre-wrap text-sm text-ink-700 leading-relaxed bg-ink-50/60 p-4 rounded-lg border hairline font-sans">
                    {s.body}
                  </pre>
                </CardBody>
              </Card>
            );
          })}
        </div>
      )}

      <Modal
        open={editing !== null}
        onClose={() => setEditing(null)}
        title={editing?.id ? "Edit script" : "New script"}
        description="Use stage / role / campaign filters to control where the script appears."
        size="xl"
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
            <Button loading={saving} onClick={handleSave}>Save script</Button>
          </>
        }
      >
        {editing && (
          <div className="space-y-3">
            <Input label="Script name" required
              value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })}
              placeholder="e.g. ACA Front Pitch" />
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <Select label="Stage" value={editing.stage ?? ""} onChange={(e) => setEditing({ ...editing, stage: e.target.value })}>
                <option value="">— Any stage —</option>
                {STAGES.map((s) => <option key={s} value={s}>{s}</option>)}
              </Select>
              <Select label="Role" value={editing.role ?? ""} onChange={(e) => setEditing({ ...editing, role: e.target.value })}>
                <option value="">— Any role —</option>
                {ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}
              </Select>
              <Select label="Campaign" value={editing.campaignId ?? ""} onChange={(e) => setEditing({ ...editing, campaignId: e.target.value })}>
                <option value="">— Any campaign —</option>
                {campaigns?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </Select>
            </div>
            <Textarea
              label="Script body" required
              hint="Markdown is supported in the agent UI. Use {{firstName}}, {{lastName}}, {{state}} placeholders."
              value={editing.body} onChange={(e) => setEditing({ ...editing, body: e.target.value })}
              className="font-mono text-sm min-h-[260px]"
            />
            <label className="inline-flex items-center gap-2 text-sm text-ink-700">
              <input type="checkbox" className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
                checked={!!editing.isActive}
                onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })} />
              Active
            </label>
          </div>
        )}
      </Modal>
    </>
  );
}
