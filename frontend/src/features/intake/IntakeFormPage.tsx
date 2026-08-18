import { getErrorDetail } from "../../shared/api/apiError";
import { useCaptureIntakeLeadMutation } from "../../shared/api/baseApi";
import type { IntakeLeadInput } from "../../shared/api/types";
import { Badge, Card, CardBody, CardHeader, InfoHint, PageHeader, useToast } from "../../shared/ui";
import { IntakeLeadForm } from "./IntakeLeadForm";
import { INTAKE_MSG } from "./messages";

/** Fronter intake form — Jornaya lead capture. All fields mandatory, typing-only. */
export function IntakeFormPage() {
  const [capture, { isLoading }] = useCaptureIntakeLeadMutation();
  const toast = useToast();

  async function onSubmit(input: IntakeLeadInput) {
    try {
      const r = await capture(input).unwrap();
      toast.success(INTAKE_MSG.leadSubmittedTitle, INTAKE_MSG.leadSubmittedDesc(`${r.firstName} ${r.lastName}`));
      return true;
    } catch (err: unknown) {
      toast.error(INTAKE_MSG.submitFailedTitle, getErrorDetail(err) ?? INTAKE_MSG.checkRequiredFields);
      return false;
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Fronter intake"
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
