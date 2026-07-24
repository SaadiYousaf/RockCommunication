import { API_URL } from "../../shared/config";
import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import {
  useBulkAssignLeadsMutation,
  useBulkSetStageMutation,
  useCreateLeadMutation,
  useDialLeadMutation,
  useListCadencesQuery,
  useListLeadsQuery,
  useListUsersQuery,
  useTransitionLeadMutation,
  useBulkEnrollCadenceMutation,
} from "../../shared/api/baseApi";
import type { WorkflowStage } from "../../shared/api/types";
import {
  Avatar, Badge, Button, Card, CardBody, EmptyState, Icon, Input, Modal, PageHeader,
  Select, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, Tabs, useToast,
} from "../../shared/ui";
import { WORKFLOW_STAGES as stages, STAGE_TONE as stageTone, stageOf, dispOf } from "../../shared/constants/leadStage";




const PAGE_SIZE = 25;

function formatPhone(p: string) {
  const d = (p || "").replace(/\D/g, "");
  if (d.length === 10) return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
  return p;
}

export function LeadsPage() {
  const navigate = useNavigate();
  const toast = useToast();

  const [searchParams, setSearchParams] = useSearchParams();
  const stageParam = searchParams.get("stage");
  const initialFilter: WorkflowStage | "All" =
    stageParam && (stages as string[]).includes(stageParam) ? (stageParam as WorkflowStage) : "All";

  const [page, setPage] = useState(0);
  const [filter, setFilter] = useState<WorkflowStage | "All">(initialFilter);

  // Keep URL ?stage= in sync with the filter state, and pick up external changes (e.g. clicking
  // a different stage card in another tab/window).
  useEffect(() => {
    const next = new URLSearchParams(searchParams);
    if (filter === "All") next.delete("stage");
    else next.set("stage", filter);
    if (next.toString() !== searchParams.toString()) {
      setSearchParams(next, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filter]);

  useEffect(() => {
    const s = searchParams.get("stage");
    const next: WorkflowStage | "All" =
      s && (stages as string[]).includes(s) ? (s as WorkflowStage) : "All";
    if (next !== filter) setFilter(next);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);
  const [sort, setSort] = useState("createdAt-desc");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);
  useEffect(() => { setPage(0); }, [filter, sort, debouncedSearch]);

  const queryParams = {
    stage: filter === "All" ? undefined : filter,
    sort,
    skip: page * PAGE_SIZE,
    take: PAGE_SIZE,
  } as const;

  const { data: leadsResult, isLoading, isFetching, refetch } = useListLeadsQuery(queryParams);
  const allLeads = leadsResult?.items;
  const total = leadsResult?.total ?? 0;
  const { data: cadences } = useListCadencesQuery();
  const { data: users } = useListUsersQuery();

  const filteredClient = useMemo(() => {
    if (!allLeads) return [];
    const q = debouncedSearch.toLowerCase();
    if (!q) return allLeads;
    return allLeads.filter((l) => {
      const name = `${l.firstName} ${l.lastName}`.toLowerCase();
      return name.includes(q) || l.phoneNumber.toLowerCase().includes(q) || (l.email ?? "").toLowerCase().includes(q);
    });
  }, [allLeads, debouncedSearch]);

  // Selection state
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const allOnPageSelected = filteredClient.length > 0 && filteredClient.every((l) => selected.has(l.id));
  function toggleAll() {
    setSelected((prev) => {
      const next = new Set(prev);
      if (allOnPageSelected) filteredClient.forEach((l) => next.delete(l.id));
      else filteredClient.forEach((l) => next.add(l.id));
      return next;
    });
  }
  function toggleOne(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }
  function clearSelection() { setSelected(new Set()); }

  const [createLead, { isLoading: creating }] = useCreateLeadMutation();
  const [transitionLead] = useTransitionLeadMutation();
  const [dialLead] = useDialLeadMutation();
  const [bulkAssign] = useBulkAssignLeadsMutation();
  const [bulkStage] = useBulkSetStageMutation();
  const [bulkEnroll] = useBulkEnrollCadenceMutation();

  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ firstName: "", lastName: "", phoneNumber: "", email: "" });

  const [bulkAction, setBulkAction] = useState<"none" | "assign" | "stage" | "cadence">("none");
  const [bulkAssignee, setBulkAssignee] = useState("");
  const [bulkStageVal, setBulkStageVal] = useState<WorkflowStage>("Fronted");
  const [bulkCadence, setBulkCadence] = useState("");

  async function transition(id: string, toStage: string, name: string) {
    try {
      await transitionLead({ id, toStage: toStage as WorkflowStage, disposition: "Interested" }).unwrap();
      toast.success("Lead transitioned", `${name} → ${toStage}`);
    } catch (err: unknown) {
      toast.error("Couldn't transition", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function dialFromRow(id: string) {
    try {
      await dialLead({ leadId: id }).unwrap();
      toast.success("Calling…");
      navigate(`/leads/${id}`);
    } catch (err: unknown) {
      toast.error("Couldn't dial", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function applyBulk() {
    const ids = Array.from(selected);
    if (ids.length === 0) return;
    try {
      if (bulkAction === "assign" && bulkAssignee) {
        const r = await bulkAssign({ leadIds: ids, assigneeUserId: bulkAssignee }).unwrap();
        toast.success("Assigned", `${r.updated} updated · ${r.skipped} skipped`);
      } else if (bulkAction === "stage") {
        const r = await bulkStage({ leadIds: ids, toStage: bulkStageVal, disposition: "Interested" }).unwrap();
        const errs = r.errors.length > 0 ? `, ${r.errors.length} errors` : "";
        toast.success("Stage updated", `${r.updated} updated · ${r.skipped} skipped${errs}`);
      } else if (bulkAction === "cadence" && bulkCadence) {
        const r = await bulkEnroll({ leadIds: ids, cadenceId: bulkCadence }).unwrap();
        toast.success("Enrolled in cadence", `${r.updated} enrolled · ${r.skipped} already in`);
      }
      setBulkAction("none");
      clearSelection();
      refetch();
    } catch (err: unknown) {
      toast.error("Bulk action failed", getErrorDetail(err) ?? "Try again.");
    }
  }

  function exportCsv() {
    const params = new URLSearchParams();
    if (filter !== "All") params.set("stage", filter);
    const token = localStorage.getItem("auth")
      ? JSON.parse(localStorage.getItem("auth")!)?.accessToken
      : null;
    if (!token) { toast.error("Not authenticated"); return; }
    fetch(`${API_URL}/api/leads/export.csv?${params.toString()}`, {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then((r) => { if (!r.ok) throw new Error("Export failed"); return r.blob(); })
      .then((blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = `leads-${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
      })
      .catch((err) => toast.error("Export failed", getErrorDetail(err) ?? "Export failed"));
  }

  async function submitCreate(e: React.FormEvent) {
    e.preventDefault();
    try {
      await createLead(form).unwrap();
      toast.success("Lead created", `${form.firstName} ${form.lastName}`);
      setForm({ firstName: "", lastName: "", phoneNumber: "", email: "" });
      setOpen(false);
    } catch (err: unknown) {
      toast.error("Couldn't create lead", getErrorDetail(err) ?? "Try again.");
    }
  }

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const tabItems: { value: WorkflowStage | "All"; label: string }[] = [
    { value: "All", label: `All${filter === "All" && total ? ` · ${total}` : ""}` },
    ...stages.map((s) => ({ value: s, label: s })),
  ];

  return (
    <>
      <PageHeader
        title="Leads"
        description="Search, transition, and manage every lead in your pipeline."
        actions={
          <div className="flex gap-2">
            <Button variant="outline" leftIcon={<Icon name="download" size={15} />} onClick={exportCsv}>
              Export CSV
            </Button>
            <Button leftIcon={<Icon name="plus" size={15} />} onClick={() => setOpen(true)}>
              New lead
            </Button>
          </div>
        }
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-5">
        <Stat label="Total leads" value={total} icon={<Icon name="list" size={16} />} tone="brand" />
        <Stat label="On this page" value={filteredClient.length} icon={<Icon name="rows" size={16} />} tone="accent"
              hint={total > 0 ? `Page ${page + 1} of ${totalPages}` : undefined} />
        <Stat label="Selected" value={selected.size} icon={<Icon name="check" size={16} />}
              tone={selected.size > 0 ? "success" : "neutral"}
              hint={selected.size > 0 ? "Bulk actions available" : "Tick rows to bulk-edit"} />
        <Stat label="Stage filter" value={filter === "All" ? "All stages" : filter} icon={<Icon name="filter" size={16} />} tone="warning" />
      </div>

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[260px]">
            <Input
              leftIcon={<Icon name="search" size={16} />}
              placeholder="Search by name, phone, or email..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <Select value={sort} onChange={(e) => setSort(e.target.value)} className="h-10 w-56">
            <option value="createdAt-desc">Newest first</option>
            <option value="createdAt-asc">Oldest first</option>
            <option value="score-desc">Highest score</option>
            <option value="score-asc">Lowest score</option>
            <option value="name-asc">Name (A → Z)</option>
            <option value="name-desc">Name (Z → A)</option>
            <option value="stage-asc">By stage</option>
          </Select>
        </CardBody>
        <div className="px-5 -mt-2 pb-1 overflow-x-auto">
          <Tabs value={filter} onChange={setFilter} items={tabItems} />
        </div>
      </Card>

      {selected.size > 0 && (
        <div className="mb-4 rounded-2xl bg-gradient-to-r from-brand-50 via-white to-accent-50 ring-1 ring-brand-200 shadow-card overflow-hidden">
          <div className="flex items-center gap-3 flex-wrap p-4">
            <div className="flex items-center gap-2.5">
              <div className="h-9 w-9 rounded-lg grid place-items-center bg-brand-100 text-brand-700 ring-1 ring-inset ring-brand-200">
                <Icon name="check" size={16} />
              </div>
              <div>
                <div className="text-sm font-semibold text-ink-900">{selected.size} selected</div>
                <div className="text-[11px] text-ink-500">Choose a bulk action</div>
              </div>
            </div>
            <div className="h-8 w-px bg-ink-200/70" />
            <Select className="h-9 w-44 text-sm" value={bulkAction} onChange={(e) => setBulkAction(e.target.value as any)}>
              <option value="none">Choose action…</option>
              <option value="assign">Assign agent</option>
              <option value="stage">Set stage</option>
              <option value="cadence">Enroll in cadence</option>
            </Select>

            {bulkAction === "assign" && (
              <Select className="h-9 w-56 text-sm" value={bulkAssignee} onChange={(e) => setBulkAssignee(e.target.value)}>
                <option value="">Pick user…</option>
                {users?.map((u) => <option key={u.id} value={u.id}>{u.userName}</option>)}
              </Select>
            )}
            {bulkAction === "stage" && (
              <Select className="h-9 w-44 text-sm" value={bulkStageVal} onChange={(e) => setBulkStageVal(e.target.value as WorkflowStage)}>
                {stages.map((s) => <option key={s} value={s}>{s}</option>)}
              </Select>
            )}
            {bulkAction === "cadence" && (
              <Select className="h-9 w-56 text-sm" value={bulkCadence} onChange={(e) => setBulkCadence(e.target.value)}>
                <option value="">Pick cadence…</option>
                {cadences?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </Select>
            )}

            <div className="ml-auto flex gap-2">
              <Button size="sm" variant="ghost" onClick={clearSelection} leftIcon={<Icon name="x" size={13} />}>Clear</Button>
              <Button size="sm" disabled={bulkAction === "none"} onClick={applyBulk} leftIcon={<Icon name="zap" size={13} />}>
                Apply
              </Button>
            </div>
          </div>
        </div>
      )}

      {isLoading ? (
        <Card><CardBody>
          {[0, 1, 2, 3, 4].map((i) => (
            <div key={i} className="flex items-center gap-3 py-3 border-b hairline last:border-0">
              <Skeleton className="h-9 w-9 rounded-full" />
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-5 w-16 rounded-full ml-auto" />
            </div>
          ))}
        </CardBody></Card>
      ) : filteredClient.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="list" size={20} />}
            title={search || filter !== "All" ? "No leads match your filter" : "No leads yet"}
            description={search || filter !== "All"
              ? "Try clearing your search or picking a different stage."
              : "Get started by creating your first lead."}
            action={
              <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>
                New lead
              </Button>
            }
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH className="w-8">
                <input type="checkbox" checked={allOnPageSelected} onChange={toggleAll} />
              </TH>
              <TH>Lead</TH>
              <TH>Phone</TH>
              <TH>Stage</TH>
              <TH>Disposition</TH>
              <TH>Created</TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {filteredClient.map((l) => {
              const stage = stageOf(l.stage);
              const name = `${l.firstName} ${l.lastName}`;
              return (
                <TR key={l.id} className={selected.has(l.id) ? "bg-brand-50/50" : ""}>
                  <TD>
                    <input type="checkbox" checked={selected.has(l.id)} onChange={() => toggleOne(l.id)} />
                  </TD>
                  <TD>
                    <div className="flex items-center gap-3">
                      <Avatar name={name} size={36} />
                      <div className="min-w-0">
                        <Link to={`/leads/${l.id}`} className="font-medium text-ink-900 hover:text-brand-700">
                          {name}
                        </Link>
                        {l.email && <div className="text-xs text-ink-500 truncate">{l.email}</div>}
                      </div>
                    </div>
                  </TD>
                  <TD className="font-mono text-xs text-ink-700">{formatPhone(l.phoneNumber)}</TD>
                  <TD><Badge tone={stageTone[stage]} variant="soft" dot>{stage}</Badge></TD>
                  <TD className="text-ink-600 text-xs">{dispOf(l.disposition)}</TD>
                  <TD className="text-ink-500 text-xs">{new Date(l.createdAt).toLocaleDateString()}</TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1.5">
                      <Button variant="outline" size="sm" leftIcon={<Icon name="phoneCall" size={13} />}
                        onClick={() => dialFromRow(l.id)}>Dial</Button>
                      <Select
                        defaultValue=""
                        onChange={(e) => {
                          if (e.target.value) {
                            transition(l.id, e.target.value, name);
                            e.currentTarget.value = "";
                          }
                        }}
                        className="h-8 text-xs py-0 w-28"
                        aria-label="Move to stage"
                      >
                        <option value="">Move to…</option>
                        {stages.map((s) => <option key={s} value={s}>{s}</option>)}
                      </Select>
                      <Link to={`/leads/${l.id}`}>
                        <Button variant="ghost" size="sm" rightIcon={<Icon name="chevronRight" size={13} />}>Open</Button>
                      </Link>
                    </div>
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
      )}

      {total > PAGE_SIZE && (
        <div className="flex items-center justify-between mt-4 text-sm">
          <div className="text-ink-500">
            Showing <span className="font-medium text-ink-700 tabular-nums">{page * PAGE_SIZE + 1}–{Math.min(total, (page + 1) * PAGE_SIZE)}</span> of <span className="font-medium text-ink-700 tabular-nums">{total}</span>
          </div>
          <div className="flex items-center gap-1.5">
            <Button size="sm" variant="outline" disabled={page === 0 || isFetching}
              leftIcon={<Icon name="chevronLeft" size={13} />}
              onClick={() => setPage(p => Math.max(0, p - 1))}>Prev</Button>
            <span className="px-3 py-1.5 text-xs text-ink-600 tabular-nums">
              Page {page + 1} of {totalPages}
            </span>
            <Button size="sm" variant="outline" disabled={page + 1 >= totalPages || isFetching}
              rightIcon={<Icon name="chevronRight" size={13} />}
              onClick={() => setPage(p => p + 1)}>Next</Button>
          </div>
        </div>
      )}

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Create new lead"
        description="Add a lead to your pipeline. You can edit or assign it later."
        size="lg"
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="create-lead" type="submit" loading={creating}>Create lead</Button>
          </>
        }
      >
        {/* secure: Fronter/Verifier/Closer must type lead data, not paste it. */}
        <form id="create-lead" onSubmit={submitCreate} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input label="First name" required secure value={form.firstName}
            onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
          <Input label="Last name" required secure value={form.lastName}
            onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
          <Input label="Phone" required secure value={form.phoneNumber}
            leftIcon={<Icon name="phone" size={14} />}
            onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} />
          <Input label="Email" type="email" secure value={form.email}
            onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </form>
      </Modal>
    </>
  );
}
