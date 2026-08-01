namespace CRM.Domain.Enums;

/// <summary>Lifecycle of a scheduled meeting.</summary>
public enum MeetingStatus
{
    /// <summary>The meeting is on the calendar and expected to happen.</summary>
    Scheduled = 0,
    /// <summary>The organizer called the meeting off — invitees are notified.</summary>
    Cancelled = 1,
    /// <summary>The meeting has taken place.</summary>
    Completed = 2,
}

/// <summary>An invitee's RSVP to a meeting invitation.</summary>
public enum AttendeeResponse
{
    /// <summary>No response yet (the state every fresh invite starts in).</summary>
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    /// <summary>"Maybe" — the invitee might attend.</summary>
    Tentative = 3,
}
