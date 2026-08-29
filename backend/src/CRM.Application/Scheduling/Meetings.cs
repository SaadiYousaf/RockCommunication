using CRM.Domain.Constants;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Scheduling;

// ── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>One invitee on a meeting, with their resolved display name and RSVP.</summary>
public record MeetingAttendeeDto(
    Guid Id, Guid? UserId, string Email, string? UserName, AttendeeResponse Response);

/// <summary>A meeting as seen by the current user — includes whether they own it and their own RSVP.</summary>
public record MeetingDto(
    Guid Id, string Title, string? Description, DateTime StartsAt, DateTime EndsAt,
    string? Location, string? OnlineUrl, Guid OrganizerUserId, string? OrganizerName,
    MeetingStatus Status, bool IsOrganizer, AttendeeResponse? MyResponse,
    IReadOnlyList<MeetingAttendeeDto> Attendees, DateTime CreatedAt);

/// <summary>Writable fields, shared by create + update. Attendees are given as a mix of agency
/// user ids (they can RSVP in-app) and free-text external emails.</summary>
public record MeetingInput(
    string Title, string? Description, DateTime StartsAt, DateTime EndsAt,
    string? Location, string? OnlineUrl,
    IReadOnlyList<Guid>? AttendeeUserIds, IReadOnlyList<string>? AttendeeEmails);

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>Meetings I organize or am invited to that overlap the [From, To) window.</summary>
public record ListMeetingsQuery(DateTime From, DateTime To) : IRequest<IReadOnlyList<MeetingDto>>;
public record GetMeetingQuery(Guid Id) : IRequest<MeetingDto>;
public record CreateMeetingCommand(MeetingInput Input) : IRequest<MeetingDto>;
public record UpdateMeetingCommand(Guid Id, MeetingInput Input) : IRequest<MeetingDto>;
public record CancelMeetingCommand(Guid Id) : IRequest<MeetingDto>;
public record RespondToMeetingCommand(Guid MeetingId, AttendeeResponse Response) : IRequest<MeetingDto>;

// ── Validation ──────────────────────────────────────────────────────────────

public class MeetingInputValidator : AbstractValidator<MeetingInput>
{
    public MeetingInputValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(SchedulingDefaults.MaxTitleLength);
        RuleFor(x => x.Description).MaximumLength(SchedulingDefaults.MaxDescriptionLength);
        RuleFor(x => x.Location).MaximumLength(SchedulingDefaults.MaxLocationLength);
        RuleFor(x => x.OnlineUrl).MaximumLength(SchedulingDefaults.MaxOnlineUrlLength);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).WithMessage("End time must be after the start time.");
        // Upcoming Events and the attendee's calendar only ever show FUTURE meetings, so a meeting
        // booked in the past is invisible the moment it is created — which is exactly what happened:
        // a meeting was scheduled for 29 July while the date was 29 August, and then nobody could
        // find it. A small grace window allows for clock skew and a slow form submission.
        RuleFor(x => x.StartsAt)
            .GreaterThan(_ => DateTime.UtcNow.AddMinutes(-SchedulingDefaults.PastGraceMinutes))
            .WithMessage("That start time has already passed. Pick a date and time in the future.");
        // Null-safe combined count with no When-gate: previously the cap was skipped entirely when
        // either list was null, so a request with one huge list (and the other null) bypassed the
        // limit and could insert thousands of attendees + fire thousands of email invites.
        // Ternaries (not ?. — illegal in an expression-tree lambda) keep this null-safe.
        RuleFor(x => (x.AttendeeUserIds == null ? 0 : x.AttendeeUserIds.Count)
                   + (x.AttendeeEmails == null ? 0 : x.AttendeeEmails.Count))
            .LessThanOrEqualTo(SchedulingDefaults.MaxAttendees)
            .WithMessage($"A meeting can have at most {SchedulingDefaults.MaxAttendees} attendees.");
    }
}

public class CreateMeetingValidator : AbstractValidator<CreateMeetingCommand>
{ public CreateMeetingValidator() => RuleFor(x => x.Input).SetValidator(new MeetingInputValidator()); }

public class UpdateMeetingValidator : AbstractValidator<UpdateMeetingCommand>
{ public UpdateMeetingValidator() => RuleFor(x => x.Input).SetValidator(new MeetingInputValidator()); }

public class RespondToMeetingValidator : AbstractValidator<RespondToMeetingCommand>
{
    public RespondToMeetingValidator()
    {
        RuleFor(x => x.MeetingId).NotEmpty();
        RuleFor(x => x.Response).IsInEnum()
            .NotEqual(AttendeeResponse.Pending).WithMessage("Choose Accept, Decline or Tentative.");
    }
}

// ── Handlers ────────────────────────────────────────────────────────────────

