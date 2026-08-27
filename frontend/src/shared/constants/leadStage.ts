import type { BadgeTone } from "../ui";
import type { WorkflowStage } from "../api/types";

/** Pipeline stages in pipeline order. */
export const WORKFLOW_STAGES: WorkflowStage[] = [
  "New", "Fronted", "Verified", "JrClosed", "Closed", "Validated", "Funded", "Followup", "Winback", "Lost",
];

/** Maps the API's numeric OR string stage value to the canonical stage name. */
export const STAGE_MAP: Record<number | string, WorkflowStage> = {
  0: "New", 10: "Fronted", 20: "Verified", 30: "JrClosed", 40: "Closed",
  50: "Validated", 60: "Funded", 70: "Followup", 80: "Winback", 90: "Lost",
  New: "New", Fronted: "Fronted", Verified: "Verified", JrClosed: "JrClosed", Closed: "Closed",
  Validated: "Validated", Funded: "Funded", Followup: "Followup", Winback: "Winback", Lost: "Lost",
};

/** Maps the API's numeric OR string disposition value to its name. */
export const DISPOSITION_MAP: Record<number | string, string> = {
  0: "None", 1: "Interested", 2: "NotInterested", 3: "CallBack", 4: "DoNotCall",
  5: "Sold", 6: "NotQualified", 7: "Voicemail", 8: "NoAnswer", 9: "WrongNumber",
  None: "None", Interested: "Interested", NotInterested: "NotInterested", CallBack: "CallBack",
  DoNotCall: "DoNotCall", Sold: "Sold", NotQualified: "NotQualified", Voicemail: "Voicemail",
  NoAnswer: "NoAnswer", WrongNumber: "WrongNumber",
};

export const stageOf = (s: number | string): WorkflowStage => STAGE_MAP[s] ?? "New";
export const dispOf = (d: number | string): string => DISPOSITION_MAP[d] ?? "None";

/**
 * What users SEE for a pipeline stage. The enum names are internal (`JrClosed`, `Followup`) and
 * must never reach the screen — always render through `stageLabel`.
 */
export const STAGE_LABEL: Record<string, string> = {
  New: "New",
  Fronted: "Fronted",
  Verified: "Verified",
  JrClosed: "Jr Closed",
  Closed: "Closed",
  Validated: "Validated",
  Funded: "Funded",
  Followup: "Follow-up",
  Winback: "Win-back",
  Lost: "Lost",
};

/** Friendly stage name. Accepts the numeric or string form the API may send. */
export const stageLabel = (s: number | string | null | undefined): string => {
  if (s === null || s === undefined) return "—";
  const name = STAGE_MAP[s] ?? String(s);
  return STAGE_LABEL[name] ?? name;
};

/** What users SEE for a lead disposition — the enum names are internal. */
export const DISPOSITION_LABEL: Record<string, string> = {
  None: "None",
  Interested: "Interested",
  NotInterested: "Not interested",
  CallBack: "Call back",
  DoNotCall: "Do not call",
  Sold: "Sold",
  NotQualified: "Not qualified",
  Voicemail: "Voicemail",
  NoAnswer: "No answer",
  WrongNumber: "Wrong number",
};

/** Friendly disposition name. Accepts the numeric or string form the API may send. */
export const dispositionLabel = (d: number | string | null | undefined): string => {
  if (d === null || d === undefined) return "—";
  const name = DISPOSITION_MAP[d] ?? String(d);
  return DISPOSITION_LABEL[name] ?? name;
};


/**
 * Valid next stages per current stage — mirrors the backend TransitionLeadHandler.Allowed map
 * (backend/src/CRM.Application/Leads/Commands/TransitionLead.cs). Used to show only the moves
 * that will succeed instead of every stage button (invalid ones 409).
 */
export const ALLOWED_TRANSITIONS: Record<WorkflowStage, WorkflowStage[]> = {
  New:       ["Fronted", "Lost"],
  Fronted:   ["Verified", "Lost", "Followup"],
  Verified:  ["JrClosed", "Closed", "Lost", "Followup"],
  JrClosed:  ["Closed", "Lost", "Followup"],
  Closed:    ["Validated", "Lost"],
  Validated: ["Funded", "Lost"],
  Funded:    ["Followup"],
  Followup:  ["Fronted", "Verified", "Closed", "Winback", "Lost"],
  Winback:   ["Fronted", "Lost"],
  Lost:      ["Winback"],
};

/** Terminal / off-track stages that warrant a confirmation before moving there. */
export const TERMINAL_STAGES: WorkflowStage[] = ["Lost"];

/** One-line, plain-language meaning of each pipeline stage (for the InfoHint glossary). */
export const STAGE_DESCRIPTIONS: Record<WorkflowStage, string> = {
  New: "Fresh lead, not yet worked.",
  Fronted: "A fronter made initial contact and pitched.",
  Verified: "A verifier confirmed the details; ready for a closer.",
  JrClosed: "A junior closer worked it before the senior close.",
  Closed: "The closing application was taken (a sale exists).",
  Validated: "A submission agent approved the sale.",
  Funded: "The policy funded — commission-eligible.",
  Followup: "Parked for a later follow-up / callback.",
  Winback: "Being re-engaged after being lost.",
  Lost: "Not moving forward (off the pipeline).",
};

/** Plain-language meaning of each call-outcome disposition. */
export const DISPOSITION_DESCRIPTIONS: Record<string, string> = {
  Interested: "Prospect is interested — keep working it.",
  NotInterested: "Prospect declined.",
  CallBack: "Prospect asked to be called back later.",
  DoNotCall: "Prospect asked not to be contacted (should be added to DNC).",
  Sold: "Sale completed on this contact.",
  NotQualified: "Doesn't meet qualification criteria.",
  Voicemail: "Left a voicemail.",
  NoAnswer: "No answer.",
  WrongNumber: "Number doesn't reach the prospect.",
};

/** Badge tone per stage. String-keyed so callers can index with a raw API value + `?? "neutral"`. */
export const STAGE_TONE: Record<string, BadgeTone> = {
  New: "brand", Fronted: "info", Verified: "info", JrClosed: "warning",
  Closed: "warning", Validated: "success", Funded: "success",
  Followup: "neutral", Winback: "neutral", Lost: "danger",
};
