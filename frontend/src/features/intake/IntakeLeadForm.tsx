import { MARITAL_STATUSES as MARITAL } from "../../shared/constants/intake";
import { useState } from "react";
import type { IntakeLeadInput } from "../../shared/api/types";
import { Button, Icon, Input, Select } from "../../shared/ui";


const empty = {
  firstName: "", lastName: "", maritalStatus: "", createdDate: new Date().toISOString().slice(0, 10),
  streetAddress: "", city: "", state: "", zipcode: "", phoneNumber: "",
  birthDate: "", ageYears: "", email: "", jornayaLeadId: "",
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
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
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
    });
    if (ok) setF({ ...empty });
  }

  return (
    <form onSubmit={submit} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
      <Input label="First name" required secure value={f.firstName} onChange={set("firstName")} />
      <Input label="Last name" required secure value={f.lastName} onChange={set("lastName")} />
      <Select label="Marital status" required value={f.maritalStatus} onChange={set("maritalStatus")}>
        <option value="" disabled>Select…</option>
        {MARITAL.map((m) => <option key={m} value={m}>{m}</option>)}
      </Select>
      <Input label="Created date" type="date" required value={f.createdDate} onChange={set("createdDate")} />
      <Input label="Street address" required secure containerClassName="sm:col-span-2" value={f.streetAddress} onChange={set("streetAddress")} />
      <Input label="City" required secure value={f.city} onChange={set("city")} />
      <Input label="State" required secure value={f.state} onChange={set("state")} />
      <Input label="Zipcode" required secure inputMode="numeric" value={f.zipcode} onChange={set("zipcode")} />
      <Input label="Phone number" required secure leftIcon={<Icon name="phone" size={14} />} value={f.phoneNumber} onChange={set("phoneNumber")} />
      <Input label="Birth date" type="date" required value={f.birthDate} onChange={set("birthDate")} />
      <Input label="Age (years)" type="number" required min={1} max={129} value={f.ageYears} onChange={set("ageYears")} />
      <Input label="Email" type="email" required secure containerClassName="sm:col-span-2" value={f.email} onChange={set("email")} />
      <Input label="Jornaya LeadiD" containerClassName="sm:col-span-2" placeholder="Optional token" value={f.jornayaLeadId} onChange={set("jornayaLeadId")} />
      <div className="sm:col-span-2 flex justify-end pt-1">
        <Button type="submit" loading={isLoading} leftIcon={<Icon name="check" size={16} />}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}
