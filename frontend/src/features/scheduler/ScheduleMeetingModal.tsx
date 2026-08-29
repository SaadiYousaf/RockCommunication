import { useEffect, useMemo, useState } from "react";
import { SCHEDULER_MSG } from "./messages";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useCreateMeetingMutation, useUpdateMeetingMutation, useUserDirectoryQuery,
} from "../../shared/api/baseApi";
import { getErrorDetail } from "../../shared/api/apiError";
import type { Meeting } from "./types";
import {
  addMinutesToLocalInput, fromLocalInput, localInputAt, toLocalInput,
} from "./calendarUtils";
import {
  Badge, Button, Icon, InfoHint, Input, Modal, Select, Textarea, useToast,
} from "../../shared/ui";

const DEFAULT_HOUR = 9;
const DEFAULT_DURATION_MIN = 30;
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface FormState {
  title: string;
  description: string;
  start: string;      // datetime-local value (local wall clock)
  end: string;        // datetime-local value
  location: string;
  onlineUrl: string;
  attendeeUserIds: string[];
  attendeeEmails: string[];
}

/**
 * A new meeting always starts in the FUTURE.
 *
 * The form used to seed 9:00 AM on whichever day was clicked — so opening it from a past calendar
 * cell (or a month you were browsing) produced a start time that had already gone. The server
 * accepted it, and the meeting was then invisible in Upcoming Events and on the calendar, because
 * both only show what is still to come. That is exactly how a real meeting went missing: booked for
 * 29 July while the date was 29 August, then nobody could find it.
 *
 * If the chosen day's default slot has passed, fall forward to the next half hour from now.
 */
function blankForm(day: Date): FormState {
  const preferred = new Date(day.getFullYear(), day.getMonth(), day.getDate(), DEFAULT_HOUR, 0);
  const start = preferred.getTime() > Date.now()
    ? localInputAt(day, DEFAULT_HOUR, 0)
    : nextHalfHourInput();
  return {
    title: "", description: "", start, end: addMinutesToLocalInput(start, DEFAULT_DURATION_MIN),
    location: "", onlineUrl: "", attendeeUserIds: [], attendeeEmails: [],
  };
}

/** Right now, as a datetime-local value — the earliest a new meeting may start. */
function nowInput(): string {
  const d = new Date();
  return localInputAt(d, d.getHours(), d.getMinutes());
}

/** The next :00 or :30 from now, as a datetime-local value. */
function nextHalfHourInput(): string {
  const d = new Date();
  d.setSeconds(0, 0);
  d.setMinutes(d.getMinutes() <= 30 ? 30 : 60);
  return localInputAt(d, d.getHours(), d.getMinutes());
}

function fromMeeting(m: Meeting): FormState {
  return {
    title: m.title,
    description: m.description ?? "",
    start: toLocalInput(m.startsAt),
    end: toLocalInput(m.endsAt),
    location: m.location ?? "",
    onlineUrl: m.onlineUrl ?? "",
    attendeeUserIds: m.attendees.filter((a) => a.userId).map((a) => a.userId as string),
    attendeeEmails: m.attendees.filter((a) => !a.userId).map((a) => a.email),
  };
}

/**
 * Create / edit a meeting. Prefilled to a chosen day (from a calendar cell) when creating, or
 * hydrated from an existing meeting when editing. Attendees are a mix of agency users (picked
 * from the directory) and free-text external email guests.
 */
