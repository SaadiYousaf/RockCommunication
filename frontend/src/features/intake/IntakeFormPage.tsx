import { getErrorDetail } from "../../shared/api/apiError";
import { useCaptureIntakeLeadMutation } from "../../shared/api/baseApi";
import type { IntakeLeadInput } from "../../shared/api/types";
import { Badge, Card, CardBody, CardHeader, InfoHint, PageHeader, useToast } from "../../shared/ui";
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
    } catch (err: unknown) {
      toast.error("Couldn't submit", getErrorDetail(err) ?? "Check the required fields and try again.");
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
          action={
            <span className="inline-flex items-center gap-1">
              <Badge tone="warning" variant="soft" dot>Typing only</Badge>
              <InfoHint title="Typing only" side="left">
                Every field must be typed by the agent — copy/paste is blocked. This keeps the lead captured live and TCPA-compliant, and stops recycled or pre-filled data.
              </InfoHint>
            </span>
          }
        />
        <CardBody>
          <IntakeLeadForm onSubmit={onSubmit} isLoading={isLoading} submitLabel="Submit to verifier" />
        </CardBody>
      </Card>
    </>
  );
}
