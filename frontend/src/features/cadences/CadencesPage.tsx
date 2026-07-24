import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import { useCadenceEnrollmentsQuery, useListCadencesQuery, useUpsertCadenceMutation } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Table, TBody, TD, TH, THead, TR, Textarea, useToast, type IconName,
} from "../../shared/ui";
import type { Cadence, CadenceStep } from "../../shared/api/types";

const KINDS = ["Call", "Sms", "Email", "Wait"] as const;
type Kind = typeof KINDS[number];

const kindIcon: Record<Kind, IconName> = {
  Call: "phone", Sms: "chat", Email: "doc", Wait: "clock",
};

const kindTone: Record<Kind, "brand" | "info" | "warning" | "neutral"> = {
  Call: "brand", Sms: "info", Email: "warning", Wait: "neutral",
};

const statusTone: Record<string, "success" | "warning" | "neutral" | "danger" | "info"> = {
  Active: "info", Running: "info", InProgress: "info",
  Completed: "success", Stopped: "neutral", Paused: "warning", Failed: "danger",
};

function formatDelay(min: number) {
  if (min === 0) return "immediate";
  if (min < 60) return `${min}m`;
  if (min < 1440) return `${(min / 60).toFixed(min % 60 ? 1 : 0)}h`;
  return `${(min / 1440).toFixed(min % 1440 ? 1 : 0)}d`;
}