export function ScheduleMeetingModal({
  open, onClose, prefillDay, meeting,
}: {
  open: boolean;
  onClose: () => void;
  /** Day the calendar cell that was clicked represents (create mode). */
  prefillDay?: Date;
  /** When set, the modal edits this meeting instead of creating a new one. */
  meeting?: Meeting | null;
}) {
  const toast = useToast();
  const myId = useSelector((s: RootState) => s.auth.user?.id);
  const { data: directory } = useUserDirectoryQuery();
  const [create, { isLoading: creating }] = useCreateMeetingMutation();
  const [update, { isLoading: updating }] = useUpdateMeetingMutation();

  const [form, setForm] = useState<FormState>(() => blankForm(prefillDay ?? new Date()));
  const [emailDraft, setEmailDraft] = useState("");
  const [emailError, setEmailError] = useState<string>();

  // Re-seed the form whenever the modal (re)opens for a new target.
  useEffect(() => {
    if (!open) return;
    setForm(meeting ? fromMeeting(meeting) : blankForm(prefillDay ?? new Date()));
    setEmailDraft("");
    setEmailError(undefined);
  }, [open, meeting, prefillDay]);

  const editing = !!meeting;
  const set = <K extends keyof FormState>(k: K) => (v: FormState[K]) => setForm((f) => ({ ...f, [k]: v }));

  // Directory users available to add (exclude self + already-selected).
  const userById = useMemo(
    () => new Map((directory ?? []).map((u) => [u.id, u.userName] as const)),
    [directory],
  );
  const addableUsers = useMemo(
    () => (directory ?? [])
      .filter((u) => u.id !== myId && !form.attendeeUserIds.includes(u.id))
      .sort((a, b) => a.userName.localeCompare(b.userName)),
    [directory, myId, form.attendeeUserIds],
  );

  function addUser(id: string) {
    if (!id) return;
    setForm((f) => f.attendeeUserIds.includes(id) ? f : { ...f, attendeeUserIds: [...f.attendeeUserIds, id] });
  }
  function removeUser(id: string) {
    setForm((f) => ({ ...f, attendeeUserIds: f.attendeeUserIds.filter((x) => x !== id) }));
  }
  function addEmail() {
    const email = emailDraft.trim();
    if (!email) return;
    if (!EMAIL_RE.test(email)) { setEmailError("Enter a valid email address."); return; }
    if (form.attendeeEmails.some((e) => e.toLowerCase() === email.toLowerCase())) {
      setEmailError("That address is already invited.");
      return;
    }
    setForm((f) => ({ ...f, attendeeEmails: [...f.attendeeEmails, email] }));
    setEmailDraft("");
    setEmailError(undefined);
  }
  function removeEmail(email: string) {
    setForm((f) => ({ ...f, attendeeEmails: f.attendeeEmails.filter((e) => e !== email) }));
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.title.trim()) { toast.error("Title required", "Give the meeting a title."); return; }
    if (new Date(form.end) <= new Date(form.start)) {
      toast.error("Check the times", "The end time must be after the start time.");
      return;
    }
    const payload = {
      title: form.title.trim(),
      description: form.description.trim() || null,
      startsAt: fromLocalInput(form.start),
      endsAt: fromLocalInput(form.end),
      location: form.location.trim() || null,
      onlineUrl: form.onlineUrl.trim() || null,
      attendeeUserIds: form.attendeeUserIds,
      attendeeEmails: form.attendeeEmails,
    };
    try {
      if (editing && meeting) {
        await update({ id: meeting.id, ...payload }).unwrap();
        toast.success("Meeting updated");
      } else {
        await create(payload).unwrap();
        toast.success("Meeting scheduled", "Invites are on their way to your attendees.");
      }
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Check the fields and try again.");
    }
  }

  const attendeeCount = form.attendeeUserIds.length + form.attendeeEmails.length;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? "Edit meeting" : "Schedule a meeting"}
      description="Times are in your local time zone. Invitees get an email invite with an add-to-calendar link."
      size="xl"
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button form="meeting-form" type="submit" loading={creating || updating}>
          {editing ? "Save changes" : "Schedule"}
        </Button>
      </>}
    >
      <form id="meeting-form" onSubmit={submit} className="space-y-4">
        <Input
          label="Title" required value={form.title}
          onChange={(e) => set("title")(e.target.value)}
          placeholder="e.g. Weekly pipeline sync"
        />
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input
            label="Starts" type="datetime-local" required value={form.start}
            min={meeting ? undefined : nowInput()}
            hint={meeting ? undefined : SCHEDULER_MSG.startsInFutureHint}
            onChange={(e) => {
              const start = e.target.value;
              setForm((f) => {
                // Keep the end after the start: if it would slip past, push it out by the gap.
                const end = new Date(f.end) <= new Date(start)
                  ? addMinutesToLocalInput(start, DEFAULT_DURATION_MIN) : f.end;
                return { ...f, start, end };
              });
            }}
          />
          <Input
            label="Ends" type="datetime-local" required value={form.end}
            min={form.start || (meeting ? undefined : nowInput())}
            onChange={(e) => set("end")(e.target.value)}
          />
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Input
            label="Location" value={form.location}
            onChange={(e) => set("location")(e.target.value)}
            placeholder="Room, address…"
            leftIcon={<Icon name="mapPin" size={15} />}
          />
          <Input
            label="Online link" type="url" value={form.onlineUrl}
            onChange={(e) => set("onlineUrl")(e.target.value)}
            placeholder="https://…"
            leftIcon={<Icon name="globe" size={15} />}
          />
        </div>
        <Textarea
          label="Description" value={form.description}
          onChange={(e) => set("description")(e.target.value)}
          placeholder="Agenda, notes, dial-in details…"
        />

        {/* Attendees */}
        <div className="space-y-2">
          <div className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-[0.12em] text-ink-500 pb-1 border-b hairline">
            Attendees
            <InfoHint title="Attendees" side="top">
              Invite colleagues from your agency or add outside guests by email address. Everyone
              invited gets an email with a calendar invite; colleagues also see the meeting on their
              own calendar here. All invitees can RSVP.
            </InfoHint>
            {attendeeCount > 0 && <span className="text-ink-400 normal-case tracking-normal font-normal">· {attendeeCount}</span>}
          </div>

          {attendeeCount > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {form.attendeeUserIds.map((id) => (
                <Badge key={id} tone="brand" variant="soft" className="gap-1">
                  <Icon name="user" size={11} />
                  {userById.get(id) ?? "User"}
                  <button type="button" aria-label={`Remove ${userById.get(id) ?? "colleague"}`} title="Remove attendee" onClick={() => removeUser(id)} className="ml-0.5 hover:text-brand-900">
                    <Icon name="x" size={11} />
                  </button>
                </Badge>
              ))}
              {form.attendeeEmails.map((email) => (
                <Badge key={email} tone="neutral" variant="soft" className="gap-1">
                  <Icon name="mail" size={11} />
                  {email}
                  <button type="button" aria-label={`Remove ${email}`} title="Remove guest" onClick={() => removeEmail(email)} className="ml-0.5 hover:text-ink-900">
                    <Icon name="x" size={11} />
                  </button>
                </Badge>
              ))}
            </div>
          )}

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Select
              label="Invite a colleague"
              value=""
              onChange={(e) => { addUser(e.target.value); e.target.value = ""; }}
            >
              <option value="">Select a user…</option>
              {addableUsers.map((u) => <option key={u.id} value={u.id}>{u.userName}</option>)}
            </Select>
            <div className="flex flex-col gap-1.5">
              <label className="text-[12px] font-medium text-ink-700 leading-none">Invite by email</label>
              <div className="flex items-center gap-2">
                <Input
                  containerClassName="flex-1"
                  type="email"
                  value={emailDraft}
                  error={emailError}
                  placeholder="guest@example.com"
                  onChange={(e) => { setEmailDraft(e.target.value); setEmailError(undefined); }}
                  onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); addEmail(); } }}
                />
                <Button type="button" variant="outline" onClick={addEmail}>Add</Button>
              </div>
            </div>
          </div>
        </div>
      </form>
    </Modal>
  );
}
