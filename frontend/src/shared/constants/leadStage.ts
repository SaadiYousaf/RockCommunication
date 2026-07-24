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

/** Badge tone per stage. String-keyed so callers can index with a raw API value + `?? "neutral"`. */
export const STAGE_TONE: Record<string, BadgeTone> = {
  New: "brand", Fronted: "info", Verified: "info", JrClosed: "warning",
  Closed: "warning", Validated: "success", Funded: "success",
  Followup: "neutral", Winback: "neutral", Lost: "danger",
};
