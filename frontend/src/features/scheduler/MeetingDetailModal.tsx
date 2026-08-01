import {
  useCancelMeetingMutation, useGetMeetingQuery, useRespondMeetingMutation,
} from "../../shared/api/baseApi";
import { getErrorDetail } from "../../shared/api/apiError";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import type { Meeting } from "./types";
import { RESPONSE_LABEL, RESPONSE_TONE, RSVP_CHOICES, STATUS_TONE } from "./types";
import { formatRange } from "./calendarUtils";
import {
  Badge, Button, Icon, InfoHint, Modal, Skeleton, useToast,
} from "../../shared/ui";

/**
 * Read-only meeting detail with actions: an invited user can RSVP (Accept / Tentative / Decline)
 * and the organizer can edit or cancel. Cancelling notifies every invitee by email (server-side).
 */
export function MeetingDetailModal({
  meetingId, onClose, onEdit,
}: {
  meetingId: string;
  onClose: () => void;
  onEdit: (m: Meeting) => void;
}) {
  const toast = useToast();
  const confirm = useConfirm();
  const { data: meeting, isLoading } = useGetMeetingQuery(meetingId);
  const [respond, { isLoading: responding }] = useRespondMeetingMutation();
  const [cancel, { isLoading: cancelling }] = useCancelMeetingMutation();

  async function onRespond(response: (typeof RSVP_CHOICES)[number]["value"]) {
    try {
      await respond({ id: meetingId, response }).unwrap();
      toast.success("Response saved", RESPONSE_LABEL[response]);
    } catch (err: unknown) {
      toast.error("Couldn't respond", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function onCancel() {
    if (!(await confirm({
      title: "Cancel this meeting?",
      description: "Every invitee will be notified by email that it's cancelled. This can't be undone.",
      confirmLabel: "Cancel meeting", danger: true,
    }))) return;
    try {
      await cancel(meetingId).unwrap();
      toast.success("Meeting cancelled");
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't cancel", getErrorDetail(err) ?? "Try again.");
    }
  }

  const cancelled = meeting?.status === "Cancelled";
  const canRespond = !!meeting && !meeting.isOrganizer && meeting.myResponse != null && !cancelled;

  return (
    <Modal
      open
      onClose={onClose}
      title={
        isLoading ? "Meeting" : (
          <span className="flex items-center gap-2.5 flex-wrap">
            <span className={cancelled ? "line-through text-ink-400" : ""}>{meeting?.title}</span>
            {meeting && (
              <span className="inline-flex items-center gap-1">
                <Badge tone={STATUS_TONE[meeting.status]} variant="soft">{meeting.status}</Badge>
                <InfoHint title="Meeting status" side="bottom">
                  <b>Scheduled</b> — still on the calendar. <b>Completed</b> — the end time has passed.
                  <b>Cancelled</b> — called off; every invitee was emailed.
                </InfoHint>
              </span>
            )}
          </span>
        )
      }
      size="lg"
      footer={meeting && meeting.isOrganizer && !cancelled ? (
        <>
          <Button variant="ghost" onClick={onClose}>Close</Button>
          <Button variant="outline" leftIcon={<Icon name="edit" size={14} />} onClick={() => onEdit(meeting)}>Edit</Button>
          <Button variant="danger" leftIcon={<Icon name="x" size={14} />} loading={cancelling} onClick={onCancel}>
            Cancel meeting
          </Button>
        </>
      ) : (
        <Button variant="ghost" onClick={onClose}>Close</Button>
      )}
    >
      {isLoading || !meeting ? (
        <div className="space-y-3">
          <Skeleton className="h-5 w-2/3" />
          <Skeleton className="h-5 w-1/2" />
          <Skeleton className="h-24" />
        </div>
      ) : (
        <div className="space-y-5">
          {/* When / where / organizer */}
          <div className="space-y-2.5">
            <DetailRow icon="clock" label="When">{formatRange(meeting.startsAt, meeting.endsAt)}</DetailRow>
            {meeting.location && <DetailRow icon="mapPin" label="Where">{meeting.location}</DetailRow>}
            {meeting.onlineUrl && (
              <DetailRow icon="globe" label="Online">
                <a href={meeting.onlineUrl} target="_blank" rel="noreferrer" title="Opens in a new tab" className="text-brand-600 hover:underline break-all">
                  {meeting.onlineUrl}
                </a>
              </DetailRow>
            )}
            <DetailRow icon="user" label="Organizer">
              {meeting.organizerName ?? "—"}{meeting.isOrganizer && <span className="text-ink-400"> (you)</span>}
            </DetailRow>
          </div>

          {meeting.description && (
            <div>
              <div className="section-title mb-1.5">Description</div>
              <p className="text-sm text-ink-700 leading-relaxed whitespace-pre-wrap">{meeting.description}</p>
            </div>
          )}

          {/* RSVP actions for an invited user */}
          {canRespond && (
            <div>
              <div className="section-title mb-2">
                <span className="inline-flex items-center gap-1">
                  Your response
                  <InfoHint title="Your RSVP" side="right">
                    Let the organizer know if you'll attend. <b>Tentative</b> means "maybe". You can change
                    your answer any time before the meeting.
                  </InfoHint>
                </span>
              </div>
              <div className="flex flex-wrap gap-2">
                {RSVP_CHOICES.map((c) => {
                  const active = meeting.myResponse === c.value;
                  return (
                    <Button
                      key={c.value}
                      size="sm"
                      variant={active ? (c.value === "Accepted" ? "success" : c.value === "Declined" ? "danger" : "primary") : "outline"}
                      loading={responding}
                      onClick={() => onRespond(c.value)}
                    >
                      {c.label}{active ? " ✓" : ""}
                    </Button>
                  );
                })}
              </div>
            </div>
          )}

          {/* Attendee roster */}
          <div>
            <div className="section-title mb-2">
              <span className="inline-flex items-center gap-1">
                Attendees <span className="text-ink-400">· {meeting.attendees.length}</span>
                <InfoHint title="RSVP status" side="top">
                  The tag beside each name is their reply — Accepted, Declined, Tentative (maybe), or
                  "No response" if they haven't answered yet.
                </InfoHint>
              </span>
            </div>
            {meeting.attendees.length === 0 ? (
              <p className="text-sm text-ink-400">No attendees invited.</p>
            ) : (
              <ul className="space-y-1.5">
                {meeting.attendees.map((a) => (
                  <li key={a.id} className="flex items-center justify-between gap-3 py-1">
                    <div className="flex items-center gap-2 min-w-0">
                      <Icon name={a.userId ? "user" : "mail"} size={14} className="text-ink-400 shrink-0" />
                      <span className="text-sm text-ink-800 truncate">{a.userName ?? a.email}</span>
                      {a.userName && <span className="text-xs text-ink-400 truncate hidden sm:inline">{a.email}</span>}
                    </div>
                    <Badge tone={RESPONSE_TONE[a.response]} variant="soft">{RESPONSE_LABEL[a.response]}</Badge>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}

function DetailRow({ icon, label, children }: { icon: string; label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-start gap-2.5">
      <Icon name={icon} size={15} className="text-ink-400 mt-0.5 shrink-0" />
      <div className="min-w-0">
        <div className="text-[11px] uppercase tracking-[0.06em] text-ink-400">{label}</div>
        <div className="text-sm text-ink-800">{children}</div>
      </div>
    </div>
  );
}
