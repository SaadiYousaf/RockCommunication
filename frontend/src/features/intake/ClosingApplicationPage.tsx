import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useGetClosingApplicationQuery, useSubmitClosingApplicationMutation } from "../../shared/api/baseApi";
import type { CloserStatusValue } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, Icon, Input, PageHeader, Select, Skeleton, Textarea, useToast,
} from "../../shared/ui";

const CLOSER_STATUSES: { value: CloserStatusValue; label: string }[] = [
  { value: "CompleteAndSold", label: "Complete and Sold" },
  { value: "LostOnSocial", label: "Lost on Social" },
  { value: "LostOnAccount", label: "Lost on Account" },
  { value: "DncLead", label: "DNC Lead" },
  { value: "NotInterestedCallback", label: "Not Interested, Callback later" },
];

const blank = {
  healthConditions: "", gender: "", age: "", smokerStatus: "", name: "", dateOfBirth: "", address: "",
  carrier: "", plan: "", faceAmount: "", premium: "", email: "", beneficiary: "", secondBeneficiary: "",
  initialDraftDate: "", futureDraftDate: "", phoneNumber: "", altPhone: "", primaryDoctor: "", social: "",
  bornIn: "", driversLicense: "", height: "", weight: "", accountType: "checking", bankName: "",
  accountNumber: "", routingNumber: "", banking198Reason: "",
};

