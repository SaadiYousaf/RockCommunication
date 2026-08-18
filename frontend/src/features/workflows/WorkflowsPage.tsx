import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import {
  useDeleteWorkflowRuleMutation, useListWorkflowRulesQuery, useUpsertWorkflowRuleMutation,
  useWorkflowActionTypesQuery, useWorkflowEventTypesQuery, useWorkflowExecutionsQuery,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tabs, Textarea, useToast, cn,
  type IconName,
} from "../../shared/ui";
import type { WorkflowRule } from "../../shared/api/types";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { MESSAGES } from "../../shared/constants/messages";
import { WORKFLOWS_MSG } from "./messages";

type Rule = {
  id: string | null;
  name: string;
  eventType: string;
  conditionJson: string | null;
  priority: number;
  isActive: boolean;
  continueOnError: boolean;
  description: string | null;
  actions: { actionType: string; parametersJson: string | null; order: number }[];
};

type Tone = "brand" | "info" | "warning" | "success" | "danger" | "neutral" | "accent";

const eventTone: Record<string, Tone> = {
  "lead.created": "brand",
  "lead.stage-changed": "info",
  "lead.transitioned": "info",
  "call.completed": "info",
  "sale.closed": "success",
  "sale.validated": "success",
  "sale.funded": "success",
  "callback.due": "warning",
};

const actionIcon: Record<string, IconName> = {
  "assign-agent": "userCheck",
  "notify-user": "bell",
  "send-sms": "send",
  "send-email": "mail",
  "create-callback": "calendar",
  "move-stage": "arrowRight",
  "webhook": "globe",
};

