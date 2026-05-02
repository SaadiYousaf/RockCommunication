import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  useDialLeadMutation,
  useDuplicateLeadsQuery,
  useSearchLeadsQuery,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader,
  EmptyState, Icon, Input, PageHeader, Skeleton, Stat, Tabs,
  Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

type Filters = { phone: string; email: string; name: string; state: string; stage: string };

const STAGES = ["", "New", "Fronted", "Verified", "JrClosed", "Closed", "Validated", "Funded", "Followup", "Winback", "Lost"];

const stageTone: Record<string, "brand" | "info" | "warning" | "success" | "danger" | "neutral"> = {
  New: "brand", Fronted: "info", Verified: "info", JrClosed: "warning",
  Closed: "warning", Validated: "success", Funded: "success",
  Followup: "neutral", Winback: "neutral", Lost: "danger",
};

function formatPhone(p?: string | null) {
  if (!p) return "";
  const d = p.replace(/\D/g, "");
  if (d.length === 10) return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
  return p;
}

function useDebounced<T>(value: T, delay = 350) {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return v;
}

export function LeadSearchPage() {
  const [tab, setTab] = useState<"search" | "duplicates">("search");
  const [filters, setFilters] = useState<Filters>({ phone: "", email: "", name: "", state: "", stage: "" });
  const debounced = useDebounced(filters);
  const hasQuery = useMemo(() =>
    Object.values(debounced).some((v) => v.trim().length > 0), [debounced]);

  const { data: results, isFetching: searching } = useSearchLeadsQuery(
    {
      phone: debounced.phone || undefined,
      email: debounced.email || undefined,
      name: debounced.name || undefined,
    },
    { skip: !hasQuery },
  );

  const filtered = useMemo(() => {
    if (!results) return [];
    return results.filter((r) => {
      if (debounced.state && r.state?.toLowerCase() !== debounced.state.toLowerCase()) return false;
      if (debounced.stage && String(r.stage) !== debounced.stage) return false;
      return true;
    });
  }, [results, debounced.state, debounced.stage]);

  const { data: duplicates, isLoading: dupLoading, refetch: refetchDups } = useDuplicateLeadsQuery();
  const [dialLead] = useDialLeadMutation();
  const toast = useToast();
  const navigate = useNavigate();

  function clearAll() {
    setFilters({ phone: "", email: "", name: "", state: "", stage: "" });
  }

  async function dialFromRow(leadId: string) {
    try {
      await dialLead({ leadId }).unwrap();
      toast.success("Calling…", "Watch the dock for status.");
      navigate(`/leads/${leadId}`);
    } catch (err: any) {
      toast.error("Couldn't dial", err?.data?.detail ?? "Try again.");
    }
  }

  const totalDupLeads = duplicates?.reduce((sum, g) => sum + g.leads.length, 0) ?? 0;

  return (
    <>
      <PageHeader
        title="Lead troubleshooting"
        description="Find any lead by phone, email, or name. Detect duplicates and clean up the database."
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
        <Stat label="Search hits" value={hasQuery ? filtered.length : "—"} icon={<Icon name="search" size={16} />} />
        <Stat label="Active filters" value={Object.values(debounced).filter(Boolean).length} />
        <Stat label="Duplicate groups" value={duplicates?.length ?? 0} />
        <Stat label="Total dup leads" value={totalDupLeads} />
      </div>

      <Tabs<typeof tab>
        items={[
          { value: "search", label: <span className="inline-flex items-center gap-1.5"><Icon name="search" size={14} /> Search</span> },
          { value: "duplicates", label: <span className="inline-flex items-center gap-1.5"><Icon name="flag" size={14} /> Duplicates</span>, count: duplicates?.length },
        ]}
        value={tab}
        onChange={setTab}
      />

      {tab === "search" && (
        <>
          <Card className="mb-4 mt-4">
            <CardBody>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                <Input
                  leftIcon={<Icon name="phone" size={16} />}
                  placeholder="Phone fragment (e.g. 5551234)"
                  value={filters.phone}
                  onChange={(e) => setFilters({ ...filters, phone: e.target.value })}
                />
                <Input
                  leftIcon={<Icon name="chat" size={16} />}
                  placeholder="Email fragment"
                  value={filters.email}
                  onChange={(e) => setFilters({ ...filters, email: e.target.value })}
                />
                <Input
                  leftIcon={<Icon name="users" size={16} />}
                  placeholder="Name fragment"
                  value={filters.name}
                  onChange={(e) => setFilters({ ...filters, name: e.target.value })}
                />
              </div>

              <div className="flex flex-wrap items-center gap-2 mt-3 text-xs">
                <span className="text-ink-500">Filter by:</span>
                <select
                  className="border border-ink-200 rounded px-2 py-1 text-xs bg-white"
                  value={filters.stage}
                  onChange={(e) => setFilters({ ...filters, stage: e.target.value })}
                >
                  {STAGES.map((s) => <option key={s} value={s}>{s || "Any stage"}</option>)}
                </select>
                <input
                  className="border border-ink-200 rounded px-2 py-1 text-xs bg-white w-24"
                  placeholder="State (e.g. TX)"
                  maxLength={2}
                  value={filters.state}
                  onChange={(e) => setFilters({ ...filters, state: e.target.value.toUpperCase() })}
                />
                {(hasQuery || filters.state || filters.stage) && (
                  <Button variant="ghost" size="sm" onClick={clearAll} leftIcon={<Icon name="x" size={12} />}>
                    Clear
                  </Button>
                )}
                <span className="ml-auto text-ink-500">
                  {searching ? "Searching…" : hasQuery ? `${filtered.length} match${filtered.length === 1 ? "" : "es"}` : "Type to search"}
                </span>
              </div>
            </CardBody>
          </Card>

          {!hasQuery ? (
            <Card><CardBody>
              <EmptyState
                icon={<Icon name="search" size={20} />}
                title="Start typing to search"
                description="Search across all leads by phone fragment, email, or partial name. Results appear as you type."
              />
            </CardBody></Card>
          ) : searching ? (
            <Card><CardBody>
              <div className="space-y-2">
                {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12" />)}
              </div>
            </CardBody></Card>
          ) : filtered.length === 0 ? (
            <Card><CardBody>
              <EmptyState
                icon={<Icon name="search" size={20} />}
                title="No leads match"
                description="Try a shorter fragment or remove a filter."
                action={<Button variant="ghost" size="sm" onClick={clearAll}>Clear filters</Button>}
              />
            </CardBody></Card>
          ) : (
            <Card>
              <CardHeader title={`Search results · ${filtered.length}`} subtitle="Click a row to open the lead. Use the dial button to call." />
              <CardBody className="pt-0 px-0">
                <Table className="border-0 shadow-none rounded-none">
                  <THead>
                    <TR>
                      <TH>Lead</TH>
                      <TH>Phone</TH>
                      <TH>Email</TH>
                      <TH>State</TH>
                      <TH>Stage</TH>
                      <TH>Disposition</TH>
                      <TH>Created</TH>
                      <TH className="text-right">Actions</TH>
                    </TR>
                  </THead>
                  <TBody>
                    {filtered.map((l) => (
                      <TR key={l.id}>
                        <TD>
                          <Link to={`/leads/${l.id}`} className="flex items-center gap-3 group">
                            <Avatar name={`${l.firstName} ${l.lastName}`} size={32} />
                            <span className="font-medium text-ink-900 group-hover:text-brand-700">
                              {l.firstName} {l.lastName}
                            </span>
                          </Link>
                        </TD>
                        <TD className="font-mono text-xs text-ink-700">{formatPhone(l.phoneNumber)}</TD>
                        <TD className="text-ink-600 text-sm">{l.email ?? <span className="text-ink-400">—</span>}</TD>
                        <TD className="text-ink-600">{l.state ?? <span className="text-ink-400">—</span>}</TD>
                        <TD>
                          <Badge tone={stageTone[String(l.stage)] ?? "neutral"} variant="soft" dot>
                            {String(l.stage)}
                          </Badge>
                        </TD>
                        <TD className="text-xs text-ink-600">{String(l.disposition)}</TD>
                        <TD className="text-xs text-ink-500">
                          {new Date(l.createdAt).toLocaleDateString()}
                        </TD>
                        <TD>
                          <div className="flex items-center justify-end gap-1">
                            <Button
                              variant="ghost" size="sm" leftIcon={<Icon name="phone" size={14} />}
                              onClick={(e) => { e.preventDefault(); dialFromRow(l.id); }}
                            >Dial</Button>
                            <Link to={`/leads/${l.id}`}>
                              <Button variant="ghost" size="sm" leftIcon={<Icon name="arrowRight" size={14} />}>
                                Open
                              </Button>
                            </Link>
                          </div>
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              </CardBody>
            </Card>
          )}
        </>
      )}

      {tab === "duplicates" && (
        <div className="mt-4 space-y-4">
          <Card>
            <CardHeader
              title="Duplicate phone numbers"
              subtitle="Two or more leads share the same phone. Reach out once, then archive the rest."
              action={
                <Button variant="ghost" size="sm" leftIcon={<Icon name="filter" size={14} />}
                  onClick={() => refetchDups()}>
                  Refresh
                </Button>
              }
            />
            <CardBody className="pt-0">
              {dupLoading ? (
                <div className="space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-24" />)}</div>
              ) : !duplicates || duplicates.length === 0 ? (
                <EmptyState
                  icon={<Icon name="flag" size={20} />}
                  title="No duplicates"
                  description="Your database is clean — no two leads share the same phone number."
                />
              ) : (
                <div className="space-y-3">
                  {duplicates.map((g) => (
                    <DuplicateGroup key={g.key} group={g} onDial={dialFromRow} />
                  ))}
                </div>
              )}
            </CardBody>
          </Card>
        </div>
      )}
    </>
  );
}

function DuplicateGroup({
  group, onDial,
}: {
  group: { key: string; leads: any[] };
  onDial: (id: string) => void;
}) {
  const phone = group.key.replace(/^phone:/, "");
  return (
    <div className="border hairline rounded-lg overflow-hidden">
      <div className="flex items-center justify-between bg-ink-50/60 px-4 py-2 border-b hairline">
        <div className="flex items-center gap-2">
          <Icon name="phone" size={14} className="text-ink-500" />
          <code className="font-mono text-sm text-ink-800">{formatPhone(phone)}</code>
          <Badge tone="warning" variant="soft">{group.leads.length} duplicates</Badge>
        </div>
      </div>
      <ul className="divide-y hairline">
        {group.leads.map((l, idx) => (
          <li key={l.id} className="flex items-center gap-3 px-4 py-2 hover:bg-ink-50/50">
            <Avatar name={`${l.firstName} ${l.lastName}`} size={28} />
            <div className="flex-1 min-w-0">
              <Link to={`/leads/${l.id}`} className="font-medium text-ink-900 hover:text-brand-700">
                {l.firstName} {l.lastName}
              </Link>
              <div className="text-xs text-ink-500">
                Created {new Date(l.createdAt).toLocaleDateString()}
                {l.email && <> · {l.email}</>}
                {l.state && <> · {l.state}</>}
                {idx === 0 && <Badge tone="success" variant="soft" className="ml-2">Primary</Badge>}
              </div>
            </div>
            <Badge tone={stageTone[String(l.stage)] ?? "neutral"} variant="soft">
              {String(l.stage)}
            </Badge>
            <Button variant="ghost" size="sm" leftIcon={<Icon name="phone" size={14} />}
              onClick={() => onDial(l.id)}>
              Dial
            </Button>
            <Link to={`/leads/${l.id}`}>
              <Button variant="ghost" size="sm" leftIcon={<Icon name="arrowRight" size={14} />}>
                Open
              </Button>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