/** Closer's closing application. All fields typed (no paste). "Complete and Sold" creates the sale. */
export function ClosingApplicationPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const { data, isLoading } = useGetClosingApplicationQuery(id, { skip: !id });
  const [submit, { isLoading: saving }] = useSubmitClosingApplicationMutation();

  const [status, setStatus] = useState<CloserStatusValue | "">("");
  const [f, setF] = useState({ ...blank });
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) =>
    setF((p) => ({ ...p, [k]: e.target.value }));

  // Prefill from the lead (and any saved application).
  useEffect(() => {
    if (!data) return;
    const a = data.application;
    setStatus(data.closerStatus !== "None" ? data.closerStatus : "");
    setF({
      ...blank,
      name: a?.name ?? `${data.firstName} ${data.lastName}`.trim(),
      dateOfBirth: (a?.dateOfBirth ?? data.dateOfBirth)?.slice(0, 10) ?? "",
      address: a?.address ?? data.address ?? "",
      email: a?.email ?? data.email ?? "",
      phoneNumber: a?.phoneNumber ?? data.phoneNumber ?? "",
      healthConditions: a?.healthConditions ?? "", gender: a?.gender ?? "", age: a?.age?.toString() ?? "",
      smokerStatus: a?.smokerStatus ?? "", carrier: a?.carrier ?? "", plan: a?.plan ?? "",
      faceAmount: a?.faceAmount?.toString() ?? "", premium: a?.premium?.toString() ?? "",
      beneficiary: a?.beneficiary ?? "", secondBeneficiary: a?.secondBeneficiary ?? "",
      initialDraftDate: a?.initialDraftDate?.slice(0, 10) ?? "", futureDraftDate: a?.futureDraftDate?.slice(0, 10) ?? "",
      altPhone: a?.altPhone ?? "", primaryDoctor: a?.primaryDoctor ?? "", social: a?.social ?? "",
      bornIn: a?.bornIn ?? "", driversLicense: a?.driversLicense ?? "", height: a?.height ?? "", weight: a?.weight ?? "",
      accountType: a?.accountType ?? "checking", bankName: a?.bankName ?? "", accountNumber: "", routingNumber: a?.routingNumber ?? "",
    });
  }, [data]);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!status) { toast.error("Select a closer status"); return; }
    try {
      const r = await submit({
        leadId: id, status,
        application: {
          healthConditions: f.healthConditions || undefined, gender: f.gender, age: parseInt(f.age, 10) || 0,
          smokerStatus: f.smokerStatus, name: f.name, dateOfBirth: f.dateOfBirth ? new Date(f.dateOfBirth).toISOString() : undefined,
          address: f.address, carrier: f.carrier, plan: f.plan, faceAmount: parseFloat(f.faceAmount) || 0,
          premium: parseFloat(f.premium) || 0, email: f.email, beneficiary: f.beneficiary,
          secondBeneficiary: f.secondBeneficiary || undefined,
          initialDraftDate: f.initialDraftDate ? new Date(f.initialDraftDate).toISOString() : undefined,
          futureDraftDate: f.futureDraftDate ? new Date(f.futureDraftDate).toISOString() : undefined,
          phoneNumber: f.phoneNumber, altPhone: f.altPhone || undefined, primaryDoctor: f.primaryDoctor,
          social: f.social, bornIn: f.bornIn, driversLicense: f.driversLicense, height: f.height, weight: f.weight,
          accountType: f.accountType, bankName: f.bankName, accountNumber: f.accountNumber, routingNumber: f.routingNumber,
          banking198Reason: f.banking198Reason || undefined,
        },
      }).unwrap();
      toast.success("Application submitted",
        r.status === "CompleteAndSold" ? "Sale created (Lyons cleared the account)." : `Marked ${r.status}`);
      navigate("/close-queue");
    } catch (err: any) {
      toast.error("Couldn't submit", err?.data?.detail ?? "Check the required fields and bank details.");
    }
  }

  if (isLoading) return <Skeleton className="h-96" />;
  if (!data) return <PageHeader title="Lead not found" />;

  const sold = status === "CompleteAndSold";

  return (
    <>
      <PageHeader
        title={`Closing — ${data.firstName} ${data.lastName}`}
        description="Complete the application. All fields are typed (no paste). 'Complete and Sold' validates banking via Lyons and creates the sale."
      />
      {/* Read-only intake context captured by the fronter, so the closer has the full picture. */}
      <Card className="max-w-4xl mb-4">
        <CardHeader title="Lead info (from intake)" />
        <CardBody>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-2 text-sm">
            <Field label="Marital status" value={data.maritalStatus} />
            <Field label="Age" value={data.ageYears != null ? String(data.ageYears) : null} />
            <Field label="City" value={data.city} />
            <Field label="State" value={data.state} />
            <Field label="Zipcode" value={data.postalCode} />
            <Field label="Phone" value={data.phoneNumber} />
            <Field label="Email" value={data.email} />
            <Field label="Created date" value={data.createdAt ? data.createdAt.slice(0, 10) : null} />
            <Field label="Jornaya LeadiD" value={data.jornayaLeadId} className="col-span-2 sm:col-span-4 font-mono text-xs" />
          </div>
        </CardBody>
      </Card>
      <form onSubmit={onSubmit} className="space-y-4 max-w-4xl">
        <Card>
          <CardHeader title="Closer status"
            action={<Badge tone="warning" variant="soft" dot>Typing only</Badge>} />
          <CardBody>
            <Select label="Status (select at least one)" required value={status} onChange={(e) => setStatus(e.target.value as CloserStatusValue)} containerClassName="max-w-sm">
              <option value="">Select…</option>
              {CLOSER_STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </Select>
            {!sold && status && (
              <p className="text-xs text-ink-500 mt-2">Full application fields are only required for “Complete and Sold”.</p>
            )}
          </CardBody>
        </Card>

        <Section title="Health & applicant">
          <Textarea label="Health conditions" secure containerClassName="sm:col-span-2" value={f.healthConditions} onChange={set("healthConditions")} />
          <Input label="Gender" required={sold} secure value={f.gender} onChange={set("gender")} />
          <Input label="Age" type="number" required={sold} min={1} max={129} value={f.age} onChange={set("age")} />
          <Input label="Smoker status" required={sold} secure value={f.smokerStatus} onChange={set("smokerStatus")} placeholder="Smoker / Non-smoker" />
          <Input label="Name" required={sold} secure value={f.name} onChange={set("name")} />
          <Input label="DOB" type="date" required={sold} value={f.dateOfBirth} onChange={set("dateOfBirth")} />
          <Input label="Address" required={sold} secure containerClassName="sm:col-span-2" value={f.address} onChange={set("address")} />
        </Section>

        <Section title="Policy">
          <Input label="Carrier" required={sold} secure value={f.carrier} onChange={set("carrier")} />
          <Input label="Plan" required={sold} secure value={f.plan} onChange={set("plan")} />
          <Input label="Face amount" type="number" required={sold} min={0} step="0.01" leftIcon={<Icon name="dollar" size={14} />} value={f.faceAmount} onChange={set("faceAmount")} />
          <Input label="Premium" type="number" required={sold} min={0} step="0.01" leftIcon={<Icon name="dollar" size={14} />} value={f.premium} onChange={set("premium")} />
          <Input label="Email" type="email" required={sold} secure value={f.email} onChange={set("email")} />
          <Input label="Beneficiary" required={sold} secure value={f.beneficiary} onChange={set("beneficiary")} />
          <Input label="Second beneficiary" secure value={f.secondBeneficiary} onChange={set("secondBeneficiary")} />
          <Input label="Initial draft date" type="date" required={sold} value={f.initialDraftDate} onChange={set("initialDraftDate")} />
          <Input label="Future draft date" type="date" value={f.futureDraftDate} onChange={set("futureDraftDate")} />
        </Section>

        <Section title="Contact & identity">
          <Input label="Phone number" required={sold} secure value={f.phoneNumber} onChange={set("phoneNumber")} />
          <Input label="Alt phone" secure value={f.altPhone} onChange={set("altPhone")} />
          <Input label="Primary doctor" required={sold} secure value={f.primaryDoctor} onChange={set("primaryDoctor")} />
          <Input label="Social (SSN)" required={sold} secure value={f.social} onChange={set("social")} />
          <Input label="Born in" required={sold} secure value={f.bornIn} onChange={set("bornIn")} />
          <Input label="Driver's licence / State ID" required={sold} secure value={f.driversLicense} onChange={set("driversLicense")} />
          <Input label="Height" required={sold} secure value={f.height} onChange={set("height")} />
          <Input label="Weight" required={sold} secure value={f.weight} onChange={set("weight")} />
        </Section>

        <Section title="Banking (validated by Lyons)">
          <Select label="Account type" required={sold} value={f.accountType} onChange={set("accountType")}>
            <option value="checking">Checking</option>
            <option value="savings">Savings</option>
          </Select>
          <Input label="Bank name" required={sold} secure value={f.bankName} onChange={set("bankName")} />
          <Input label="Account number" required={sold} secure inputMode="numeric" value={f.accountNumber} onChange={set("accountNumber")} />
          <Input label="Routing number" required={sold} secure inputMode="numeric" value={f.routingNumber} onChange={set("routingNumber")} />
          <Input label="Reason (only if Lyons flags the account — code 198)" secure containerClassName="sm:col-span-2"
            placeholder="Why proceed on a flagged account?" value={f.banking198Reason} onChange={set("banking198Reason")} />
        </Section>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => navigate("/close-queue")}>Cancel</Button>
          <Button type="submit" loading={saving} leftIcon={<Icon name="check" size={16} />}>
            {sold ? "Complete & create sale" : "Submit"}
          </Button>
        </div>
      </form>
    </>
  );
}

function Field({ label, value, className }: { label: string; value?: string | null; className?: string }) {
  return (
    <div className={className}>
      <div className="text-xs text-ink-500">{label}</div>
      <div className="text-ink-900">{value && value.trim() ? value : "—"}</div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card>
      <CardHeader title={title} />
      <CardBody>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">{children}</div>
      </CardBody>
    </Card>
  );
}
