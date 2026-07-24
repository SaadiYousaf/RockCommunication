import type { ButtonVariant } from "../../shared/ui";
import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import {
  useCarriersQuery, useListLeadsQuery, useRecordSaleMutation,
  useValidateSaleMutation, useFundSaleMutation, useUploadSaleRecordingMutation,
  useListSalesQuery, useListUsersQuery, type SalesQuery,
} from "../../shared/api/baseApi";
import {
  Avatar, Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, PageHeader,
  Select, Skeleton, Stat, Table, Tabs, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

const statusTone: Record<string, "success" | "info" | "warning" | "neutral" | "brand"> = {
  funded: "success", validated: "info", pending: "warning", internal: "brand",
};

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
      toast.error("Bank details required", "Enter the routing and account number so Lyons can validate the account.");
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
        ? "Lyons flagged the account (198) — recording attached."
        : "Lyons cleared the account (103).";
      toast.success("Sale recorded", `$${premium.toFixed(2)}/mo · ${carrier} · ${bankMsg}`);
      setLeadId(""); setPolicyNumber(""); setRecordingFile(null);
      setRoutingNumber(""); setAccountNumber(""); setAccountType("checking");
    } catch (err: unknown) {
      const detail = getErrorDetail(err) ?? "Try again.";
      // Lyons flagged the account (198) and no recording was attached — keep the form so they can add one.
      if (/recording/i.test(detail)) {
        toast.warning("Verification recording needed", detail);
      } else {
        toast.error("Couldn't record sale", detail);
      }
    }
  }

  const [tab, setTab] = useState<ViewTab>("record");

  return (
    <>
      <PageHeader
        title="Sales"
        description="Record new sales, validate them, and fund them through to commission."
      />

      <Tabs<ViewTab>
        className="mb-4"
        value={tab}
        onChange={setTab}
        items={[
          { value: "record", label: "Record & validate" },
          { value: "list", label: "All sales" },
        ]}
      />

      {tab === "list" ? <SalesList /> : <RecordView />}
    </>
  );

  function RecordView() {
    return (
      <>
      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4 mb-6">
        {/* Record sale */}
        <Card className="xl:col-span-2">
          <CardHeader title="Record a sale" subtitle="Capture a new closed deal." />
          <CardBody>
            <form onSubmit={submit} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Input
                label="Lead ID" required
                placeholder="UUID of the closed lead"
                value={leadId} onChange={(e) => setLeadId(e.target.value)}
                hint="Pick from the list on the right →"
              />
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

        {/* Validate / Fund */}
        <Card>
          <CardHeader title="Validate & fund" subtitle="Move sales through QA and funding." />
          <CardBody>
            <SaleActionForm
              label="Validate" placeholder="Sale ID to validate" tone="success"
              actions={[
                { label: "Approve", variant: "success", onClick: (id) => doValidate(id, true) },
                { label: "Reject",  variant: "danger",  onClick: (id) => doValidate(id, false) },
              ]}
              loading={validating}
            />
            <div className="my-4 border-t hairline" />
            <SaleActionForm
              label="Fund" placeholder="Sale ID to fund" tone="info"
              actions={[{ label: "Fund", variant: "primary", onClick: (id) => doFund(id) }]}
              loading={funding}
            />
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader
          title="Recent closed leads"
          subtitle="Eligible for sale recording"
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
                title="No closed leads"
                description="Closed leads will appear here once they're transitioned to Closed."
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
                        <div className="flex items-center gap-3">
                          <Avatar name={name} size={32} />
                          <div>
                            <div className="font-medium text-ink-900">{name}</div>
                            <div className="text-xs text-ink-500 font-mono">{l.id.slice(0, 8)}…</div>
                          </div>
                        </div>
                      </TD>
                      <TD className="text-ink-600">{l.phoneNumber}</TD>
                      <TD><Badge tone="warning" variant="soft" dot>{String(l.stage)}</Badge></TD>
                      <TD>
                        <div className="flex justify-end">
                          <Button variant="outline" size="sm" onClick={() => setLeadId(l.id)}>
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
    try {
      await validateSale({ id, approve }).unwrap();
      toast.success(approve ? "Sale approved" : "Sale rejected");
    } catch (err: unknown) {
      toast.error("Couldn't validate sale", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function doFund(id: string) {
    try {
      await fundSale(id).unwrap();
      toast.success("Sale funded");
    } catch (err: unknown) {
      toast.error("Couldn't fund sale", getErrorDetail(err) ?? "Try again.");
    }
  }
}

function SalesList() {
  const [filters, setFilters] = useState<SalesQuery>({ skip: 0, take: 50, sort: "soldAt-desc" });
  const { data, isLoading, isFetching } = useListSalesQuery(filters);
  const { data: users } = useListUsersQuery();
  const { data: carriers } = useCarriersQuery();

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
              onClick={() => update("status", undefined)} />
        <Stat label="Total premium" value={`$${(data?.totalPremium ?? 0).toLocaleString(undefined, { maximumFractionDigits: 0 })}`}  icon={<Icon name="dollar" size={16} />}    tone="success" />
        <Stat label="Funded"        value={data?.fundedCount ?? 0}                                                                  icon={<Icon name="success" size={16} />}   tone="success"
              onClick={() => update("status", "Funded")} />
        <Stat label="Validated"     value={data?.validatedCount ?? 0}                                                               icon={<Icon name="shield" size={16} />}    tone="brand"
              onClick={() => update("status", "Validated")} />
        <Stat label="Pending"       value={data?.pendingCount ?? 0}                                                                 icon={<Icon name="clock" size={16} />}     tone="warning"
              onClick={() => update("status", "Pending")} />
      </div>

      <Card className="mb-4">
        <CardBody className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-3">
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
            value={filters.from?.slice(0, 10) ?? ""}
            onChange={(e) => update("from", e.target.value ? new Date(e.target.value).toISOString() : undefined)}
          />
          <Input type="date" leftIcon={<Icon name="calendar" size={14} />}
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
            <div>{pageInfo} {isFetching && <span className="ml-2 text-ink-400">refreshing…</span>}</div>
            <Button variant="ghost" size="sm" leftIcon={<Icon name="refresh" size={13} />}
              onClick={() => setFilters({ skip: 0, take: 50, sort: "soldAt-desc" })}>
              Reset filters
            </Button>
          </div>
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0,1,2,3,4].map((i) => <Skeleton key={i} className="h-12 my-2" />)}</CardBody></Card>
      ) : !data || data.items.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="briefcase" size={20} />}
            title="No sales match"
            description="Try a different filter or date range."
          />
        </CardBody></Card>
      ) : (
        <>
          <Table>
            <THead>
              <TR>
                <TH>Sold</TH>
                <TH>Lead</TH>
                <TH>Closer</TH>
                <TH>Carrier</TH>
                <TH>Premium</TH>
                <TH>Status</TH>
                <TH>Policy #</TH>
              </TR>
            </THead>
            <TBody>
              {data.items.map((s) => (
                <TR key={s.id}>
                  <TD className="text-ink-600 whitespace-nowrap text-xs">
                    {new Date(s.soldAt).toLocaleString()}
                  </TD>
                  <TD>
                    <Link to={`/leads/${s.leadId}`} className="block hover:underline">
                      <div className="font-medium text-ink-900">{s.leadName}</div>
                      <div className="text-xs text-ink-500">{s.leadPhone}</div>
                    </Link>
                  </TD>
                  <TD>
                    {s.closerName ? (
                      <div className="flex items-center gap-2">
                        <Avatar name={s.closerName} size={24} />
                        <span className="text-ink-700 text-sm">{s.closerName}</span>
                      </div>
                    ) : <span className="text-ink-400">—</span>}
                  </TD>
                  <TD className="text-ink-700">{s.carrier}</TD>
                  <TD className="text-ink-900 font-medium">
                    ${s.monthlyPremium.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}/mo
                    <div className="text-xs text-ink-500">${s.annualPremium.toLocaleString(undefined, { maximumFractionDigits: 0 })}/yr</div>
                  </TD>
                  <TD>
                    <Badge tone={statusTone[s.status] ?? "neutral"} variant="soft" dot>
                      {s.status}{s.isInternalSale ? " · internal" : ""}
                    </Badge>
                  </TD>
                  <TD className="text-ink-500 font-mono text-xs">{s.policyNumber ?? "—"}</TD>
                </TR>
              ))}
            </TBody>
          </Table>

          <div className="flex items-center justify-between mt-4">
            <div className="text-xs text-ink-500">{pageInfo}</div>
            <div className="flex gap-1.5">
              <Button variant="outline" size="sm" disabled={skip === 0}
                leftIcon={<Icon name="chevronLeft" size={13} />}
                onClick={() => setFilters((f) => ({ ...f, skip: Math.max(0, (f.skip ?? 0) - take) }))}>
                Prev
              </Button>
              <Button variant="outline" size="sm" disabled={skip + take >= total}
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
  label, placeholder, actions, loading,
}: {
  label: string;
  placeholder: string;
  tone: "success" | "info";
  actions: { label: string; variant: ButtonVariant; onClick: (id: string) => void }[];
  loading: boolean;
}) {
  const [id, setId] = useState("");
  return (
    <div>
      <Input label={label} placeholder={placeholder} value={id} onChange={(e) => setId(e.target.value)} />
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
