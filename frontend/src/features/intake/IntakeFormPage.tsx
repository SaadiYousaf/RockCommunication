import { useSelector } from "react-redux";
import { getErrorDetail } from "../../shared/api/apiError";
import { useCaptureCloserLeadMutation, useCaptureIntakeLeadMutation } from "../../shared/api/baseApi";
import type { RootState } from "../../app/store";
import type { IntakeLeadInput } from "../../shared/api/types";
import { Badge, Card, CardBody, CardHeader, InfoHint, PageHeader, useToast } from "../../shared/ui";
import { IntakeLeadForm } from "./IntakeLeadForm";
import { INTAKE_MSG } from "./messages";

/**
 * Add Lead.
 *
 * One form, two jobs, and which one it is depends on who is looking at it.
 *
 * A FRONTER is starting the pipeline: the lead is captured at the Fronted stage and passed to a
 * Verifier. A CLOSER adding a lead has already had that conversation — a referral, a call-back, an
 * inbound — so theirs is captured as Verified and assigned straight to them.
 *
 * These are two different endpoints, and each is restricted to its own role. This page used to call
 * the fronter one unconditionally while the sidebar offered "Add Lead" to Closers as well, so every
 * Closer who tried to add a lead filled the whole form in and was refused on submit.
 */
export function IntakeFormPage() {
  const roles = useSelector((s: RootState) => s.auth.user?.roles ?? []);

  // A Closer who is ALSO a Fronter keeps the fronter flow: it is the one that feeds the pipeline,
  // and it is the more conservative of the two (the lead still gets verified).
  const asCloser = roles.includes("Closer") && !roles.includes("Fronter");

  const [captureAsFronter, fronterState] = useCaptureIntakeLeadMutation();
  const [captureAsCloser, closerState] = useCaptureCloserLeadMutation();

  const capture = asCloser ? captureAsCloser : captureAsFronter;
  const isLoading = asCloser ? closerState.isLoading : fronterState.isLoading;
  const toast = useToast();

  const copy = asCloser
    ? {
        eyebrow: INTAKE_MSG.addLeadCloserEyebrow,
        title: INTAKE_MSG.addLeadCloserTitle,
        description: INTAKE_MSG.addLeadCloserDescription,
        submit: INTAKE_MSG.addLeadCloserSubmit,
        subtitle: INTAKE_MSG.addLeadCloserSubtitle,
      }
    : {
        eyebrow: INTAKE_MSG.addLeadFronterEyebrow,
        title: INTAKE_MSG.addLeadFronterTitle,
        description: INTAKE_MSG.addLeadFronterDescription,
        submit: INTAKE_MSG.addLeadFronterSubmit,
        subtitle: INTAKE_MSG.addLeadFronterSubtitle,
      };

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
      <PageHeader eyebrow={copy.eyebrow} title={copy.title} description={copy.description} />
      <Card className="max-w-3xl">
        <CardHeader
          title={INTAKE_MSG.addLeadTitle}
          subtitle={copy.subtitle}
          action={
            <span className="inline-flex items-center gap-1">
              <Badge tone="warning" variant="soft" dot>{INTAKE_MSG.typingOnlyBadge}</Badge>
              <InfoHint title={INTAKE_MSG.typingOnlyBadge} side="left">
                {INTAKE_MSG.typingOnlyHint}
              </InfoHint>
            </span>
          }
        />
        <CardBody>
          <IntakeLeadForm onSubmit={onSubmit} isLoading={isLoading} submitLabel={copy.submit} />
        </CardBody>
      </Card>
    </>
  );
}
