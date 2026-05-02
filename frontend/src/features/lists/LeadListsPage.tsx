import { useMemo, useRef, useState } from "react";
import {
  useImportLeadsCsvMutation, useLeadListsQuery, useListImportBatchesQuery, useUpsertLeadListMutation,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast, cn,
} from "../../shared/ui";

export function LeadListsPage() {
  const { data: lists, isLoading } = useLeadListsQuery();
  const [upsert, { isLoading: saving }] = useUpsertLeadListMutation();
  const [importCsv, { isLoading: importing }] = useImportLeadsCsvMutation();
  const toast = useToast();

  const [search, setSearch] = useState("");
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
      toast.success("List created", name);
      setName(""); setOpen(false);
    } catch (err: any) {
      toast.error("Couldn't create list", err?.data?.detail ?? "Try again.");
    }
  }

  async function toggle(l: any) {
    try {
      await upsert({ id: l.id, name: l.name, isActive: !l.isActive }).unwrap();
      toast.success(l.isActive ? "List disabled" : "List enabled");
    } catch (err: any) {
      toast.error("Couldn't update", err?.data?.detail ?? "Try again.");
    }
  }

  async function runImport() {
    if (!activeListId) return;
    const file = fileRef.current?.files?.[0];
    if (!file) {
      toast.warning("No file selected", "Pick a CSV first.");
      return;
    }
    try {
      const result: any = await importCsv({ listId: activeListId, file }).unwrap();
      toast.success("Import finished",
        `${result?.imported ?? 0} imported · ${result?.duplicates ?? 0} dups · ${result?.dncScrubbed ?? 0} scrubbed`);
      if (fileRef.current) fileRef.current.value = "";
      setFilename("");
    } catch (err: any) {
      toast.error("Import failed", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Lead lists"
        description="Group leads into named lists, import via CSV (DNC-scrubbed automatically), and run cadences against them."
        actions={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New list</Button>}
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-6">
        <SmallTile label="Total lists"  value={stats.total}      icon="inbox"     tone="bg-brand-50 text-brand-600" />
        <SmallTile label="Active"       value={stats.active}     icon="check"     tone="bg-emerald-50 text-emerald-600" />
        <SmallTile label="Total leads"  value={stats.totalLeads} icon="list"      tone="bg-violet-50 text-violet-600" />
      </div>

      <Card className="mb-4">
        <CardBody>
          <Input
            leftIcon={<Icon name="search" size={16} />}
            placeholder="Search lists by name..."
            value={search} onChange={(e) => setSearch(e.target.value)}
          />
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="inbox" size={20} />}
            title={search ? "No lists match" : "No lead lists yet"}
            description={search ? "Try a different search." : "Create a list to organize leads and import contacts in bulk."}
            action={!search ? <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New list</Button> : undefined}
          />
        </CardBody></Card>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>Name</TH>
              <TH>Leads</TH>
              <TH>Status</TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {filtered.map((l) => {
              const isActive = activeListId === l.id;
              return (
                <TR key={l.id} className={cn(isActive && "bg-brand-50/40")}>
                  <TD>
                    <div className="flex items-center gap-3">
                      <div className="h-9 w-9 rounded-lg bg-brand-50 text-brand-600 grid place-items-center">
                        <Icon name="inbox" size={16} />
                      </div>
                      <div className="font-medium text-ink-900">{l.name}</div>
                      {isActive && <Badge tone="brand" variant="soft">Selected</Badge>}
                    </div>
                  </TD>
                  <TD className="font-semibold text-ink-900">{l.leadCount?.toLocaleString() ?? 0}</TD>
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
                      <Button variant="ghost" size="sm" onClick={() => toggle(l)}>
                        {l.isActive ? "Disable" : "Enable"}
                      </Button>
                    </div>
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
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
              <span>
                Columns:{" "}
                <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">
                  firstname,lastname,phone,email,state,postal,source,jornaya
                </code>{" "}
                — DNC numbers are scrubbed automatically.
              </span>
            }
          />
          <CardBody className="pt-0">
            {/* File picker row */}
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
                leftIcon={<Icon name="arrowRight" size={16} />}
              >
                Import {filename ? `"${filename.length > 20 ? filename.slice(0, 20) + "…" : filename}"` : ""}
              </Button>
            </div>

            <div className="text-xs font-semibold text-ink-700 uppercase tracking-wider mb-2">Recent imports</div>
            {batchesLoading ? (
              <div className="space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-10" />)}</div>
            ) : !batches || batches.length === 0 ? (
              <EmptyState
                icon={<Icon name="clock" size={18} />}
                title="No imports yet"
                description="Once you upload a CSV, the run summary will appear here."
              />
            ) : (
              <div className="overflow-x-auto rounded-lg border hairline">
                <table className="w-full text-sm">
                  <thead className="bg-ink-50/60 text-ink-600 text-xs uppercase tracking-wide">
                    <tr>
                      <th className="text-left font-medium px-4 py-2.5">When</th>
                      <th className="text-left font-medium px-4 py-2.5">File</th>
                      <th className="text-left font-medium px-4 py-2.5">Total</th>
                      <th className="text-left font-medium px-4 py-2.5">Imported</th>
                      <th className="text-left font-medium px-4 py-2.5">Duplicates</th>
                      <th className="text-left font-medium px-4 py-2.5">DNC scrubbed</th>
                      <th className="text-left font-medium px-4 py-2.5">Errors</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-ink-100">
                    {batches.map((b: any) => (
                      <tr key={b.id} className="hover:bg-ink-50/60">
                        <td className="px-4 py-2.5 text-xs text-ink-600">
                          {b.completedAt ? new Date(b.completedAt).toLocaleString() : <Badge tone="warning" variant="soft" dot>Running</Badge>}
                        </td>
                        <td className="px-4 py-2.5 font-mono text-xs text-ink-700">{b.fileName}</td>
                        <td className="px-4 py-2.5 text-ink-700">{b.totalRows ?? 0}</td>
                        <td className="px-4 py-2.5"><Badge tone="success" variant="soft">{b.imported ?? 0}</Badge></td>
                        <td className="px-4 py-2.5"><Badge tone="warning" variant="soft">{b.duplicates ?? 0}</Badge></td>
                        <td className="px-4 py-2.5"><Badge tone="danger" variant="soft">{b.dncScrubbed ?? 0}</Badge></td>
                        <td className="px-4 py-2.5">
                          {b.errors > 0
                            ? <Badge tone="danger" variant="soft">{b.errors}</Badge>
                            : <span className="text-ink-400">0</span>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
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

function SmallTile({ label, value, icon, tone }: { label: string; value: number; icon: any; tone: string }) {
  return (
    <div className="surface p-4 flex items-center gap-3">
      <div className={`h-10 w-10 rounded-lg grid place-items-center ${tone}`}>
        <Icon name={icon} size={18} />
      </div>
      <div>
        <div className="text-xs font-medium text-ink-500 uppercase tracking-wide">{label}</div>
        <div className="text-xl font-semibold text-ink-900">{value.toLocaleString()}</div>
      </div>
    </div>
  );
}
