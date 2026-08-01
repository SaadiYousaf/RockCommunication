using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// A calendar meeting — the MS-Teams-style scheduler unit. Agency-scoped (the global tenant
/// query filter isolates reads by <see cref="TenantEntity.AgencyId"/>); on top of that, handlers
/// only surface meetings the caller organizes or is invited to. Times are stored in UTC.
/// </summary>
public class Meeting : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Start instant, UTC.</summary>
    public DateTime StartsAt { get; set; }
    /// <summary>End instant, UTC. Always strictly after <see cref="StartsAt"/>.</summary>
    public DateTime EndsAt { get; set; }

    /// <summary>Physical location (room / address), or null for an online-only meeting.</summary>
    public string? Location { get; set; }
    /// <summary>Join link for an online meeting (Teams/Zoom/Meet/…), or null.</summary>
    public string? OnlineUrl { get; set; }

    /// <summary>The user login that created (owns) the meeting — the only one who may edit or cancel it.</summary>
    public Guid OrganizerUserId { get; set; }

    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;

    public ICollection<MeetingAttendee> Attendees { get; set; } = new List<MeetingAttendee>();
}

/// <summary>
/// One invitee on a <see cref="Meeting"/>. <see cref="UserId"/> is set when the invitee is an
/// agency user login (so they can RSVP in-app); it is null for a purely external email guest.
/// <see cref="Email"/> is always populated — it is the address the invite is delivered to.
/// </summary>
public class MeetingAttendee : TenantEntity
{
    public Guid MeetingId { get; set; }

    /// <summary>The invited agency user, or null for an external (email-only) guest.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Address the invite/cancellation is emailed to (never null).</summary>
    public string Email { get; set; } = string.Empty;

    public AttendeeResponse Response { get; set; } = AttendeeResponse.Pending;
}
