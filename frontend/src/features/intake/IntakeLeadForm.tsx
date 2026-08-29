import { MARITAL_STATUSES as MARITAL } from "../../shared/constants/intake";
import { useState } from "react";
import type { IntakeLeadInput } from "../../shared/api/types";
import { Button, FieldGroup, Icon, InfoHint, Input, Select, Textarea } from "../../shared/ui";
import { INTAKE_MSG } from "./messages";


const empty = {
  firstName: "", lastName: "", maritalStatus: "", createdDate: new Date().toISOString().slice(0, 10),
  streetAddress: "", city: "", state: "", zipcode: "", phoneNumber: "",
  birthDate: "", ageYears: "", email: "", jornayaLeadId: "", notes: "",
};

/**
 * The "Get Yourself Protected" Jornaya intake form fields. Shared by the Fronter
 * intake page and the Closer "add lead" modal. All fields mandatory, typing-only.
 */
export function IntakeLeadForm({
  onSubmit,
  isLoading,
  submitLabel,
}: {
  onSubmit: (input: IntakeLeadInput) => Promise<boolean>;
  isLoading: boolean;
  submitLabel: string;
}) {
  const [f, setF] = useState({ ...empty });
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) =>
    setF({ ...f, [k]: e.target.value });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const ok = await onSubmit({
      firstName: f.firstName, lastName: f.lastName, maritalStatus: f.maritalStatus,
      createdDate: new Date(f.createdDate).toISOString(),
      streetAddress: f.streetAddress, city: f.city, state: f.state, zipcode: f.zipcode,
      phoneNumber: f.phoneNumber, birthDate: new Date(f.birthDate).toISOString(),
      ageYears: parseInt(f.ageYears, 10) || 0, email: f.email,
      jornayaLeadId: f.jornayaLeadId || undefined,
      notes: f.notes.trim() || undefined,
    });
    if (ok) setF({ ...empty });
  }

  return (
    // Grouped rather than one wall of twelve fields: a person filling this in live on a call is
    // reading down a form, and "who are they / where are they / how do we reach them" are three
    // different questions asked at three different points in the conversation.
    <form onSubmit={submit} className="space-y-6">
      <FieldGroup title={INTAKE_MSG.groupCustomer} hint={INTAKE_MSG.groupCustomerHint}>
      <Input label="First name" required secure value={f.firstName} onChange={set("firstName")} />
      <Input label="Last name" required secure value={f.lastName} onChange={set("lastName")} />
      <Select label="Marital status" required value={f.maritalStatus} onChange={set("maritalStatus")}>
        <option value="" disabled>Select…</option>
        {MARITAL.map((m) => <option key={m} value={m}>{m}</option>)}
      </Select>
      <Input label="Birth date" type="date" required leftIcon={<Icon name="calendar" size={14} />} value={f.birthDate} onChange={set("birthDate")} />
      <Input label="Age (years)" type="number" required min={1} max={129} className="tabular-nums" value={f.ageYears} onChange={set("ageYears")} />
      </FieldGroup>

      <FieldGroup title={INTAKE_MSG.groupContact} hint={INTAKE_MSG.groupContactHint}>
      <Input label="Phone number" required secure leftIcon={<Icon name="phone" size={14} />} placeholder="(555) 123-4567" className="tabular-nums" value={f.phoneNumber} onChange={set("phoneNumber")} />
      <Input label="Email" type="email" required secure leftIcon={<Icon name="mail" size={14} />} placeholder="name@example.com" value={f.email} onChange={set("email")} />
      <Input label="Street address" required secure containerClassName="sm:col-span-2" value={f.streetAddress} onChange={set("streetAddress")} />
      <Input label="City" required secure value={f.city} onChange={set("city")} />
      <Input label="State" required secure placeholder="e.g. TX" value={f.state} onChange={set("state")} />
      <Input label="Zipcode" required secure inputMode="numeric" placeholder="5-digit ZIP" className="tabular-nums" value={f.zipcode} onChange={set("zipcode")} />
      </FieldGroup>

      <FieldGroup title={INTAKE_MSG.groupCompliance} hint={INTAKE_MSG.groupComplianceHint}>
      <Input label="Created date" type="date" required leftIcon={<Icon name="calendar" size={14} />} value={f.createdDate} onChange={set("createdDate")} />
      <div className="sm:col-span-2 flex flex-col gap-1.5">
        <div className="flex items-center gap-1">
          <span className="text-[12px] font-medium text-ink-700 leading-none">Jornaya LeadiD</span>
          <InfoHint title="Jornaya LeadiD" side="right">
            A compliance tracking token from Jornaya that proves the prospect consented to be contacted (TCPA) and ties this lead to the web form it came from. Optional — leave blank if you don't have one.
          </InfoHint>
        </div>
        <Input placeholder="Optional token" value={f.jornayaLeadId} onChange={set("jornayaLeadId")} />
      </div>
      <Textarea
        label="Notes" containerClassName="sm:col-span-2" rows={3}
        placeholder="Optional — context for this lead (why they're interested, best time to call, etc.)"
        value={f.notes} onChange={set("notes")}
      />
      </FieldGroup>

      <div className="flex justify-end pt-1">
        <Button type="submit" loading={isLoading} leftIcon={<Icon name="check" size={16} />}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}
