import { useEffect, useMemo, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { useDashboardQuery, useMetricCatalogQuery, useAgencyOptionsQuery } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, Icon, Input, Modal, PageHeader,
  Select, Skeleton, useToast, cn,
} from "../../shared/ui";
import { usePersistentState } from "../../shared/hooks/usePersistentState";

/** A saved dashboard preset — name + chosen metric keys + date range strategy. */
interface DashboardPreset {
  id: string;
  name: string;
  metrics: string[];
  /** Either a fixed lookback in days, or "custom" if the user wants explicit dates. */
  range: { kind: "rolling"; days: number } | { kind: "custom"; from: string; to: string };
}

const DEFAULT_PRESETS: DashboardPreset[] = [
  {
    id: "preset-leads-7d",
    name: "Leads · 7 days",
    metrics: ["totalLeads", "frontedLeads", "conversionRate"],
    range: { kind: "rolling", days: 7 },
  },
  {
    id: "preset-sales-30d",
    name: "Sales · 30 days",
    metrics: ["closedSales", "fundedSales", "totalPremium"],
    range: { kind: "rolling", days: 30 },
  },
  {
    id: "preset-callcenter",
    name: "Call center · today",
    metrics: ["answerRate", "abandonRate", "avgHandleTime", "occupancy", "serviceLevel"],
    range: { kind: "rolling", days: 0 },
  },
];

