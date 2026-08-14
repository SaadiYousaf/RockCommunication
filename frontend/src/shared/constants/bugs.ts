import type { BadgeTone } from "../ui";

/** Backend BugStatus enum names (order = the intended workflow left→right). */
export type BugStatus =
  | "New" | "Triaged" | "InProgress" | "Resolved" | "Closed"
  | "WontFix" | "Duplicate" | "CannotReproduce";

export type BugSeverity = "Low" | "Medium" | "High" | "Critical";

export interface BugStatusMeta {
  value: BugStatus;
  label: string;
  tone: BadgeTone;
  /** True for the terminal statuses that take the bug off the active board. */
  terminal: boolean;
  description: string;
}

/**
 * The bug lifecycle. Professional issue-tracker vocabulary: a report starts New, is Triaged when
 * confirmed, moves In Progress, then Resolved and Closed — or exits via Won't Fix / Duplicate /
 * Can't Reproduce. This single ordered list drives the status filter, the triage dropdown, and badges.
 */
export const BUG_STATUSES: BugStatusMeta[] = [
  { value: "New",             label: "New",             tone: "info",    terminal: false, description: "Reported, awaiting triage." },
  { value: "Triaged",         label: "Triaged",         tone: "brand",   terminal: false, description: "Confirmed as a valid, reproducible issue." },
  { value: "InProgress",      label: "In Progress",     tone: "warning", terminal: false, description: "Actively being worked on." },
  { value: "Resolved",        label: "Resolved",        tone: "success", terminal: false, description: "A fix has been made." },
  { value: "Closed",          label: "Closed",          tone: "neutral", terminal: true,  description: "Verified and archived." },
  { value: "WontFix",         label: "Won't Fix",       tone: "neutral", terminal: true,  description: "Valid, but intentionally not being fixed." },
  { value: "Duplicate",       label: "Duplicate",       tone: "neutral", terminal: true,  description: "Already tracked by another report." },
  { value: "CannotReproduce", label: "Can't Reproduce", tone: "danger",  terminal: true,  description: "Could not be reproduced." },
];

/** The linear "happy path" the Stepper renders — off-ramp statuses sit outside it. */
export const BUG_WORKFLOW: BugStatus[] = ["New", "Triaged", "InProgress", "Resolved", "Closed"];
export const isBugOffRamp = (v: string): boolean => !BUG_WORKFLOW.includes(v as BugStatus);

const STATUS_BY_VALUE = new Map(BUG_STATUSES.map((s) => [s.value, s]));

export const bugStatusMeta = (v: string): BugStatusMeta =>
  STATUS_BY_VALUE.get(v as BugStatus) ?? { value: "New", label: v, tone: "neutral", terminal: false, description: "" };
export const bugStatusLabel = (v: string): string => bugStatusMeta(v).label;

export interface BugSeverityMeta {
  value: BugSeverity;
  label: string;
  tone: BadgeTone;
  /** Tailwind border colour for the row's left accent stripe. */
  accent: string;
}

export const BUG_SEVERITIES: BugSeverityMeta[] = [
  { value: "Low",      label: "Low",      tone: "neutral", accent: "border-l-ink-300" },
  { value: "Medium",   label: "Medium",   tone: "info",    accent: "border-l-sky-400" },
  { value: "High",     label: "High",     tone: "warning", accent: "border-l-amber-400" },
  { value: "Critical", label: "Critical", tone: "danger",  accent: "border-l-rose-500" },
];

const SEVERITY_BY_VALUE = new Map(BUG_SEVERITIES.map((s) => [s.value, s]));
export const bugSeverityMeta = (v: string): BugSeverityMeta =>
  SEVERITY_BY_VALUE.get(v as BugSeverity) ?? { value: "Medium", label: v, tone: "neutral", accent: "border-l-ink-300" };
