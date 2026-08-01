import type { BadgeTone } from "../../shared/ui";
import type { AttendeeResponse, MeetingStatus } from "../../shared/api/types";

// Re-export the API types so the scheduler feature has a single import surface.
export type { Meeting, MeetingAttendee, MeetingInput, AttendeeResponse, MeetingStatus } from "../../shared/api/types";

/** RSVP → badge tone. */
export const RESPONSE_TONE: Record<AttendeeResponse, BadgeTone> = {
  Pending: "neutral",
  Accepted: "success",
  Declined: "danger",
  Tentative: "warning",
};

/** RSVP → short human label. */
export const RESPONSE_LABEL: Record<AttendeeResponse, string> = {
  Pending: "No response",
  Accepted: "Accepted",
  Declined: "Declined",
  Tentative: "Tentative",
};

/** Meeting status → badge tone. */
export const STATUS_TONE: Record<MeetingStatus, BadgeTone> = {
  Scheduled: "brand",
  Cancelled: "danger",
  Completed: "neutral",
};

/** The three RSVP actions an invitee can take (Pending is never chosen explicitly). */
export const RSVP_CHOICES: { value: Exclude<AttendeeResponse, "Pending">; label: string; tone: BadgeTone }[] = [
  { value: "Accepted", label: "Accept", tone: "success" },
  { value: "Tentative", label: "Tentative", tone: "warning" },
  { value: "Declined", label: "Decline", tone: "danger" },
];