export function KpiDashboardPage() {
  const toast = useToast();
  const { data: catalog } = useMetricCatalogQuery();

  // SuperAdmin has no agency of their own, so KPIs need a target agency (like the Team page).
  const isSuperAdmin = useSelector((s: RootState) => s.auth.user?.roles?.includes("SuperAdmin") ?? false);
  const { data: agencyOptions } = useAgencyOptionsQuery(undefined, { skip: !isSuperAdmin });
  const [agencyId, setAgencyId] = useState("");
  useEffect(() => {
    if (isSuperAdmin && !agencyId && agencyOptions && agencyOptions.length) setAgencyId(agencyOptions[0].id);
  }, [isSuperAdmin, agencyOptions, agencyId]);

  // Persisted user-defined presets (per-browser via localStorage).
  // Defaults only seed on first run; we never overwrite user customisations.
  const [presets, setPresets] = usePersistentState<DashboardPreset[]>(
    "kpi.presets",
    DEFAULT_PRESETS,
  );
  const [activePresetId, setActivePresetId] = usePersistentState<string | null>(
    "kpi.active-preset",
    DEFAULT_PRESETS[0].id,
  );

  const [from, setFrom] = useState(() => new Date(Date.now() - 30 * 86400 * 1000).toISOString().slice(0, 10));
  const [to, setTo] = useState(() => new Date(Date.now() + 86400 * 1000).toISOString().slice(0, 10));
  // Which quick-range button is active (null once the user edits the dates by hand), so the
  // toolbar shows the current selection instead of leaving every button looking unselected.
  const [activeRange, setActiveRange] = useState<number | null>(30);
  const [picked, setPicked] = useState<Record<string, boolean>>({});
  const [showPicker, setShowPicker] = useState(false);
  const [saveOpen, setSaveOpen] = useState(false);
  const [newName, setNewName] = useState("");
  const [confirmDelete, setConfirmDelete] = useState<DashboardPreset | null>(null);

  // Apply a preset → state.
  function applyPreset(preset: DashboardPreset) {
    setActivePresetId(preset.id);
    const newPicked: Record<string, boolean> = {};
    preset.metrics.forEach((k) => { newPicked[k] = true; });
    setPicked(newPicked);
    if (preset.range.kind === "rolling") {
      setFrom(new Date(Date.now() - preset.range.days * 86400 * 1000).toISOString().slice(0, 10));
      setTo(new Date(Date.now() + 86400 * 1000).toISOString().slice(0, 10));
    } else {
      setFrom(preset.range.from);
      setTo(preset.range.to);
    }
  }

  // Apply the initial preset on mount (if one is active and we haven't manually changed).
  // Run once via lazy init in `picked`.
  useMemo(() => {
    if (Object.keys(picked).length === 0) {
      const active = presets.find((p) => p.id === activePresetId) ?? presets[0];
      if (active) applyPreset(active);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const selected = Object.keys(picked).filter((k) => picked[k]);
  const waitingForAgency = isSuperAdmin && !agencyId;
  const { data: values, isFetching } = useDashboardQuery(
    { from, to, metrics: selected.length > 0 ? selected : undefined, agencyId: isSuperAdmin ? agencyId : undefined },
    { skip: waitingForAgency },
  );

  const groups = useMemo(() => {
    return (values ?? []).reduce<Record<string, typeof values>>((acc, v) => {
      const g = v.group ?? "General";
      (acc[g] ||= []).push(v);
      return acc;
    }, {});
  }, [values]);

  const dirty = useMemo(() => {
    if (!activePresetId) return selected.length > 0;
    const p = presets.find((p) => p.id === activePresetId);
    if (!p) return true;
    if (p.metrics.length !== selected.length) return true;
    return p.metrics.some((m) => !picked[m]);
  }, [activePresetId, presets, picked, selected]);

  function applyRange(days: number) {
    setFrom(new Date(Date.now() - days * 86400 * 1000).toISOString().slice(0, 10));
    setTo(new Date(Date.now() + 86400 * 1000).toISOString().slice(0, 10));
    setActiveRange(days);
  }

  function saveAsPreset() {
    if (!newName.trim()) return;
    const id = `preset-${Date.now().toString(36)}`;
    const preset: DashboardPreset = {
      id, name: newName.trim(),
      metrics: selected,
      range: { kind: "custom", from, to },
    };
    setPresets((cur) => [...cur, preset]);
    setActivePresetId(id);
    setNewName("");
    setSaveOpen(false);
    toast.success("Dashboard saved", `"${preset.name}" is now in your saved presets.`);
  }

  function overwriteActivePreset() {
    if (!activePresetId) return;
    const updated = presets.map((p) =>
      p.id === activePresetId
        ? { ...p, metrics: selected, range: { kind: "custom" as const, from, to } }
        : p,
    );
    setPresets(updated);
    toast.success("Dashboard updated");
  }

  function removePreset(p: DashboardPreset) {
    setPresets((cur) => cur.filter((x) => x.id !== p.id));
    if (activePresetId === p.id) setActivePresetId(null);
    setConfirmDelete(null);
    toast.success("Dashboard removed");
  }

  return (
    <>
      <PageHeader
        eyebrow="Analytics"
        title="KPI Dashboards"
        description="Save your favourite metric views and switch between them in one click."
        actions={
          <>
            {isSuperAdmin && (
              <Select aria-label="Agency" value={agencyId} onChange={(e) => setAgencyId(e.target.value)} className="w-48">
                {(!agencyOptions || agencyOptions.length === 0) && <option value="">No agencies</option>}
                {(agencyOptions ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
              </Select>
            )}
            <Button
              variant="outline"
              leftIcon={<Icon name="filter" size={16} />}
              onClick={() => setShowPicker((s) => !s)}
            >
              {selected.length === 0 ? "All metrics" : `${selected.length} selected`}
            </Button>
            {dirty && activePresetId && (
              <Button
                variant="outline"
                leftIcon={<Icon name="save" size={16} />}
                onClick={overwriteActivePreset}
              >
                Save changes
              </Button>
            )}
            <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setSaveOpen(true)}>
              Save as new
            </Button>
          </>
        }
      />

      {/* Preset chips — persistent dashboard switcher */}
      <Card className="mb-4">
        {presets.length === 0 ? (
          <CardBody className="flex items-center gap-3 py-3.5">
            <div className="h-9 w-9 rounded-lg bg-ink-100 grid place-items-center text-ink-400 shrink-0">
              <Icon name="bookmark" size={16} />
            </div>
            <div className="text-sm text-ink-600 leading-snug">
              No saved dashboards yet. Choose the metrics you want below, then{" "}
              <span className="font-medium text-ink-800">Save as new</span> to pin the view here for one-click access.
            </div>
          </CardBody>
        ) : (
          <CardBody className="flex items-center gap-2 flex-wrap">
            <span className="text-[11px] font-semibold text-ink-500 uppercase tracking-[0.12em] mr-1">
              Saved
            </span>
            {presets.map((p) => {
              const active = activePresetId === p.id;
              return (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => applyPreset(p)}
                  className={cn(
                    "group inline-flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-medium border transition-all",
                    active
                      ? "bg-brand-50 border-brand-300 text-brand-700 ring-2 ring-brand-500/20"
                      : "bg-white border-ink-200 text-ink-700 hover:border-ink-300",
                  )}
                >
                  <Icon name={active ? "check" : "chart"} size={12} />
                  <span>{p.name}</span>
                  <Badge tone={active ? "brand" : "neutral"} variant="soft" size="sm">
                    {p.metrics.length}
                  </Badge>
                  <span
                    role="button"
                    tabIndex={0}
                    aria-label={`Delete ${p.name}`}
                    onClick={(e) => { e.stopPropagation(); setConfirmDelete(p); }}
                    onKeyDown={(e) => { if (e.key === "Enter") { e.stopPropagation(); setConfirmDelete(p); } }}
                    className="opacity-0 group-hover:opacity-60 hover:opacity-100 hover:text-rose-600 transition-opacity ml-0.5 -mr-1"
                  >
                    <Icon name="x" size={12} />
                  </span>
                </button>
              );
            })}
          </CardBody>
        )}
      </Card>

      <Card className="mb-6">
        <CardBody>
          <div className="flex flex-wrap items-end gap-4">
            <div className="flex gap-1.5">
              {[
                { label: "Today",   days: 0 },
                { label: "7 days",  days: 7 },
                { label: "30 days", days: 30 },
                { label: "90 days", days: 90 },
              ].map((p) => (
                <button
                  key={p.label}
                  onClick={() => applyRange(p.days)}
                  className={cn(
                    "px-3 py-1.5 rounded-lg text-xs font-medium transition-colors",
                    activeRange === p.days
                      ? "bg-brand-600 text-white shadow-sm"
                      : "bg-ink-100 hover:bg-ink-200 text-ink-700",
                  )}
                >{p.label}</button>
              ))}
            </div>
            <div className="h-9 w-px bg-ink-200 hidden sm:block" />
            <Input label="From" type="date" value={from} onChange={(e) => { setFrom(e.target.value); setActiveRange(null); }} containerClassName="w-44" />
            <Input label="To"   type="date" value={to}   onChange={(e) => { setTo(e.target.value); setActiveRange(null); }}   containerClassName="w-44" />
            {isFetching && <span className="text-xs text-ink-500 pb-2">Updating…</span>}
          </div>

          {showPicker && (
            <div className="mt-5 pt-5 border-t hairline">
              <div className="text-xs font-medium text-ink-700 mb-2">Filter metrics (none = all)</div>
              <div className="flex flex-wrap gap-2">
                {catalog?.map((m) => {
                  const active = !!picked[m.key];
                  return (
                    <button
                      key={m.key} type="button"
                      onClick={() => setPicked((p) => ({ ...p, [m.key]: !p[m.key] }))}
                      className={cn(
                        "px-3 py-1.5 rounded-lg text-xs font-medium border transition-colors",
                        active ? "bg-brand-50 border-brand-300 text-brand-700"
                               : "bg-white border-ink-200 text-ink-700 hover:border-ink-300",
                      )}
                    >
                      {active && <Icon name="check" size={12} className="mr-1 inline-block" />}{m.label}
                    </button>
                  );
                })}
              </div>
              {selected.length > 0 && (
                <button
                  className="text-xs text-ink-500 hover:text-ink-700 mt-3"
                  onClick={() => setPicked({})}
                >Clear selection</button>
              )}
            </div>
          )}
        </CardBody>
      </Card>

      {waitingForAgency ? (
        <Card><CardBody className="py-10">
          <div className="text-center max-w-md mx-auto">
            <div className="mx-auto mb-4 h-14 w-14 rounded-2xl bg-ink-100 grid place-items-center text-ink-400">
              <Icon name="building" size={24} />
            </div>
            <h3 className="text-base font-semibold text-ink-900 mb-1">Pick an agency</h3>
            <p className="text-sm text-ink-500">
              {!agencyOptions || agencyOptions.length === 0
                ? "No agencies exist yet. Create one from the Agencies page to see its KPIs."
                : "Choose an agency from the selector above to view its KPI dashboards."}
            </p>
          </div>
        </CardBody></Card>
      ) : isFetching && !values ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
          {[0, 1, 2, 3, 4, 5, 6, 7].map((i) => <Skeleton key={i} className="h-28 rounded-xl" />)}
        </div>
      ) : !values || values.length === 0 ? (
        <Card><CardBody className="py-10">
          <div className="text-center max-w-md mx-auto">
            <div className="mx-auto mb-4 h-14 w-14 rounded-2xl bg-ink-100 grid place-items-center text-ink-400">
              <Icon name="chart" size={24} />
            </div>
            <h3 className="text-base font-semibold text-ink-900 mb-1">No data for this range</h3>
            <p className="text-sm text-ink-500 mb-5">
              {activeRange === 0
                ? "There's no activity recorded today yet. Widen the range to see recent numbers."
                : "Nothing was recorded in the selected dates. Try a wider range or show every metric."}
            </p>
            <div className="flex items-center justify-center gap-2 flex-wrap">
              <Button variant="primary" size="sm" leftIcon={<Icon name="calendar" size={14} />}
                onClick={() => applyRange(90)}>
                Widen to 90 days
              </Button>
              {selected.length > 0 && (
                <Button variant="outline" size="sm" leftIcon={<Icon name="filter" size={14} />}
                  onClick={() => setPicked({})}>
                  Show all metrics
                </Button>
              )}
            </div>
          </div>
        </CardBody></Card>
      ) : (
        Object.entries(groups).map(([group, list]) => (
          <div key={group} className="mb-6">
            <div className="flex items-center gap-3 mb-3">
              <h2 className="text-sm font-semibold text-ink-700 uppercase tracking-wider">{group}</h2>
              <Badge tone="neutral" variant="soft">{list?.length ?? 0}</Badge>
              <div className="flex-1 h-px bg-ink-200" />
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
              {list?.map((v) => (
                <div
                  key={v.key}
                  className="surface p-5 hover:shadow-card-hover transition-shadow"
                >
                  <div className="text-xs font-medium text-ink-500 uppercase tracking-wide mb-2">{v.label}</div>
                  <div className="text-3xl font-semibold tracking-tight text-ink-900 tabular-nums">
                    {v.value.toLocaleString()}
                    {v.unit && <span className="text-sm text-ink-500 ml-1.5 font-normal">{v.unit}</span>}
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))
      )}

      <Modal
        open={saveOpen}
        onClose={() => setSaveOpen(false)}
        title="Save dashboard"
        description="Give this set of metrics + date range a name. You'll be able to switch back in one click."
        size="md"
        footer={
          <>
            <Button variant="ghost" onClick={() => setSaveOpen(false)}>Cancel</Button>
            <Button disabled={!newName.trim() || selected.length === 0} onClick={saveAsPreset}>
              Save dashboard
            </Button>
          </>
        }
      >
        <Input
          label="Dashboard name" required autoFocus
          placeholder="e.g. Closer team — week"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          hint={selected.length === 0 ? "Pick at least one metric first." : `${selected.length} metric(s) will be saved.`}
        />
      </Modal>

      <Modal
        open={confirmDelete !== null}
        onClose={() => setConfirmDelete(null)}
        title="Delete this dashboard?"
        description={confirmDelete ? `"${confirmDelete.name}" will be removed from your saved list.` : ""}
        size="sm"
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmDelete(null)}>Cancel</Button>
            <Button variant="danger" onClick={() => confirmDelete && removePreset(confirmDelete)}>
              Delete
            </Button>
          </>
        }
      >
        <div className="text-sm text-ink-700">You can always recreate it later.</div>
      </Modal>
    </>
  );
}