export function CadencesPage() {
  const { data: cadences, isLoading } = useListCadencesQuery();
  const { data: enrollments, isLoading: enrLoading } = useCadenceEnrollmentsQuery();
  const [upsert, { isLoading: saving }] = useUpsertCadenceMutation();
  const toast = useToast();

  const [editing, setEditing] = useState<any | null>(null);

  function openNew() {
    setEditing({
      id: null, name: "", campaignId: null, isActive: true, description: "",
      steps: [{ order: 1, stepKind: "Call", delayMinutes: 0, parametersJson: "{}", stopIfContacted: true }],
    });
  }

  async function handleSave() {
    if (!editing) return;
    try {
      await upsert(editing).unwrap();
      toast.success(editing.id ? "Cadence updated" : "Cadence created", editing.name);
      setEditing(null);
    } catch (err: unknown) {
      toast.error("Couldn't save cadence", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Cadences"
        description="Multi-touch outreach sequences. Enroll a lead and steps fire automatically: Call → wait → SMS → wait → Email."
        actions={<Button leftIcon={<Icon name="plus" size={16} />} onClick={openNew}>New cadence</Button>}
      />

      {isLoading ? (
        <div className="space-y-3 mb-8">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-28" />)}</div>
      ) : !cadences || cadences.length === 0 ? (
        <Card className="mb-8"><CardBody>
          <EmptyState
            icon={<Icon name="filter" size={20} />}
            title="No cadences yet"
            description="Build a multi-touch sequence to consistently engage leads over time."
            action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={openNew}>New cadence</Button>}
          />
        </CardBody></Card>
      ) : (
        <div className="space-y-4 mb-8">
          {cadences.map((c) => (
            <Card key={c.id} className="hover:shadow-card-hover transition-shadow">
              <CardBody>
                <div className="flex items-start justify-between gap-4 mb-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <h3 className="font-semibold text-ink-900 text-base">{c.name}</h3>
                      {c.isActive
                        ? <Badge tone="success" variant="soft" dot>Active</Badge>
                        : <Badge tone="neutral" variant="soft">Inactive</Badge>}
                      <Badge tone="neutral" variant="soft">{c.steps.length} step{c.steps.length === 1 ? "" : "s"}</Badge>
                    </div>
                    {c.description && <p className="text-sm text-ink-600 mt-1">{c.description}</p>}
                  </div>
                  <Button variant="outline" size="sm" leftIcon={<Icon name="cog" size={14} />}
                    onClick={() => setEditing(c)}>Edit</Button>
                </div>

                {/* Step timeline */}
                <div className="flex items-center gap-2 flex-wrap pt-3 border-t hairline">
                  {c.steps
                    .slice()
                    .sort((a, b: any) => a.order - b.order)
                    .map((s, idx: number) => (
                    <div key={s.id ?? idx} className="flex items-center gap-2">
                      <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-ink-50/80 border hairline">
                        <Icon name={kindIcon[s.stepKind as Kind] ?? "doc"} size={14} className="text-ink-600" />
                        <Badge tone={kindTone[s.stepKind as Kind] ?? "neutral"} variant="soft">
                          {s.order}. {s.stepKind}
                        </Badge>
                        <span className="text-xs text-ink-500 font-mono">{formatDelay(s.delayMinutes)}</span>
                      </div>
                      {idx < c.steps.length - 1 && (
                        <Icon name="arrowRight" size={14} className="text-ink-300" />
                      )}
                    </div>
                  ))}
                </div>
              </CardBody>
            </Card>
          ))}
        </div>
      )}

      {/* Recent enrollments */}
      <Card>
        <CardHeader title="Recent enrollments" subtitle="Leads currently moving through cadences" />
        <CardBody className="pt-0 px-0">
          {enrLoading ? (
            <div className="px-5 pb-5 space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
          ) : !enrollments || enrollments.length === 0 ? (
            <div className="px-5 pb-5">
              <EmptyState
                icon={<Icon name="inbox" size={20} />}
                title="No enrollments yet"
                description="Enroll a lead into a cadence to see live progress here."
              />
            </div>
          ) : (
            <Table className="border-0 shadow-none rounded-none">
              <THead>
                <TR>
                  <TH>Enrolled</TH>
                  <TH>Lead</TH>
                  <TH>Cadence</TH>
                  <TH>Step</TH>
                  <TH>Status</TH>
                </TR>
              </THead>
              <TBody>
                {enrollments.map((e) => (
                  <TR key={e.id}>
                    <TD className="text-ink-600 text-xs">{new Date(e.enrolledAt).toLocaleString()}</TD>
                    <TD className="font-mono text-xs text-ink-700">{e.leadId.slice(0, 8)}…</TD>
                    <TD className="font-mono text-xs text-ink-700">{e.cadenceId.slice(0, 8)}…</TD>
                    <TD>
                      <Badge tone="brand" variant="soft">Step {e.currentStepOrder}</Badge>
                    </TD>
                    <TD>
                      <Badge tone={statusTone[e.status] ?? "neutral"} variant="soft" dot>{e.status}</Badge>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      <Modal
        open={editing !== null}
        onClose={() => setEditing(null)}
        title={editing?.id ? "Edit cadence" : "New cadence"}
        description="Sequence of automatic touches. Add Call / SMS / Email / Wait steps in order."
        size="xl"
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
            <Button loading={saving} onClick={handleSave}>Save cadence</Button>
          </>
        }
      >
        {editing && <CadenceForm cadence={editing} setCadence={setEditing} />}
      </Modal>
    </>
  );
}

function CadenceForm({ cadence, setCadence }: { cadence: Cadence; setCadence: (c: Cadence) => void }) {
  function setStep(i: number, patch: Partial<CadenceStep>) {
    const s = [...cadence.steps];
    s[i] = { ...s[i], ...patch };
    setCadence({ ...cadence, steps: s });
  }

  return (
    <div className="space-y-4">
      <Input label="Name" required value={cadence.name}
        onChange={(e) => setCadence({ ...cadence, name: e.target.value })}
        placeholder="e.g. New Lead 7-touch" />
      <Textarea label="Description" value={cadence.description ?? ""}
        onChange={(e) => setCadence({ ...cadence, description: e.target.value })}
        placeholder="When does this cadence get used?" />
      <label className="inline-flex items-center gap-2 text-sm text-ink-700">
        <input type="checkbox" className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
          checked={!!cadence.isActive}
          onChange={(e) => setCadence({ ...cadence, isActive: e.target.checked })} />
        Active
      </label>

      <div>
        <div className="flex items-center justify-between mb-2">
          <div className="text-xs font-semibold text-ink-700 uppercase tracking-wider">Steps</div>
          <Button type="button" variant="outline" size="sm" leftIcon={<Icon name="plus" size={14} />}
            onClick={() => setCadence({
              ...cadence,
              steps: [...cadence.steps, {
                order: cadence.steps.length + 1, stepKind: "Call",
                delayMinutes: 60, parametersJson: "{}", stopIfContacted: true,
              }],
            })}>
            Add step
          </Button>
        </div>

        <div className="space-y-2">
          {cadence.steps.map((s, i: number) => (
            <div key={i} className="p-3 rounded-lg border hairline bg-ink-50/40 space-y-2">
              <div className="flex items-center gap-2">
                <div className="h-7 w-7 rounded-md bg-brand-100 text-brand-700 grid place-items-center text-xs font-semibold">
                  {s.order}
                </div>
                <Select value={s.stepKind} onChange={(e) => setStep(i, { stepKind: e.target.value })}
                  containerClassName="w-32">
                  {KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
                </Select>
                <Input type="number" min={0} value={s.delayMinutes}
                  onChange={(e) => setStep(i, { delayMinutes: parseInt(e.target.value) || 0 })}
                  containerClassName="w-32"
                  hint="delay (min)" />
                <div className="flex-1" />
                <label className="inline-flex items-center gap-1.5 text-xs text-ink-600 whitespace-nowrap">
                  <input type="checkbox"
                    className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
                    checked={!!s.stopIfContacted}
                    onChange={(e) => setStep(i, { stopIfContacted: e.target.checked })} />
                  stop if contacted
                </label>
                <Button type="button" variant="ghost" size="icon" className="text-rose-600 hover:bg-rose-50"
                  onClick={() => setCadence({ ...cadence, steps: cadence.steps.filter((_, j: number) => j !== i) })}>
                  <Icon name="x" size={14} />
                </Button>
              </div>
              <Input
                placeholder='Parameters JSON (e.g. {"template":"welcome-sms"})'
                value={s.parametersJson ?? ""}
                onChange={(e) => setStep(i, { parametersJson: e.target.value })}
                className="font-mono text-xs"
              />
            </div>
          ))}
          {cadence.steps.length === 0 && (
            <div className="text-sm text-ink-500 text-center py-4 border border-dashed hairline rounded-lg">
              No steps yet. Add at least one for the cadence to do anything.
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