export function WorkflowsPage() {
  const { data: rules, isLoading } = useListWorkflowRulesQuery();
  const { data: eventTypes } = useWorkflowEventTypesQuery();
  const { data: actionTypes } = useWorkflowActionTypesQuery();
  // Poll: execution rows are produced entirely by the background engine, never by a UI mutation.
  const { data: executions } = useWorkflowExecutionsQuery(undefined, { pollingInterval: 30_000 });
  const [upsert, { isLoading: saving }] = useUpsertWorkflowRuleMutation();
  const [del] = useDeleteWorkflowRuleMutation();
  const toast = useToast();

  const [tab, setTab] = useState<"rules" | "executions">("rules");
  const [editing, setEditing] = useState<Rule | null>(null);
  const [search, setSearch] = useState("");
  const [confirmDelete, setConfirmDelete] = useState<{ id: string; name: string } | null>(null);

  const filtered = useMemo(() => {
    if (!rules) return [];
    const q = search.trim().toLowerCase();
    if (!q) return rules;
    return rules.filter((r) =>
      r.name.toLowerCase().includes(q) ||
      r.eventType.toLowerCase().includes(q) ||
      (r.description ?? "").toLowerCase().includes(q),
    );
  }, [rules, search]);

  const stats = useMemo(() => {
    const items = rules ?? [];
    return {
      total: items.length,
      active: items.filter((r) => r.isActive).length,
      events: new Set(items.map((r) => r.eventType)).size,
      runs: executions?.length ?? 0,
    };
  }, [rules, executions]);

  const { sorted: sortedExecutions, dirFor: executionDir, toggle: sortExecution } = useTableSort(executions, {
    accessors: { when: (e) => Date.parse(e.startedAt) },
  });

  function openNew() {
    setEditing({
      id: null, name: "", eventType: eventTypes?.[0] ?? "lead.created",
      conditionJson: "", priority: 100, isActive: true, continueOnError: true,
      description: "",
      actions: [{ actionType: actionTypes?.[0] ?? "assign-agent", parametersJson: "{}", order: 1 }],
    });
  }

  async function save() {
    if (!editing) return;
    try {
      await upsert(editing).unwrap();
      toast.success(editing.id ? WORKFLOWS_MSG.ruleUpdated : WORKFLOWS_MSG.ruleCreated, editing.name);
      setEditing(null);
    } catch (err: unknown) {
      toast.error(WORKFLOWS_MSG.saveRuleFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function doDelete() {
    if (!confirmDelete) return;
    try {
      await del(confirmDelete.id).unwrap();
      toast.success(WORKFLOWS_MSG.ruleDeleted, confirmDelete.name);
      setConfirmDelete(null);
    } catch (err: unknown) {
      toast.error(WORKFLOWS_MSG.deleteRuleFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <>
      <PageHeader
        title="Workflows"
        description="Rule engine: when an event happens, run actions. JSON conditions filter which leads match."
        actions={<Button leftIcon={<Icon name="plus" size={16} />} onClick={openNew}>New rule</Button>}
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Total rules"     value={stats.total}  icon={<Icon name="workflow" size={16} />} tone="brand" />
        <Stat label="Active"          value={stats.active} icon={<Icon name="zap" size={16} />}      tone="success"
              hint={stats.total > 0 ? `${Math.round((stats.active / stats.total) * 100)}% of rules` : undefined} />
        <Stat label="Distinct events" value={stats.events} icon={<Icon name="branch" size={16} />}   tone="accent"
              hint="Different trigger events your rules cover" />
        <Stat label="Recent runs"     value={stats.runs}   icon={<Icon name="activity" size={16} />} tone="warning"
              hint="Times a rule has fired lately" />
      </div>

      <Card className="mb-4">
        <div className="px-2 pt-2 pb-1 overflow-x-auto">
          <Tabs<typeof tab>
            value={tab} onChange={setTab}
            items={[
              { value: "rules", label: "Rules", count: rules?.length },
              { value: "executions", label: "Executions", count: executions?.length },
            ]}
          />
        </div>
        {tab === "rules" && (
          <CardBody className="border-t hairline">
            <SearchInput
              value={search} onChange={setSearch}
              placeholder="Search by name, event, description…"
            />
          </CardBody>
        )}
      </Card>

      {tab === "rules" && (isLoading ? (
        <div className="space-y-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-28 rounded-2xl" />)}</div>
      ) : filtered.length === 0 ? (
        <Card><CardBody className="py-12">
          <EmptyState
            icon={<Icon name="workflow" size={22} />}
            title={search ? WORKFLOWS_MSG.noRulesMatchTitle : WORKFLOWS_MSG.noRulesTitle}
            description={search ? WORKFLOWS_MSG.noRulesMatchDesc : WORKFLOWS_MSG.noRulesDesc}
            action={!search ? <Button leftIcon={<Icon name="plus" size={16} />} onClick={openNew}>New rule</Button> : undefined}
          />
        </CardBody></Card>
      ) : (
        <div className="space-y-3">
          {filtered.map((r) => (
            <RuleCard
              key={r.id}
              rule={r}
              onEdit={() => setEditing(r as Rule)}
              onDelete={() => setConfirmDelete({ id: r.id, name: r.name })}
            />
          ))}
        </div>
      ))}

      {tab === "executions" && (
        <Card>
          <CardHeader
            title={<span className="inline-flex items-center gap-2"><Icon name="activity" size={16} className="text-brand-600" /> Recent executions</span>}
            subtitle="Every time a rule fired and its outcome." bordered
          />
          <CardBody className="pt-0 px-0">
            {!executions || executions.length === 0 ? (
              <div className="px-5 py-10">
                <EmptyState
                  icon={<Icon name="activity" size={20} />}
                  title={WORKFLOWS_MSG.noExecutionsTitle}
                  description={WORKFLOWS_MSG.noExecutionsDesc}
                />
              </div>
            ) : (
              <Table className="border-0 shadow-none rounded-none">
                <THead>
                  <TR>
                    <TH sortDir={executionDir("when")} onClick={() => sortExecution("when")}>When</TH>
                    <TH sortDir={executionDir("eventType")} onClick={() => sortExecution("eventType")}><span className="inline-flex items-center gap-1">Event<InfoHint title="Triggering event" side="bottom">The pipeline event that fired this rule (e.g. sale.validated = approved by the carrier, sale.funded = first payment cleared).</InfoHint></span></TH>
                    <TH sortDir={executionDir("status")} onClick={() => sortExecution("status")}>Status</TH>
                    <TH sortDir={executionDir("error")} onClick={() => sortExecution("error")}>Error</TH>
                  </TR>
                </THead>
                <TBody>
                  {sortedExecutions.map((e) => (
                    <TR key={e.id}>
                      <TD className="text-ink-600 text-xs whitespace-nowrap tabular-nums">{new Date(e.startedAt).toLocaleString()}</TD>
                      <TD><Badge tone={eventTone[e.eventType] ?? "neutral"} variant="soft" dot>{e.eventType}</Badge></TD>
                      <TD>
                        <Badge
                          tone={e.status === "Completed" ? "success" : e.status === "Failed" ? "danger" : "neutral"}
                          variant="soft" dot>{e.status}</Badge>
                      </TD>
                      <TD className="text-rose-600 text-xs max-w-md truncate">{e.error?.split("\n")[0]}</TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </CardBody>
        </Card>
      )}

      <Modal
        open={editing !== null}
        onClose={() => setEditing(null)}
        title={editing?.id ? "Edit rule" : "New rule"}
        description="Configure an event-driven automation."
        size="xl"
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
            <Button loading={saving} onClick={save} leftIcon={<Icon name="save" size={15} />}>Save rule</Button>
          </>
        }
      >
        {editing && (
          <RuleEditor rule={editing} setRule={setEditing}
            eventTypes={eventTypes ?? []} actionTypes={actionTypes ?? []} />
        )}
      </Modal>

      <Modal open={!!confirmDelete} onClose={() => setConfirmDelete(null)} title="Delete rule">
        <div className="space-y-4">
          <div className="flex items-start gap-3 p-3 rounded-lg bg-rose-50 ring-1 ring-rose-100">
            <div className="h-9 w-9 rounded-lg grid place-items-center bg-rose-100 text-rose-600 ring-1 ring-inset ring-rose-200 shrink-0">
              <Icon name="alert" size={16} />
            </div>
            <div className="text-sm text-rose-900">
              Delete <span className="font-semibold">{confirmDelete?.name}</span>? Future events will no longer trigger this automation.
            </div>
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setConfirmDelete(null)}>Cancel</Button>
            <Button variant="danger" onClick={doDelete} leftIcon={<Icon name="trash" size={15} />}>Delete rule</Button>
          </div>
        </div>
      </Modal>
    </>
  );
}

function RuleCard({
  rule, onEdit, onDelete,
}: {
  rule: WorkflowRule;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const [showJson, setShowJson] = useState(false);
  const tone = eventTone[rule.eventType] ?? "neutral";
  const toneRing: Record<Tone, string> = {
    brand: "bg-brand-50 text-brand-600 ring-brand-100",
    info: "bg-brand-50 text-brand-600 ring-brand-100",
    warning: "bg-amber-50 text-amber-600 ring-amber-100",
    success: "bg-emerald-50 text-emerald-600 ring-emerald-100",
    danger: "bg-rose-50 text-rose-600 ring-rose-100",
    neutral: "bg-ink-100 text-ink-600 ring-ink-200",
    accent: "bg-accent-50 text-accent-600 ring-accent-100",
  };
  return (
    <Card className={cn("transition-all hover:shadow-card-hover hover:border-ink-300", !rule.isActive && "opacity-75")}>
      <CardBody className="flex items-start gap-4 p-5">
        {/* Status indicator + event icon */}
        <div className="flex flex-col items-center gap-2 shrink-0">
          <div className={cn(
            "h-11 w-11 rounded-xl grid place-items-center ring-1 ring-inset",
            toneRing[tone],
          )}>
            <Icon name="zap" size={18} />
          </div>
          <span
            className={cn(
              "h-2 w-2 rounded-full",
              rule.isActive ? "bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.6)]" : "bg-ink-300",
            )}
            title={rule.isActive ? "Active" : "Disabled"}
            aria-label={rule.isActive ? "Active" : "Disabled"}
          />
        </div>

        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap mb-1">
            <span className="font-semibold text-ink-900 text-base">{rule.name}</span>
            <Badge tone={tone} variant="soft" dot>{rule.eventType}</Badge>
            <span className="inline-flex items-center gap-1">
              <Badge tone="neutral" variant="soft" size="sm" className="tabular-nums">priority {rule.priority}</Badge>
              <InfoHint title="Priority" side="top">When several rules match the same event, lower numbers run first.</InfoHint>
            </span>
            {!rule.isActive && <Badge tone="danger" variant="soft" size="sm">Disabled</Badge>}
          </div>
          {rule.description && (
            <p className="text-sm text-ink-600 mb-2">{rule.description}</p>
          )}

          {/* Action chain */}
          <div className="flex items-center gap-1.5 flex-wrap">
            <span className="text-[10px] font-semibold uppercase tracking-[0.08em] text-ink-500 mr-1">Actions</span>
            {rule.actions.map((a, i: number) => (
              <span key={i} className="inline-flex items-center gap-1.5">
                {i > 0 && <Icon name="arrowRight" size={11} className="text-ink-300" />}
                <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-ink-100/80 ring-1 ring-inset ring-ink-200 text-xs font-medium text-ink-700">
                  <Icon name={actionIcon[a.actionType] ?? "zap"} size={11} className="text-ink-500" />
                  {a.actionType}
                </span>
              </span>
            ))}
            {rule.continueOnError && (
              <span className="inline-flex items-center gap-1 ml-1">
                <Badge tone="neutral" variant="outline" size="sm">continue on error</Badge>
                <InfoHint title="Continue on error" side="top">When on, an error while this rule runs is logged and swallowed instead of interrupting the rest of the workflow. When off, the failure is raised and stops processing.</InfoHint>
              </span>
            )}
          </div>

          {rule.conditionJson && (
            <button
              onClick={() => setShowJson((s) => !s)}
              className="mt-3 inline-flex items-center gap-1 text-[11px] font-medium text-brand-700 hover:text-brand-800"
            >
              <Icon name={showJson ? "chevronDown" : "chevronRight"} size={11} />
              {showJson ? "Hide" : "Show"} condition JSON
            </button>
          )}
          {showJson && rule.conditionJson && (
            <pre className="text-xs bg-ink-50/80 border hairline rounded-lg p-3 mt-2 font-mono text-ink-800 overflow-x-auto">{rule.conditionJson}</pre>
          )}
        </div>

        <div className="flex gap-1.5 shrink-0">
          <Button variant="outline" size="sm" leftIcon={<Icon name="edit" size={13} />} onClick={onEdit}>
            Edit
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-rose-600 hover:bg-rose-50"
            leftIcon={<Icon name="trash" size={13} />}
            onClick={onDelete}
          >
            Delete
          </Button>
        </div>
      </CardBody>
    </Card>
  );
}

function RuleEditor({
  rule, setRule, eventTypes, actionTypes,
}: {
  rule: Rule;
  setRule: (r: Rule) => void;
  eventTypes: string[];
  actionTypes: string[];
}) {
  function setAction(i: number, patch: Partial<Rule["actions"][number]>) {
    const next = [...rule.actions];
    next[i] = { ...next[i], ...patch };
    setRule({ ...rule, actions: next });
  }

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <Input label="Rule name" required value={rule.name}
          onChange={(e) => setRule({ ...rule, name: e.target.value })} containerClassName="sm:col-span-2" />
        <Input label="Priority" type="number" value={rule.priority}
          hint="Lower runs first"
          onChange={(e) => setRule({ ...rule, priority: parseInt(e.target.value) || 100 })} />
        <Select label="On event" value={rule.eventType}
          onChange={(e) => setRule({ ...rule, eventType: e.target.value })}>
          {eventTypes.map((et) => <option key={et} value={et}>{et}</option>)}
        </Select>
        <Input label="Description" value={rule.description ?? ""}
          onChange={(e) => setRule({ ...rule, description: e.target.value })}
          containerClassName="sm:col-span-2" />
      </div>

      <Textarea
        label="Condition JSON" hint='Optional. Example: {"all":[{"fact":"score","op":"gte","value":40}]}'
        rows={3} value={rule.conditionJson ?? ""}
        onChange={(e) => setRule({ ...rule, conditionJson: e.target.value })} />

      <div className="flex items-center gap-2 flex-wrap">
        <ToggleChip
          checked={rule.isActive}
          onChange={(v) => setRule({ ...rule, isActive: v })}
          icon="zap"
          activeTone="success"
          label={rule.isActive ? "Active" : "Disabled"}
        />
        <ToggleChip
          checked={rule.continueOnError}
          onChange={(v) => setRule({ ...rule, continueOnError: v })}
          icon="shield"
          activeTone="warning"
          label="Continue on error"
        />
      </div>

      <div>
        <div className="flex items-center justify-between mb-2">
          <div>
            <div className="text-sm font-semibold text-ink-900">Actions</div>
            <div className="text-xs text-ink-500">Run in order. Each accepts a JSON parameter blob.</div>
          </div>
          <Button size="sm" variant="outline" leftIcon={<Icon name="plus" size={14} />}
            onClick={() => setRule({ ...rule, actions: [...rule.actions, {
              actionType: actionTypes[0], parametersJson: "{}", order: rule.actions.length + 1 }] })}>
            Add action
          </Button>
        </div>
        <div className="space-y-2">
          {rule.actions.map((a, i) => (
            <div key={i} className="grid grid-cols-1 sm:grid-cols-12 gap-2 items-start bg-ink-50/60 border hairline rounded-xl p-3">
              <div className="col-span-2">
                <Input type="number" value={a.order}
                  onChange={(e) => setAction(i, { order: parseInt(e.target.value) || 1 })} />
              </div>
              <div className="col-span-3">
                <Select value={a.actionType} onChange={(e) => setAction(i, { actionType: e.target.value })}>
                  {actionTypes.map((at) => <option key={at} value={at}>{at}</option>)}
                </Select>
              </div>
              <div className="col-span-6">
                <Textarea rows={2} placeholder='{"role":"Fronter","strategy":"round-robin"}'
                  value={a.parametersJson ?? ""}
                  onChange={(e) => setAction(i, { parametersJson: e.target.value })} />
              </div>
              <div className="col-span-1 flex justify-end">
                <Button variant="ghost" size="icon" className="text-rose-600 hover:bg-rose-50"
                  onClick={() => setRule({ ...rule, actions: rule.actions.filter((_, j) => j !== i) })}
                  aria-label="Remove action" title="Remove this action">
                  <Icon name="trash" size={14} />
                </Button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function ToggleChip({
  checked, onChange, icon, activeTone, label,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  icon: IconName;
  activeTone: "success" | "warning";
  label: string;
}) {
  const cls = checked
    ? activeTone === "success"
      ? "bg-emerald-50 text-emerald-700 ring-emerald-200"
      : "bg-amber-50 text-amber-700 ring-amber-200"
    : "bg-white text-ink-500 ring-ink-200 hover:ring-ink-300";
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className={cn(
        "inline-flex items-center gap-2 px-3 py-1.5 rounded-lg ring-1 text-sm font-medium transition-all",
        cls,
      )}
    >
      <Icon name={icon} size={13} />
      <span>{label}</span>
      <span className={cn(
        "h-3 w-3 rounded-full transition-colors",
        checked ? activeTone === "success" ? "bg-emerald-500" : "bg-amber-500" : "bg-ink-300",
      )} />
    </button>
  );
}
