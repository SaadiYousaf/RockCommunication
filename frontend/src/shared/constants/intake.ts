import type { BadgeTone } from "../ui";
import type { VerifierStatusValue, CloserStatusValue, ValidatorStatusValue } from "../api/types";

/** Marital-status options for the Jornaya intake / verifier-edit forms. */
export const MARITAL_STATUSES = ["Single", "Married", "Divorced", "Widowed", "Separated"] as const;

/** Verifier outcomes (queue → status dropdown). */
export const VERIFIER_STATUSES: { value: VerifierStatusValue; label: string }[] = [
  { value: "Verified", label: "Verified" },
  { value: "NotInterested", label: "Not interested" },
  { value: "Dnc", label: "DNC" },
  { value: "Busy", label: "Busy" },
  { value: "CallBack", label: "Call Back" },
  { value: "DeadAir", label: "Dead Air" },
];

/** Closer outcomes. "Complete and Sold" creates the sale. */
export const CLOSER_STATUSES: { value: CloserStatusValue; label: string }[] = [
  { value: "CompleteAndSold", label: "Complete and Sold" },
  { value: "LostOnSocial", label: "Lost on Social" },
  { value: "LostOnAccount", label: "Lost on Account" },
  { value: "DncLead", label: "DNC Lead" },
  { value: "NotInterestedCallback", label: "Not Interested, Callback later" },
];

/** Submission-agent (validator) statuses. */
export const VALIDATOR_STATUSES: { value: ValidatorStatusValue; label: string }[] = [
  { value: "Completed", label: "Completed" },
  { value: "Approved", label: "Approved" },
  { value: "ActivePaid", label: "Active Paid" },
  { value: "NoUpdateInCommission", label: "No update in commission" },
  { value: "BadBank", label: "Bad Bank" },
  { value: "Nsf", label: "NSF" },
  { value: "Decline", label: "Decline" },
  { value: "ClientCancelled", label: "Client Cancelled" },
  { value: "ErrorInApplicationInformation", label: "Error in application information" },
];

/** Sub-reasons for the "Error in application information" submission status. */
export const VALIDATOR_ERROR_REASONS = ["Wrong banking / Payor issue", "Identity Error"] as const;

export const VALIDATOR_STATUS_LABEL: Record<ValidatorStatusValue, string> = Object.fromEntries(
  VALIDATOR_STATUSES.map((s) => [s.value, s.label]),
) as Record<ValidatorStatusValue, string>;

export const VALIDATOR_STATUS_TONE: Record<ValidatorStatusValue, BadgeTone> = {
  Completed: "neutral",
  Approved: "info",
  ActivePaid: "success",
  NoUpdateInCommission: "warning",
  BadBank: "danger",
  Nsf: "danger",
  Decline: "danger",
  ClientCancelled: "neutral",
  ErrorInApplicationInformation: "danger",
};
