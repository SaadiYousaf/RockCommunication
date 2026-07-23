import { useCaptureIntakeLeadMutation } from "../../shared/api/baseApi";
import type { IntakeLeadInput } from "../../shared/api/types";
import { Badge, Card, CardBody, CardHeader, PageHeader, useToast } from "../../shared/ui";
import { IntakeLeadForm } from "./IntakeLeadForm";

/** Fronter intake form — Jornaya lead capture. All fields mandatory, typing-only. */
export function IntakeFormPage() {
  const [capture, { isLoading }] = useCaptureIntakeLeadMutation();
  const toast = useToast();

  async function onSubmit(input: IntakeLeadInput) {
    try {
      const r = await capture(input).unwrap();
      toast.success("Lead submitted", `${r.firstName} ${r.lastName} → verifier queue`);
      return true;
    } catch (err: any) {
      toast.error("Couldn't submit", err?.data?.detail ?? "Check the required fields and try again.");
      return false;
    }
  }

  return (
    <>
      <PageHeader
        title="Get Yourself Protected — Lead Intake"
        description="Capture the prospect's Jornaya details. All fields are required and must be typed (no paste)."
      />
      <Card className="max-w-3xl">
        <CardHeader
          title="New lead"
          subtitle="Final-expense intake"
          action={<Badge tone="warning" variant="soft" dot>Typing only</Badge>}
        />
        <CardBody>
          <IntakeLeadForm onSubmit={onSubmit} isLoading={isLoading} submitLabel="Submit to verifier" />
        </CardBody>
      </Card>
    </>
  );
}
