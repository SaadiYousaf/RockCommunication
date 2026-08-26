import type { LeadList } from "../../shared/api/types";
import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useRef, useState } from "react";
import {
  useImportLeadsCsvMutation, useLeadListsQuery, useListImportBatchesQuery, useUpsertLeadListMutation,
} from "../../shared/api/baseApi";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast, cn,
} from "../../shared/ui";
import { Can, Perm } from "../../shared/auth/permissions";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { LISTS_MSG } from "./messages";

export function LeadListsPage() {
  const { data: lists, isLoading } = useLeadListsQuery();
  const [upsert, { isLoading: saving }] = useUpsertLeadListMutation();
  const [importCsv, { isLoading: importing }] = useImportLeadsCsvMutation();
  const toast = useToast();
  const confirm = useConfirm();

  const [search, setSearch] = useState("");
  const [togglingId, setTogglingId] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [activeListId, setActiveListId] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [filename, setFilename] = useState<string>("");

  const { data: batches, isLoading: batchesLoading } = useListImportBatchesQuery(activeListId!, { skip: !activeListId });

  const filtered = useMemo(() => {
    if (!lists) return [];
    const q = search.trim().toLowerCase();
    return q ? lists.filter((l) => l.name.toLowerCase().includes(q)) : lists;
  }, [lists, search]);

  const { sorted, dirFor, toggle: sortBy } = useTableSort(filtered, {
    accessors: {
      leadCount: (l) => l.leadCount ?? 0,
      status: (l) => (l.isActive ? "Active" : "Inactive"),
    },
  });

  const sel = useRowSelection(sorted.map((l) => l.id));

  function exportSelected() {
    const chosen = sorted.filter((l) => sel.isSelected(l.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (l) => l.name },
      { header: "Count", value: (l) => l.leadCount ?? 0 },
      { header: "Created", value: (l) => (l.isActive ? "Active" : "Inactive") },
    ], `lead-lists-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(LISTS_MSG.exportReadyTitle, LISTS_MSG.exportRows(chosen.length));
  }

  const activeList = lists?.find((l) => l.id === activeListId);

  const stats = useMemo(() => {
    const total = lists?.length ?? 0;
    const active = lists?.filter((l) => l.isActive).length ?? 0;
    const totalLeads = lists?.reduce((s, l) => s + (l.leadCount ?? 0), 0) ?? 0;
    return { total, active, totalLeads };
  }, [lists]);

  async function createList(e: React.FormEvent) {
    e.preventDefault();
    try {
      await upsert({ id: null, name, isActive: true }).unwrap();
      toast.success(LISTS_MSG.listCreatedTitle, name);
      setName(""); setOpen(false);
    } catch (err: unknown) {
      toast.error(LISTS_MSG.createFailedTitle, getErrorDetail(err) ?? LISTS_MSG.retry);
    }
  }

  async function toggle(l: LeadList) {
    if (l.isActive && !(await confirm({
      title: LISTS_MSG.disableConfirmTitle(l.name),
      description: LISTS_MSG.disableConfirmDesc,
      confirmLabel: LISTS_MSG.disableConfirmLabel,
      danger: true,
    }))) return;
    setTogglingId(l.id);
    try {
      await upsert({ id: l.id, name: l.name, isActive: !l.isActive }).unwrap();
      toast.success(l.isActive ? LISTS_MSG.listDisabled : LISTS_MSG.listEnabled);
    } catch (err: unknown) {
      toast.error(LISTS_MSG.updateFailedTitle, getErrorDetail(err) ?? LISTS_MSG.retry);
    } finally {
      setTogglingId(null);
    }
  }

  async function runImport() {
    if (!activeListId) return;
    const file = fileRef.current?.files?.[0];
    if (!file) {
      toast.warning(LISTS_MSG.noFileTitle, LISTS_MSG.noFileDesc);
      return;
    }
    try {
      const result = await importCsv({ listId: activeListId, file }).unwrap();
      toast.success(LISTS_MSG.importFinishedTitle,
        LISTS_MSG.importSummary(result?.imported ?? 0, result?.duplicates ?? 0, result?.dncScrubbed ?? 0));
      if (fileRef.current) fileRef.current.value = "";
      setFilename("");
    } catch (err: unknown) {
      toast.error(LISTS_MSG.importFailedTitle, getErrorDetail(err) ?? LISTS_MSG.retry);
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Pipeline"
        title="Lead lists"
        description="Group leads into named lists and import them via CSV (automatically scrubbed against your DNC list)."
        actions={<Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New list</Button></Can>}
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-6">
        <Stat label="Total lists" value={stats.total}      icon={<Icon name="inbox" size={16} />} tone="brand" />
        <Stat label="Active"      value={stats.active}     icon={<Icon name="check" size={16} />} tone="success" />
        <Stat label="Total leads" value={stats.totalLeads.toLocaleString()} icon={<Icon name="list" size={16} />}  tone="accent" />
      </div>

      <Card className="mb-4">
        <CardBody>
          <SearchInput
            value={search} onChange={setSearch}
            placeholder={LISTS_MSG.searchPlaceholder}
          />
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="inbox" size={20} />}
            title={search ? LISTS_MSG.listsEmptyMatchTitle : LISTS_MSG.listsEmptyTitle}
            description={search ? LISTS_MSG.listsEmptyMatchDesc : LISTS_MSG.listsEmptyDesc}
            action={!search ? <Can permission={Perm.CampaignsManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New list</Button></Can> : undefined}
          />
        </CardBody></Card>
      ) : (
        <>
        <Table>
          <THead>
            <TR>
              <TH className="w-10"><Checkbox aria-label="Select all lists" {...sel.allCheckboxProps} /></TH>
              <TH sortDir={dirFor("name")} onClick={() => sortBy("name")}>Name</TH>
              <TH sortDir={dirFor("leadCount")} onClick={() => sortBy("leadCount")}>Leads</TH>
              <TH sortDir={dirFor("status")} onClick={() => sortBy("status")}>Status</TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {sorted.map((l) => {
              const isActive = activeListId === l.id;
              return (
                <TR key={l.id} className={cn((isActive || sel.isSelected(l.id)) && "bg-brand-50/40")}>
                  <TD onClick={(ev) => ev.stopPropagation()}>
                    <Checkbox aria-label={`Select ${l.name}`} {...sel.checkboxProps(l.id)} />
                  </TD>
                  <TD>
                    <div className="flex items-center gap-3 min-w-0">
                      <div className="h-9 w-9 rounded-lg bg-brand-50 text-brand-600 grid place-items-center shrink-0">
                        <Icon name="inbox" size={16} />
                      </div>
                      <div className="font-medium text-ink-900 truncate">{l.name}</div>
                      {isActive && <Badge tone="brand" variant="soft">Selected</Badge>}
                    </div>
                  </TD>
                  <TD className="font-semibold text-ink-900 tabular-nums">{l.leadCount?.toLocaleString() ?? 0}</TD>
                  <TD>
                    {l.isActive
                      ? <Badge tone="success" variant="soft" dot>Active</Badge>
                      : <Badge tone="neutral" variant="soft">Inactive</Badge>}
                  </TD>
                  <TD>
                    <div className="flex justify-end gap-1.5">
                      <Button variant={isActive ? "primary" : "outline"} size="sm"
                        onClick={() => setActiveListId(l.id)}>
                        {isActive ? "Selected" : "Select"}
                      </Button>
                      <Can permission={Perm.CampaignsManage}>
                        <Button variant="ghost" size="sm" loading={togglingId === l.id}
                          title={l.isActive ? "Hide this list from new campaigns" : "Make this list available again"}
                          onClick={() => toggle(l)}>
                          {l.isActive ? "Disable" : "Enable"}
                        </Button>
                      </Can>
                    </div>
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
        <BulkActionBar
          count={sel.selectedCount} itemNoun="list" onClear={sel.clear}
          actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
        />
        </>
      )}

      {/* Import section — only when a list is selected */}
      {activeList && (
        <Card className="mt-6">
          <CardHeader
            title={
              <span className="flex items-center gap-2">
                <Icon name="doc" size={18} />
                Import CSV into <span className="text-brand-700">{activeList.name}</span>
              </span>
            }
            subtitle={
              <span className="inline-flex flex-wrap items-center gap-1">
                Columns:{" "}
                <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">
                  firstname,lastname,phone,email,state,postal,source,jornaya
                </code>
                <InfoHint title="CSV columns" side="right">The first row must be these headers. <strong>source</strong> is where the lead came from (e.g. a vendor or campaign); <strong>jornaya</strong> is the Jornaya LeadiD — a consent-tracking token that proves the lead agreed to be contacted. Any extra columns are ignored.</InfoHint>{" "}
                — DNC numbers are scrubbed automatically.
              </span>
            }
          />
          <CardBody className="pt-0">
            {/* File picker row */}
            <Can permission={Perm.LeadsImport}>
            <div className="flex flex-col sm:flex-row gap-3 items-stretch sm:items-end mb-5">
              <label className="flex-1 cursor-pointer">
                <div className="text-xs font-medium text-ink-700 mb-1.5">CSV file</div>
                <div className={cn(
                  "input-base flex items-center gap-2 cursor-pointer",
                  !filename && "text-ink-400",
                )}>
                  <Icon name="doc" size={14} />
                  <span className="flex-1 truncate">{filename || "Choose a .csv file..."}</span>
                  <span className="text-xs text-brand-600 font-medium">Browse</span>
                </div>
                <input
                  ref={fileRef} type="file" accept=".csv" className="hidden"
                  onChange={(e) => setFilename(e.target.files?.[0]?.name ?? "")}
                />
              </label>
              <Button
                onClick={runImport}
                loading={importing}
                disabled={!filename}
                size="lg"
                leftIcon={<Icon name="upload" size={16} />}
              >
                Import {filename ? `"${filename.length > 20 ? filename.slice(0, 20) + "…" : filename}"` : ""}
              </Button>
            </div>
            </Can>

            <div className="text-xs font-semibold text-ink-700 uppercase tracking-wider mb-2">Recent imports</div>
            {batchesLoading ? (
              <div className="space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-10" />)}</div>
            ) : !batches || batches.length === 0 ? (
              <EmptyState
                icon={<Icon name="clock" size={18} />}
                title={LISTS_MSG.noImportsTitle}
                description={LISTS_MSG.noImportsDesc}
              />
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>When</TH>
                    <TH>File</TH>
                    <TH numeric>
                      <span className="inline-flex items-center gap-1">Total
                        <InfoHint title="Total rows" side="left">
                          Every row in the file — the sum of imported, duplicates, DNC scrubbed, and errors. That's why Imported is usually lower.
                        </InfoHint>
                      </span>
                    </TH>
                    <TH numeric>Imported</TH>
                    <TH numeric>
                      <span className="inline-flex items-center gap-1">Duplicates
                        <InfoHint title="Duplicates" side="left">
                          Rows skipped because a lead with the same phone number already exists in the CRM.
                        </InfoHint>
                      </span>
                    </TH>
                    <TH numeric>
                      <span className="inline-flex items-center gap-1">DNC scrubbed
                        <InfoHint title="DNC scrubbed" side="left">
                          Rows dropped during import because the phone number is on a do-not-call (DNC) list and can't be legally contacted.
                        </InfoHint>
                      </span>
                    </TH>
                    <TH numeric>Errors</TH>
                  </TR>
                </THead>
                <TBody>
                  {batches.map((b) => (
                    <TR key={b.id}>
                      <TD className="text-xs text-ink-600 whitespace-nowrap tabular-nums">
                        {b.completedAt ? new Date(b.completedAt).toLocaleString() : <Badge tone="warning" variant="soft" dot>Running</Badge>}
                      </TD>
                      <TD className="font-mono text-xs text-ink-700 max-w-[220px] truncate">{b.fileName}</TD>
                      <TD numeric className="text-ink-700">{b.totalRows ?? 0}</TD>
                      <TD numeric><Badge tone="success" variant="soft">{b.imported ?? 0}</Badge></TD>
                      <TD numeric><Badge tone="warning" variant="soft">{b.duplicates ?? 0}</Badge></TD>
                      <TD numeric><Badge tone="danger" variant="soft">{b.dncScrubbed ?? 0}</Badge></TD>
                      <TD numeric>
                        {b.errors > 0
                          ? <Badge tone="danger" variant="soft">{b.errors}</Badge>
                          : <span className="text-ink-400">0</span>}
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </CardBody>
        </Card>
      )}

      <Modal
        open={open} onClose={() => setOpen(false)}
        title="New lead list" size="md"
        description="Create a new list — you can import leads into it next."
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="list-form" type="submit" loading={saving}>Create list</Button>
          </>
        }
      >
        <form id="list-form" onSubmit={createList} className="grid grid-cols-1 gap-3">
          <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Q1 ACA Florida" autoFocus />
        </form>
      </Modal>
    </>
  );
}