public class MeetingHandlers :
    IRequestHandler<ListMeetingsQuery, IReadOnlyList<MeetingDto>>,
    IRequestHandler<GetMeetingQuery, MeetingDto>,
    IRequestHandler<CreateMeetingCommand, MeetingDto>,
    IRequestHandler<UpdateMeetingCommand, MeetingDto>,
    IRequestHandler<CancelMeetingCommand, MeetingDto>,
    IRequestHandler<RespondToMeetingCommand, MeetingDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly IMeetingInviteSender _invites;
    private readonly Intake.IIntakeNotifier _notifier;

    public MeetingHandlers(IApplicationDbContext db, ICurrentUser user, IIdentityService identity,
        IMeetingInviteSender invites, Intake.IIntakeNotifier notifier)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _invites = Guard.AgainstNull(invites);
        _notifier = Guard.AgainstNull(notifier);
    }

    /// <summary>
    /// Tell every INTERNAL attendee, in the app, that something happened to a meeting they are on.
    ///
    /// Scheduling only ever sent an email invite. Someone already signed in saw nothing at all — the
    /// bell said "You're all caught up" while a meeting sat on their calendar — because no
    /// notification row was ever written. Email-only also fails anyone whose address is stale.
    ///
    /// Never notifies the organiser about their own action, skips external email-only guests (they
    /// have no account to notify), and is best-effort: a notification problem must not fail the
    /// scheduling command itself.
    /// </summary>
    private async Task NotifyAttendeesAsync(Meeting meeting, Guid actingUserId, string title, string body, CancellationToken ct)
    {
        var recipients = meeting.Attendees
            .Where(a => a.UserId is { } uid && uid != actingUserId)
            .Select(a => a.UserId!.Value)
            .Distinct()
            .ToList();

        foreach (var userId in recipients)
        {
            try
            {
                await _notifier.NotifyUserAsync(meeting.AgencyId, userId, title, body,
                    AppConstants.QueueRoutes.Calendar, ct);
            }
            catch { /* graceful — a failed notice must never undo a scheduled meeting */ }
        }
    }

    /// <summary>"Tue 2 Sep at 2:00 PM" — a person-readable instant for notification text.</summary>
    private static string WhenText(DateTime utc) =>
        utc.ToString("ddd d MMM 'at' h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + " UTC";

    /// <summary>The authenticated caller's user id, or a 403 if there's no user context.</summary>
    private Guid CurrentUserId => _user.UserId ?? throw new ForbiddenAccessException();

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MeetingDto>> Handle(ListMeetingsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = CurrentUserId;

        // Agency isolation is enforced by the global tenant query filter; layer the
        // "meetings I organize OR am invited to" rule and the date-window overlap on top.
        var meetings = await _db.Meetings.AsNoTracking()
            .Include(m => m.Attendees)
            .Where(m => m.OrganizerUserId == me || m.Attendees.Any(a => a.UserId == me))
            .Where(m => m.StartsAt < request.To && m.EndsAt > request.From)
            .OrderBy(m => m.StartsAt)
            .ToListAsync(ct);

        var names = await UserNamesAsync(meetings, ct);
        return meetings.Select(m => Map(m, me, names)).ToList();
    }

    public async Task<MeetingDto> Handle(GetMeetingQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = CurrentUserId;
        var m = await LoadForReadAsync(request.Id, ct);
        EnsureParticipant(m, me);
        var names = await UserNamesAsync(new[] { m }, ct);
        return Map(m, me, names);
    }

    // ── Commands ────────────────────────────────────────────────────────────────

    public async Task<MeetingDto> Handle(CreateMeetingCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = CurrentUserId;
        var agencyId = _user.AgencyId ?? throw new ForbiddenAccessException();

        var meeting = new Meeting
        {
            AgencyId = agencyId,
            OrganizerUserId = me,
            Status = MeetingStatus.Scheduled,
        };
        ApplyScalars(meeting, request.Input);

        var directory = await AgencyUsersAsync(ct);
        meeting.Attendees = BuildAttendees(request.Input, agencyId, directory, existingByEmail: null);

        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync(ct);

        // Best-effort: never let an email problem fail the scheduling command.
        await TrySendAsync(() => _invites.SendInvitesAsync(meeting, OrganizerName(me, directory), ct));

        await NotifyAttendeesAsync(meeting, me,
            "You've been invited to a meeting",
            $"{meeting.Title} — {WhenText(meeting.StartsAt)}, organised by {OrganizerName(me, directory)}.", ct);

        return Map(meeting, me, NamesFrom(directory));
    }

    public async Task<MeetingDto> Handle(UpdateMeetingCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = CurrentUserId;
        var meeting = await LoadForWriteAsync(request.Id, ct);
        EnsureOrganizer(meeting, me);
        if (meeting.Status == MeetingStatus.Cancelled)
            throw new ConflictException("A cancelled meeting can't be edited.");

        // Capture what people were told BEFORE the edit, so we only notify on a real change.
        var previousStart = meeting.StartsAt;
        var priorUserIds = meeting.Attendees
            .Where(a => a.UserId is not null).Select(a => a.UserId!.Value).ToHashSet();

        ApplyScalars(meeting, request.Input);

        // Rebuild the attendee set, preserving each surviving invitee's RSVP (matched by email).
        var directory = await AgencyUsersAsync(ct);
        var existingByEmail = meeting.Attendees.ToDictionary(a => a.Email.Trim().ToLowerInvariant(), a => a.Response);

        // TWO SAVES, deliberately. Editing ANY meeting used to return a 500, because this replaced
        // the tracked navigation collection outright: that severs the association on a non-nullable
        // MeetingId, and the "removed" rows are only SOFT-deleted (the audit interceptor turns a
        // delete into IsDeleted = true), so re-adding the same person in the same SaveChanges also
        // races the unique index on (MeetingId, Email) filtered to IsDeleted = 0.
        // Retiring the old rows first, then inserting the new ones, avoids both.
        _db.MeetingAttendees.RemoveRange(meeting.Attendees.ToList());
        meeting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var rebuilt = BuildAttendees(request.Input, meeting.AgencyId, directory, existingByEmail);
        foreach (var a in rebuilt)
        {
            a.MeetingId = meeting.Id;
            _db.MeetingAttendees.Add(a);
        }
        await _db.SaveChangesAsync(ct);

        // The tracked collection still holds the retired rows; the response and the notifications
        // below must describe who is on the meeting NOW.
        meeting.Attendees = rebuilt;

        var organiser = OrganizerName(me, directory);

        // A moved meeting is the one edit everyone must be told about — it changes where they have
        // to be. Only when the start time actually changed, so renaming a meeting stays silent.
        if (meeting.StartsAt != previousStart)
            await NotifyAttendeesAsync(meeting, me, "A meeting was moved",
                $"{meeting.Title} is now {WhenText(meeting.StartsAt)} (was {WhenText(previousStart)}).", ct);

        // Someone added after the fact never received the original invitation.
        var added = meeting.Attendees
            .Where(a => a.UserId is { } uid && !priorUserIds.Contains(uid) && uid != me).ToList();
        foreach (var a in added)
        {
            try
            {
                await _notifier.NotifyUserAsync(meeting.AgencyId, a.UserId!.Value,
                    "You've been invited to a meeting",
                    $"{meeting.Title} — {WhenText(meeting.StartsAt)}, organised by {organiser}.",
                    AppConstants.QueueRoutes.Calendar, ct);
            }
            catch { /* graceful */ }
        }

        return Map(meeting, me, NamesFrom(directory));
    }

    public async Task<MeetingDto> Handle(CancelMeetingCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = CurrentUserId;
        var meeting = await LoadForWriteAsync(request.Id, ct);
        EnsureOrganizer(meeting, me);

        if (meeting.Status != MeetingStatus.Cancelled)
        {
            meeting.Status = MeetingStatus.Cancelled;
            meeting.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var directory = await AgencyUsersAsync(ct);
            await TrySendAsync(() => _invites.SendCancellationsAsync(meeting, OrganizerName(me, directory), ct));

            await NotifyAttendeesAsync(meeting, me, "A meeting was cancelled",
                $"{meeting.Title} on {WhenText(meeting.StartsAt)} is no longer happening.", ct);

            return Map(meeting, me, NamesFrom(directory));
        }

        var names = await UserNamesAsync(new[] { meeting }, ct);
        return Map(meeting, me, names);
    }

    public async Task<MeetingDto> Handle(RespondToMeetingCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = CurrentUserId;
        var meeting = await LoadForWriteAsync(request.MeetingId, ct);

        var mine = meeting.Attendees.FirstOrDefault(a => a.UserId == me)
            ?? throw new ForbiddenAccessException("You're not invited to this meeting.");
        if (meeting.Status == MeetingStatus.Cancelled)
            throw new ConflictException("This meeting has been cancelled.");

        mine.Response = request.Response;
        mine.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var names = await UserNamesAsync(new[] { meeting }, ct);
        return Map(meeting, me, names);
    }

    // ── Loading + access ─────────────────────────────────────────────────────────

    private async Task<Meeting> LoadForReadAsync(Guid id, CancellationToken ct) =>
        await _db.Meetings.AsNoTracking().Include(m => m.Attendees).FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException(nameof(Meeting), id);

    private async Task<Meeting> LoadForWriteAsync(Guid id, CancellationToken ct) =>
        await _db.Meetings.Include(m => m.Attendees).FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException(nameof(Meeting), id);

    private static void EnsureParticipant(Meeting m, Guid me)
    {
        if (m.OrganizerUserId != me && !m.Attendees.Any(a => a.UserId == me))
            throw new ForbiddenAccessException("You don't have access to this meeting.");
    }

    private static void EnsureOrganizer(Meeting m, Guid me)
    {
        if (m.OrganizerUserId != me)
            throw new ForbiddenAccessException("Only the organizer can change this meeting.");
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────────

    private static void ApplyScalars(Meeting m, MeetingInput i)
    {
        m.Title = i.Title.Trim();
        m.Description = Clean(i.Description);
        m.StartsAt = i.StartsAt;
        m.EndsAt = i.EndsAt;
        m.Location = Clean(i.Location);
        m.OnlineUrl = Clean(i.OnlineUrl);
    }

    /// <summary>Materialise the attendee rows from the input: de-duplicated by email, agency-user
    /// emails resolved from the directory, and any surviving RSVP carried over from a prior edit.</summary>
    private static List<MeetingAttendee> BuildAttendees(
        MeetingInput input, Guid agencyId,
        IReadOnlyList<UserRef> directory, IReadOnlyDictionary<string, AttendeeResponse>? existingByEmail)
    {
        var byId = directory.ToDictionary(u => u.Id);
        var byEmail = directory
            .Where(u => !string.IsNullOrWhiteSpace(u.Email))
            .GroupBy(u => u.Email!.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<MeetingAttendee>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(Guid? userId, string email)
        {
            var clean = email.Trim();
            if (clean.Length == 0) return;
            var key = clean.ToLowerInvariant();
            if (!seen.Add(key)) return; // de-dupe by address
            var response = existingByEmail != null && existingByEmail.TryGetValue(key, out var r)
                ? r : AttendeeResponse.Pending;
            result.Add(new MeetingAttendee
            {
                AgencyId = agencyId,
                UserId = userId,
                Email = clean,
                Response = response,
            });
        }

        foreach (var uid in input.AttendeeUserIds ?? Array.Empty<Guid>())
            if (byId.TryGetValue(uid, out var u) && !string.IsNullOrWhiteSpace(u.Email))
                Add(uid, u.Email!);

        foreach (var email in input.AttendeeEmails ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(email)) continue;
            // A free-text address that belongs to an agency user is linked so they can RSVP in-app.
            var match = byEmail.TryGetValue(email.Trim().ToLowerInvariant(), out var u) ? u : null;
            Add(match?.Id, email);
        }

        return result;
    }

    private static MeetingDto Map(Meeting m, Guid me, IReadOnlyDictionary<Guid, string> names)
    {
        var attendees = m.Attendees
            .OrderBy(a => a.Email)
            .Select(a => new MeetingAttendeeDto(
                a.Id, a.UserId, a.Email,
                a.UserId is { } uid && names.TryGetValue(uid, out var n) ? n : null,
                a.Response))
            .ToList();

        var isOrganizer = m.OrganizerUserId == me;
        AttendeeResponse? myResponse = m.Attendees.FirstOrDefault(a => a.UserId == me)?.Response;
        string? organizerName = names.TryGetValue(m.OrganizerUserId, out var on) ? on : null;

        return new MeetingDto(
            m.Id, m.Title, m.Description, m.StartsAt, m.EndsAt, m.Location, m.OnlineUrl,
            m.OrganizerUserId, organizerName, m.Status, isOrganizer, myResponse, attendees, m.CreatedAt);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ── Directory (agency user id/name/email) ─────────────────────────────────────

    private sealed record UserRef(Guid Id, string UserName, string? Email);

    private async Task<IReadOnlyList<UserRef>> AgencyUsersAsync(CancellationToken ct)
    {
        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        return users.Select(u => new UserRef(u.Id, u.UserName, u.Email)).ToList();
    }

    private static IReadOnlyDictionary<Guid, string> NamesFrom(IReadOnlyList<UserRef> directory) =>
        directory.ToDictionary(u => u.Id, u => u.UserName);

    private static string OrganizerName(Guid organizerId, IReadOnlyList<UserRef> directory) =>
        directory.FirstOrDefault(u => u.Id == organizerId)?.UserName ?? "The organizer";

    /// <summary>Resolve the user ids referenced by a set of meetings (organizers + linked attendees)
    /// to display names in one lookup.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Meeting> meetings, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        foreach (var m in meetings)
        {
            ids.Add(m.OrganizerUserId);
            foreach (var a in m.Attendees)
                if (a.UserId is { } uid) ids.Add(uid);
        }
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        var all = await _identity.ListUserNamesAsync(_user.AgencyId, ct);
        return all.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>Run a best-effort side effect (email) that must never fail the command.</summary>
    private static async Task TrySendAsync(Func<Task> action)
    {
        try { await action(); }
        catch { /* swallowed — invite emails are advisory (the sender also guards per-recipient) */ }
    }
}
