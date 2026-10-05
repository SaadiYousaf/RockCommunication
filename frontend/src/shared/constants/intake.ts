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
  { value: "ReferredToHo", label: "Referred to HO" },
];

/** Sub-reasons for the "Error in application information" submission status. */
/** Verifier outcome -> what users see (the enum names are internal). */
export const VERIFIER_STATUS_LABEL: Record<string, string> = Object.fromEntries(
  VERIFIER_STATUSES.map((s) => [s.value, s.label]));
export const verifierStatusLabel = (s: string | null | undefined): string =>
  !s ? "—" : VERIFIER_STATUS_LABEL[s] ?? s;

/** Closer outcome -> what users see. */
export const CLOSER_STATUS_LABEL: Record<string, string> = Object.fromEntries(
  CLOSER_STATUSES.map((s) => [s.value, s.label]));
export const closerStatusLabel = (s: string | null | undefined): string =>
  !s ? "—" : CLOSER_STATUS_LABEL[s] ?? s;

export const VALIDATOR_ERROR_REASONS = ["Wrong banking / Payor issue", "Identity Error"] as const;

export const VALIDATOR_STATUS_LABEL: Record<ValidatorStatusValue, string> = Object.fromEntries(
  VALIDATOR_STATUSES.map((s) => [s.value, s.label]),
) as Record<ValidatorStatusValue, string>;

/**
 * One colour per status — nine statuses, nine distinct tones.
 *
 * Four of these used to be the same red and two the same grey, so scanning the column told you
 * something had gone wrong without telling you WHAT, and a submission agent had to read every row
 * to find the bad banks among the declines. The grouping still reads at a glance — greens are good,
 * warm colours need chasing, and the failures sit apart from each other — but no two states look
 * alike.
 */
export const VALIDATOR_STATUS_TONE: Record<ValidatorStatusValue, BadgeTone> = {
  Completed: "brand",                      // submitted, working its way through
  Approved: "info",                        // carrier said yes, not yet paying
  ActivePaid: "success",                   // the finished article
  NoUpdateInCommission: "warning",         // chase the carrier
  BadBank: "danger",                       // our side: the account is wrong
  Nsf: "orange",                           // their side: the money wasn't there
  Decline: "purple",                       // the carrier refused it
  ClientCancelled: "neutral",              // the customer walked away
  ErrorInApplicationInformation: "pink",   // fixable paperwork, not a lost sale
  ReferredToHo: "accent",                  // escalated out of the floor's hands, still open
};
