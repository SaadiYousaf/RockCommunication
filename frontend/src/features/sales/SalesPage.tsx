import type { ButtonVariant } from "../../shared/ui";
import { getErrorDetail } from "../../shared/api/apiError";
import { formatPhone, formatUsd } from "../../shared/lib/format";
import { useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useCarriersQuery, useListLeadsQuery, useRecordSaleMutation,
  useValidateSaleMutation, useFundSaleMutation, useUploadSaleRecordingMutation,
  useListSalesQuery, useListUsersQuery, type SalesQuery,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, ErrorState, Icon, InfoHint, Input, PageHeader,
  SearchInput, Select, Skeleton, Stat, Table, Tabs, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { Can, Perm } from "../../shared/auth/permissions";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { MESSAGES } from "../../shared/constants/messages";
import { stageLabel } from "../../shared/constants/leadStage";
import { SALES_MSG } from "./messages";

const statusTone: Record<string, "success" | "info" | "warning" | "neutral" | "brand"> = {
  funded: "success", validated: "info", pending: "warning", internal: "brand",
};

/** What users SEE for a sale status — the API sends the raw lowercase code. */
const STATUS_LABEL: Record<string, string> = {
  funded: "Funded", validated: "Validated", pending: "Pending",
  internal: "Internal", rejected: "Rejected",
};
const statusLabel = (s: string | null | undefined): string =>
  s ? (STATUS_LABEL[s] ?? s) : "—";

type ViewTab = "record" | "list";

export function SalesPage() {
  const { data: carriers } = useCarriersQuery();
  const { data: closedResult, isLoading } = useListLeadsQuery({ stage: "Closed" });
  const closedLeads = closedResult?.items;
  const [recordSale, { isLoading: recording }] = useRecordSaleMutation();
  const [validateSale, { isLoading: validating }] = useValidateSaleMutation();
  const [fundSale, { isLoading: funding }] = useFundSaleMutation();
  const [uploadRecording, { isLoading: uploadingRec }] = useUploadSaleRecordingMutation();
  const toast = useToast();
  const confirm = useConfirm();

  // Sales to pick from for the Validate / Fund actions (instead of pasting a Sale ID).
  // SuperAdmin listing needs an explicit agency, so skip for them — the field falls back to input.
  const isSuperAdmin = useSelector((s: RootState) => s.auth.user?.roles?.includes("SuperAdmin") ?? false);
  const { data: pendingSales } = useListSalesQuery({ status: "pending", take: 200 }, { skip: isSuperAdmin });
  const { data: validatedSales } = useListSalesQuery({ status: "validated", take: 200 }, { skip: isSuperAdmin });
  const saleOpt = (s: { id: string; saleNumber: number; leadName: string; monthlyPremium: number }) =>
    ({ id: s.id, label: `#${s.saleNumber} · ${s.leadName} · $${s.monthlyPremium.toFixed(0)}/mo` });

  const [leadId, setLeadId] = useState("");
  const [carrier, setCarrier] = useState("AETNA");
  const [premium, setPremium] = useState(150);
  const [policyNumber, setPolicyNumber] = useState("");
  // Bank details are validated server-side by Lyons, which derives the banking code.
  const [routingNumber, setRoutingNumber] = useState("");
  const [accountNumber, setAccountNumber] = useState("");
  const [accountType, setAccountType] = useState("checking");
  const [recordingFile, setRecordingFile] = useState<File | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!routingNumber.trim() || !accountNumber.trim()) {
      toast.error(SALES_MSG.bankDetailsRequiredTitle, SALES_MSG.bankDetailsRequiredBody);
      return;
    }
    try {
      let recordingKey: string | undefined;
      if (recordingFile) {
        const up = await uploadRecording(recordingFile).unwrap();
        recordingKey = up.key;
      }
      const sale = await recordSale({
        leadId, carrier, monthlyPremium: premium,
        policyNumber: policyNumber || undefined,
        routingNumber, accountNumber, accountType,
        recordingKey,
      }).unwrap();
      const bankMsg = sale.bankingCode === 198
        ? SALES_MSG.bankFlagged198
        : SALES_MSG.bankCleared103;
      toast.success(SALES_MSG.saleRecordedTitle, SALES_MSG.saleRecordedBody(premium, carrier, bankMsg));
      setLeadId(""); setPolicyNumber(""); setRecordingFile(null);
      setRoutingNumber(""); setAccountNumber(""); setAccountType("checking");
    } catch (err: unknown) {
      const detail = getErrorDetail(err) ?? MESSAGES.tryAgain;
      // Lyons flagged the account (198) and no recording was attached — keep the form so they can add one.
      if (/recording/i.test(detail)) {
        toast.warning(SALES_MSG.verificationRecordingNeededTitle, detail);
      } else {
        toast.error(SALES_MSG.recordSaleFailed, detail);
      }
    }
  }

  // Arriving with a filter in the URL (e.g. from the wallboard's "Sales closed today") means the
  // caller wants the LIST, not the record form — landing on the form would look broken.
  const [pageParams] = useSearchParams();
  const [tab, setTab] = useState<ViewTab>(
    pageParams.has("from") || pageParams.has("status") ? "list" : "record",
  );

  return (
    <>
      <PageHeader
        eyebrow="Revenue"
        title="Sales"
        description="Record new sales, validate them, and fund them through to commission."
        tabs={
          <Tabs<ViewTab>
            value={tab}
            onChange={setTab}
            items={[
              { value: "record", label: "Record & validate", icon: <Icon name="edit" size={14} /> },
              { value: "list", label: "All sales", icon: <Icon name="list" size={14} /> },
            ]}
          />
        }
      />

      {/* RecordView is called inline (not <RecordView/>) on purpose: it's a nested render helper,
          so rendering it as a component would give it a new identity every keystroke and remount it,
          stealing focus from the inputs. Inlining keeps its fields in this component's tree. */}
      {tab === "list" ? <SalesList /> : RecordView()}
    </>
  );

  function RecordView() {
    return (
      <>
      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4 mb-6">
        {/* Record sale */}
        <Can permission={Perm.SalesRecord}>
        <Card className="xl:col-span-2">
          <CardHeader title="Record a sale" subtitle="Capture a new closed deal." bordered />
          <CardBody>
            <form onSubmit={submit} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Select
                id="record-sale-lead-id"
                label="Lead" required
                value={leadId} onChange={(e) => setLeadId(e.target.value)}
                hint="Pick a recently closed lead"
              >
                <option value="">Select a closed lead…</option>
                {(closedLeads ?? []).map((l) => (
                  <option key={l.id} value={l.id}>{l.firstName} {l.lastName} · {l.phoneNumber}</option>
                ))}
              </Select>
              <Select label="Carrier" value={carrier} onChange={(e) => setCarrier(e.target.value)}>
                {carriers?.map((c) => <option key={c} value={c}>{c}</option>)}
              </Select>
              <Input
                label="Monthly premium" type="number" min={1} step="0.01"
                leftIcon={<Icon name="dollar" size={14} />}
                value={premium} onChange={(e) => setPremium(parseFloat(e.target.value) || 0)}
              />
              <Input
                label="Policy #"
                placeholder="e.g. POL-123456"
                value={policyNumber} onChange={(e) => setPolicyNumber(e.target.value)}
              />

              <div className="sm:col-span-2 rounded-lg border border-ink-200 bg-ink-50/50 p-3 space-y-3">
                <div className="flex items-center gap-2 text-sm font-medium text-ink-800">
                  <Icon name="shield" size={14} /> Bank account — validated by Lyons on submit
                  <InfoHint title="Bank validation (Lyons)" side="bottom">
                    On submit, Lyons validates the bank account and returns a code — 103 = cleared (the
                    sale proceeds); 198 = flagged (attach a verification recording to proceed); any other
                    code blocks submission.
                  </InfoHint>
                  <Badge tone="neutral" variant="soft">typing only</Badge>
                </div>
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <Input
                    label="Routing #" required secure inputMode="numeric"
                    placeholder="9-digit ABA"
                    value={routingNumber} onChange={(e) => setRoutingNumber(e.target.value)}
                  />
                  <Input
                    label="Account #" required secure inputMode="numeric"
                    placeholder="Account number"
                    value={accountNumber} onChange={(e) => setAccountNumber(e.target.value)}
                  />
                  <Select label="Account type" value={accountType} onChange={(e) => setAccountType(e.target.value)}>
                    <option value="checking">Checking</option>
                    <option value="savings">Savings</option>
                  </Select>
                </div>
                <p className="text-xs text-ink-500">
                  Lyons returns the banking code automatically: <span className="font-medium">103</span> clears the
                  sale; <span className="font-medium">198</span> requires the verification recording below; anything
                  else blocks submission.
                </p>
                <div>
                  <div className="flex items-center gap-2 text-xs font-medium text-ink-700 mb-1.5">
                    <Icon name="phone" size={13} /> Verification recording (attach if Lyons flags the account · 198)
                  </div>
                  <input
                    type="file"
                    accept="audio/*,video/mp4"
                    onChange={(e) => setRecordingFile(e.target.files?.[0] ?? null)}
                    className="block w-full text-sm text-ink-700 file:mr-3 file:rounded-md file:border-0 file:bg-ink-900 file:px-3 file:py-1.5 file:text-white file:text-xs hover:file:bg-ink-800"
                  />
                  {recordingFile && (
                    <div className="mt-2 text-xs text-ink-600">
                      Selected: <span className="font-medium">{recordingFile.name}</span>{" "}
                      ({(recordingFile.size / (1024 * 1024)).toFixed(1)} MB)
                    </div>
                  )}
                </div>
              </div>

              <div className="sm:col-span-2 flex justify-end">
                <Button
                  type="submit"
                  loading={recording || uploadingRec}
                  leftIcon={<Icon name="check" size={16} />}
                >
                  Record sale
                </Button>
              </div>
            </form>
          </CardBody>
        </Card>
        </Can>

        {/* Validate / Fund */}
        <Card>
          <CardHeader title="Validate & fund" subtitle="Move sales through QA and funding." bordered />
          <CardBody>
            <Can permission={Perm.SalesValidate}>
              <SaleActionForm
                label="Validate" placeholder="Sale ID to validate" tone="success"
                options={(pendingSales?.items ?? []).map(saleOpt)}
                actions={[
                  { label: "Approve", variant: "success", onClick: (id) => doValidate(id, true) },
                  { label: "Reject",  variant: "danger",  onClick: (id) => doValidate(id, false) },
                ]}
                loading={validating}
              />
            </Can>
            <div className="my-4 border-t hairline" />
            <Can permission={Perm.SalesFund}>
              <SaleActionForm
                label="Fund" placeholder="Sale ID to fund" tone="info"
                options={(validatedSales?.items ?? []).map(saleOpt)}
                actions={[{ label: "Fund", variant: "primary", onClick: (id) => doFund(id) }]}
                loading={funding}
              />
            </Can>
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader
          title="Recent closed leads"
          subtitle="Eligible for sale recording"
          bordered
        />
        <CardBody className="pt-0 px-0">
          {isLoading ? (
            <div className="px-5 pb-5 space-y-3">
              {[0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)}
            </div>
          ) : !closedLeads || closedLeads.length === 0 ? (
            <div className="px-5 pb-5">
              <EmptyState
                icon={<Icon name="briefcase" size={20} />}
                title={SALES_MSG.noClosedLeadsTitle}
                description={SALES_MSG.noClosedLeadsBody}
              />
            </div>
          ) : (
            <Table className="border-0 shadow-none rounded-none">
              <THead>
                <TR>
                  <TH>Lead</TH>
                  <TH>Phone</TH>
                  <TH>Stage</TH>
                  <TH className="text-right">Actions</TH>
                </TR>
              </THead>
              <TBody>
                {closedLeads.map((l) => {
                  const name = `${l.firstName} ${l.lastName}`;
                  return (
                    <TR key={l.id}>
                      <TD>
                        <div className="flex items-center gap-3 min-w-0">
                          <Avatar name={name} size={32} />
                          <div className="min-w-0">
                            <div className="font-medium text-ink-900 truncate">{name}</div>
                            <div className="text-xs text-ink-500 font-mono tabular-nums">{l.id.slice(0, 8)}…</div>
                          </div>
                        </div>
                      </TD>
                      <TD className="text-ink-600 tabular-nums whitespace-nowrap">{formatPhone(l.phoneNumber)}</TD>
                      <TD><Badge tone="warning" variant="soft" dot>{stageLabel(l.stage)}</Badge></TD>
                      <TD>
                        <div className="flex justify-end">
                          <Button variant="outline" size="sm" leftIcon={<Icon name="arrowRight" size={13} className="-rotate-90" />}
                            onClick={() => {
                              setLeadId(l.id);
                              // The form is above this list, so give clear feedback: jump to it, focus
                              // the Lead ID field, and toast — otherwise the fill looks like nothing happened.
                              const el = document.getElementById("record-sale-lead-id");
                              el?.scrollIntoView({ behavior: "smooth", block: "center" });
                              setTimeout(() => (el as HTMLInputElement | null)?.focus({ preventScroll: true }), 300);
                              toast.success(SALES_MSG.leadSelectedTitle, SALES_MSG.leadSelectedBody(name));
                            }}>
                            Use this lead
                          </Button>
                        </div>
                      </TD>
                    </TR>
                  );
                })}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>
      </>
    );
  }

  async function doValidate(id: string, approve: boolean) {
    if (!approve) {
      const ok = await confirm({
        title: SALES_MSG.rejectSaleConfirmTitle,
        description: SALES_MSG.rejectSaleConfirmBody,
        confirmLabel: SALES_MSG.rejectSaleConfirmLabel,
        danger: true,
      });
      if (!ok) return;
    }
    try {
      await validateSale({ id, approve }).unwrap();
      toast.success(approve ? SALES_MSG.saleApproved : SALES_MSG.saleRejected);
    } catch (err: unknown) {
      toast.error(SALES_MSG.validateSaleFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function doFund(id: string) {
    try {
      await fundSale(id).unwrap();
      toast.success(SALES_MSG.saleFunded);
    } catch (err: unknown) {
      toast.error(SALES_MSG.fundSaleFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }
}

function SalesList() {
  const navigate = useNavigate();
  // Seed from the URL so a link that counted a subset (e.g. the wallboard's "Sales closed today")
  // opens exactly those rows. Without this the tile said 28 and the page showed every sale ever.
  const [searchParams] = useSearchParams();
  const [filters, setFilters] = useState<SalesQuery>(() => ({
    skip: 0, take: 50, sort: "soldAt-desc",
    from: searchParams.get("from") ?? undefined,
    to: searchParams.get("to") ?? undefined,
    status: searchParams.get("status") ?? undefined,
  }));
  const { data, isLoading, isFetching, isError, error, refetch } = useListSalesQuery(filters);
  const { data: users } = useListUsersQuery();
  const { data: carriers } = useCarriersQuery();
  const toast = useToast();

  // Free-text search over the rows already fetched for this page (the server-side filters,
  // sorting, paging and the stats above are untouched).
  const [search, setSearch] = useState("");
  const filteredRows = useMemo(() => {
    const all = data?.items ?? [];
    const q = search.trim().toLowerCase();
    if (!q) return all;
    return all.filter((s) =>
      [s.leadName, s.leadPhone, s.carrier, s.policyNumber, s.closerName, s.planApproved]
        .some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [data?.items, search]);

  // The server only fills agencyName when the list actually spans agencies, so the column shows
  // itself exactly when it carries information and stays hidden for tenant-scoped users.
  const showAgency = useMemo(
    () => (data?.items ?? []).some((s) => !!s.agencyName),
    [data?.items],
  );

  const sel = useRowSelection(filteredRows.map((s) => s.id));

  function exportSelected() {
    const chosen = filteredRows.filter((s) => sel.isSelected(s.id));
    exportRowsToCsv(chosen, [
      { header: "Customer", value: (s) => s.leadName },
      { header: "Carrier", value: (s) => s.carrier },
      { header: "Plan", value: (s) => s.planApproved ?? "" },
      { header: "Premium", value: (s) => s.monthlyPremium },
      { header: "Commission", value: (s) => s.commissionEarned ?? "" },
      { header: "Status", value: (s) => statusLabel(s.status) },
    ], `sales-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(SALES_MSG.exportReadyTitle, SALES_MSG.rowsDownloaded(chosen.length));
  }

  const total = data?.total ?? 0;
  const skip = filters.skip ?? 0;
  const take = filters.take ?? 50;

  const pageInfo = useMemo(() => {
    const start = total === 0 ? 0 : skip + 1;
    const end = Math.min(skip + take, total);
    return `${start}–${end} of ${total}`;
  }, [skip, take, total]);

  function update<K extends keyof SalesQuery>(key: K, value: SalesQuery[K]) {
    setFilters((f) => ({ ...f, [key]: value, skip: 0 }));
  }

  return (
    <>
      <div className="grid grid-cols-2 md:grid-cols-5 gap-4 mb-5">
        <Stat label="Total sales"   value={total}                                                                                   icon={<Icon name="briefcase" size={16} />} tone="brand"
              onClick={() => update("status", undefined)} hint="All sales matching the filters. Click to clear the status filter." />
        <Stat label="Total premium" value={formatUsd(data?.totalPremium ?? 0)}  icon={<Icon name="dollar" size={16} />}    tone="success"
              hint="Combined monthly premium across every sale matching the current filters." />
        <Stat label="Funded"        value={data?.fundedCount ?? 0}                                                                  icon={<Icon name="success" size={16} />}   tone="success"
              onClick={() => update("status", "Funded")} hint="First premium draft cleared. Click to show only these." />
        <Stat label="Validated"     value={data?.validatedCount ?? 0}                                                               icon={<Icon name="shield" size={16} />}    tone="brand"
              onClick={() => update("status", "Validated")} hint="Passed QA validation, awaiting funding. Click to filter." />
        <Stat label="Pending"       value={data?.pendingCount ?? 0}                                                                 icon={<Icon name="clock" size={16} />}     tone="warning"
              onClick={() => update("status", "Pending")} hint="Recorded, not yet validated. Click to filter." />
      </div>

      <Card className="mb-4">
        <CardBody className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-3">
          <SearchInput
            value={search} onChange={setSearch}
            placeholder={SALES_MSG.salesSearchPlaceholder}
            containerClassName="md:col-span-3 lg:col-span-2"
          />
          <Select
            value={filters.closerUserId ?? ""}
            onChange={(e) => update("closerUserId", e.target.value || undefined)}
          >
            <option value="">All closers</option>
            {users?.map((u) => <option key={u.id} value={u.id}>{u.userName}</option>)}
          </Select>
          <Select
            value={filters.carrier ?? ""}
            onChange={(e) => update("carrier", e.target.value || undefined)}
          >
            <option value="">Any carrier</option>
            {carriers?.map((c) => <option key={c} value={c}>{c}</option>)}
          </Select>
          <Select
            value={filters.status ?? ""}
            onChange={(e) => update("status", e.target.value || undefined)}
          >
            <option value="">Any status</option>
            <option value="funded">Funded</option>
            <option value="validated">Validated</option>
            <option value="pending">Pending</option>
            <option value="internal">Internal</option>
          </Select>
          <Input type="date" leftIcon={<Icon name="calendar" size={14} />}
            title="Sold on or after this date" aria-label="Sold from date"
            value={filters.from?.slice(0, 10) ?? ""}
            onChange={(e) => update("from", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
          />
          <Input type="date" leftIcon={<Icon name="calendar" size={14} />}
            title="Sold on or before this date" aria-label="Sold to date"
            value={filters.to?.slice(0, 10) ?? ""}
            onChange={(e) => update("to", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
          />
          <Select
            value={filters.sort ?? "soldAt-desc"}
            onChange={(e) => update("sort", e.target.value)}
          >
            <option value="soldAt-desc">Newest first</option>
            <option value="soldAt-asc">Oldest first</option>
            <option value="premium-desc">Premium high → low</option>
            <option value="premium-asc">Premium low → high</option>
            <option value="carrier-asc">Carrier A–Z</option>
          </Select>
          <div className="md:col-span-3 lg:col-span-6 flex items-center justify-between text-xs text-ink-500 pt-1">
            <div className="tabular-nums">{pageInfo} {isFetching && <span className="ml-2 text-ink-400">refreshing…</span>}</div>
            <Button variant="ghost" size="sm" leftIcon={<Icon name="refresh" size={13} />}
              onClick={() => setFilters({ skip: 0, take: 50, sort: "soldAt-desc" })}>
              Reset filters
            </Button>
          </div>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0,1,2,3,4].map((i) => <Skeleton key={i} className="h-12 my-2" />)}</CardBody></Card>
      ) : isError ? (
        // A failed request must NEVER fall through to "no sales found" — that reads as data loss.
        <Card><CardBody>
          <ErrorState error={error} resource={SALES_MSG.resourceName} onRetry={refetch} />
        </CardBody></Card>
      ) : !data || data.items.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="briefcase" size={20} />}
            title={SALES_MSG.noSalesMatchTitle}
            description={SALES_MSG.noSalesMatchBody}
          />
        </CardBody></Card>
      ) : filteredRows.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="search" size={20} />}
            title={SALES_MSG.noSalesSearchMatchTitle}
            description={SALES_MSG.noSalesSearchMatchBody}
          />
        </CardBody></Card>
      ) : (
        <>
          <Table>
            <THead>
              <TR>
                <TH className="w-10"><Checkbox aria-label="Select all sales" {...sel.allCheckboxProps} /></TH>
                <TH>Sold</TH>
                <TH>Lead</TH>
                <TH>
                  <span className="inline-flex items-center gap-1">
                    Closer
                    <InfoHint title="Closer" side="top">
                      The agent who closed this deal with the customer and recorded the sale.
                    </InfoHint>
                  </span>
                </TH>
                <TH>Carrier</TH>
                <TH>Premium</TH>
                <TH>
                  <span className="inline-flex items-center gap-1">
                    Status
                    <InfoHint title="Sale status" side="top">
                      A sale's lifecycle: pending (recorded) → validated (submitted and approved by the
                      carrier) → funded (first premium draft cleared).
                    </InfoHint>
                  </span>
                </TH>
                <TH>Policy #</TH>
                {/* Only meaningful when the list spans agencies (an unscoped SuperAdmin). */}
                {showAgency && <TH>Agency</TH>}
              </TR>
            </THead>
            <TBody>
              {filteredRows.map((s) => (
                <TR key={s.id} className={sel.isSelected(s.id) ? "cursor-pointer bg-brand-50/40" : "cursor-pointer"} onClick={() => navigate(`/sales/${s.id}`)}>
                  <TD onClick={(e) => e.stopPropagation()}>
                    <Checkbox aria-label={`Select ${s.leadName}`} {...sel.checkboxProps(s.id)} />
                  </TD>
                  <TD className="text-ink-600 whitespace-nowrap text-xs tabular-nums">
                    {/* Keyboard-reachable link to the sale (the row onClick is mouse-only). */}
                    <Link to={`/sales/${s.id}`} onClick={(e) => e.stopPropagation()} className="hover:underline">
                      {new Date(s.soldAt).toLocaleString()}
                    </Link>
                  </TD>
                  <TD>
                    {/* Clicking the name jumps to the LEAD; clicking anywhere else opens the SALE. */}
                    <Link to={`/leads/${s.leadId}`} onClick={(e) => e.stopPropagation()} className="block min-w-0 hover:underline">
                      <div className="font-medium text-ink-900 truncate">{s.leadName}</div>
                      <div className="text-xs text-ink-500 tabular-nums whitespace-nowrap">{formatPhone(s.leadPhone)}</div>
                    </Link>
                  </TD>
                  <TD>
                    {s.closerName ? (
                      <div className="flex items-center gap-2 min-w-0">
                        <Avatar name={s.closerName} size={24} />
                        <span className="text-ink-700 text-sm truncate">{s.closerName}</span>
                      </div>
                    ) : <span className="text-ink-400">—</span>}
                  </TD>
                  <TD className="text-ink-700">{s.carrier}</TD>
                  <TD className="text-ink-900 font-medium tabular-nums whitespace-nowrap">
                    {formatUsd(s.monthlyPremium)}/mo
                    <div className="text-xs text-ink-500 tabular-nums">{formatUsd(s.annualPremium)}/yr</div>
                  </TD>
                  <TD>
                    <span className="inline-flex items-center gap-1">
                      <Badge tone={statusTone[s.status] ?? "neutral"} variant="soft" dot>
                        {statusLabel(s.status)}{s.isInternalSale ? " · internal" : ""}
                      </Badge>
                      {s.isInternalSale && (
                        <InfoHint title="Internal sale" side="left">
                          Closed by/for an in-house agent rather than an external lead — flagged so it can
                          be handled differently in commission and reporting.
                        </InfoHint>
                      )}
                    </span>
                  </TD>
                  <TD className="text-ink-500 font-mono text-xs tabular-nums whitespace-nowrap">{s.policyNumber ?? "—"}</TD>
                  {showAgency && (
                    <TD className="text-ink-600 text-sm whitespace-nowrap">{s.agencyName ?? "—"}</TD>
                  )}
                </TR>
              ))}
            </TBody>
          </Table>
          <BulkActionBar
            count={sel.selectedCount} itemNoun="sale" onClear={sel.clear}
            actions={[{ key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected }]}
          />

          <div className="flex items-center justify-between mt-4">
            <div className="text-xs text-ink-500 tabular-nums">{pageInfo}</div>
            <div className="flex gap-1.5">
              <Button variant="outline" size="sm" disabled={skip === 0}
                title={skip === 0 ? "You're on the first page" : "Go to the previous page"}
                leftIcon={<Icon name="chevronLeft" size={13} />}
                onClick={() => setFilters((f) => ({ ...f, skip: Math.max(0, (f.skip ?? 0) - take) }))}>
                Prev
              </Button>
              <Button variant="outline" size="sm" disabled={skip + take >= total}
                title={skip + take >= total ? "No more pages" : "Go to the next page"}
                rightIcon={<Icon name="chevronRight" size={13} />}
                onClick={() => setFilters((f) => ({ ...f, skip: (f.skip ?? 0) + take }))}>
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </>
  );
}

function SaleActionForm({
  label, placeholder, actions, loading, options,
}: {
  label: string;
  placeholder: string;
  tone: "success" | "info";
  actions: { label: string; variant: ButtonVariant; onClick: (id: string) => void }[];
  loading: boolean;
  /** When provided, pick the sale from a dropdown instead of pasting an ID. */
  options?: { id: string; label: string }[];
}) {
  const [id, setId] = useState("");
  return (
    <div>
      {options && options.length > 0 ? (
        <Select label={label} value={id} onChange={(e) => setId(e.target.value)}>
          <option value="">Select a sale…</option>
          {options.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
        </Select>
      ) : (
        <Input label={label} placeholder={placeholder} value={id} onChange={(e) => setId(e.target.value)} />
      )}
      <div className="flex gap-2 mt-2 justify-end">
        {actions.map((a) => (
          <Button
            key={a.label} variant={a.variant} size="sm" loading={loading}
            disabled={!id} onClick={() => a.onClick(id)}
          >{a.label}</Button>
        ))}
      </div>
    </div>
  );
}
