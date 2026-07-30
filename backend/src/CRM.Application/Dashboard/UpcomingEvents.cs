using CRM.Application.Common.Interfaces;
using CRM.Application.Hr;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Dashboard;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>
/// One item in the dashboard "Upcoming events" widget. <see cref="Type"/> drives the icon/tone
/// on the client; <see cref="WhenUtc"/> is the moment the event happens (UTC).
/// </summary>
public record UpcomingEventDto(string Type, string Title, string? Subtitle, DateTime WhenUtc);

/// <summary>Event type discriminators shared with the client (no magic strings in handlers).</summary>
public static class UpcomingEventTypes
{
    public const string Callback = "callback";
    public const string Training = "training";
    public const string Birthday = "birthday";
}

/// <summary>
/// A unified feed of what's coming up for the signed-in user, for the dashboard widget:
/// their own scheduled callbacks (everyone), plus staff birthdays and candidate trainings
/// (management/HR only — those read HR records). Soonest first, capped for a compact widget.
/// </summary>
public record UpcomingEventsQuery(int Days = 14) : IRequest<IReadOnlyList<UpcomingEventDto>>;

// ── Handler ───────────────────────────────────────────────────────────────────

public class UpcomingEventsHandler : IRequestHandler<UpcomingEventsQuery, IReadOnlyList<UpcomingEventDto>>
{
    private const int PerSourceCap = 20;
    private const int TotalCap = 12;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public UpcomingEventsHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<UpcomingEventDto>> Handle(UpcomingEventsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var days = Math.Clamp(request.Days, 1, 60);
        var now = DateTime.UtcNow;
        var until = now.AddDays(days);
        var events = new List<UpcomingEventDto>();

        await AddCallbacksAsync(events, now, until, ct);
        if (HrAccess.IsHr(_user))
        {
            await AddTrainingsAsync(events, now, until, ct);
            await AddBirthdaysAsync(events, now, until, ct);
        }

        return events.OrderBy(e => e.WhenUtc).Take(TotalCap).ToList();
    }

    /// <summary>The caller's own upcoming, not-yet-completed callbacks (tenant-scoped by the query filter).</summary>
    private async Task AddCallbacksAsync(List<UpcomingEventDto> events, DateTime now, DateTime until, CancellationToken ct)
    {
        if (_user.UserId is not { } uid) return;

        var callbacks = await _db.ScheduledCallbacks.AsNoTracking()
            .Where(c => !c.Completed && c.AssignedUserId == uid && c.ScheduledFor >= now && c.ScheduledFor <= until)
            .OrderBy(c => c.ScheduledFor)
            .Take(PerSourceCap)
            .Select(c => new { c.LeadId, c.ScheduledFor, c.Reason })
            .ToListAsync(ct);
        if (callbacks.Count == 0) return;

        var leadIds = callbacks.Select(c => c.LeadId).Distinct().ToList();
        var leadNames = await _db.Leads.AsNoTracking()
            .Where(l => leadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.FirstName, l.LastName })
            .ToDictionaryAsync(l => l.Id, l => $"{l.FirstName} {l.LastName}".Trim(), ct);

        foreach (var c in callbacks)
        {
            var name = leadNames.TryGetValue(c.LeadId, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "a lead";
            events.Add(new UpcomingEventDto(UpcomingEventTypes.Callback, $"Callback · {name}", c.Reason, c.ScheduledFor));
        }
    }

    /// <summary>Candidate training sessions booked within the window.</summary>
    private async Task AddTrainingsAsync(List<UpcomingEventDto> events, DateTime now, DateTime until, CancellationToken ct)
    {
        var trainings = await _db.Interviews.AsNoTracking()
            .Where(i => i.TrainingScheduledAt != null && i.TrainingScheduledAt >= now && i.TrainingScheduledAt <= until)
            .OrderBy(i => i.TrainingScheduledAt)
            .Take(PerSourceCap)
            .Select(i => new { i.CandidateName, i.PositionOffered, When = i.TrainingScheduledAt!.Value })
            .ToListAsync(ct);

        foreach (var t in trainings)
        {
            var subtitle = string.IsNullOrWhiteSpace(t.PositionOffered) ? "Training scheduled" : $"Training · {t.PositionOffered}";
            events.Add(new UpcomingEventDto(UpcomingEventTypes.Training, t.CandidateName, subtitle, t.When));
        }
    }

    /// <summary>
    /// Staff birthdays landing within the window. The next-birthday date can't be expressed in a
    /// provider-translatable query (month/day rollover), so we project the DOBs and match in memory.
    /// </summary>
    private async Task AddBirthdaysAsync(List<UpcomingEventDto> events, DateTime now, DateTime until, CancellationToken ct)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.DateOfBirth != null)
            .Select(e => new { e.FullName, Dob = e.DateOfBirth!.Value })
            .ToListAsync(ct);

        // Compare against the start of today (birthdays are whole-day events with no clock time),
        // so a birthday landing *today* still counts even though `now` is already past midnight.
        var today = now.Date;
        foreach (var e in employees)
        {
            var next = NextBirthday(e.Dob, now);
            if (next >= today && next <= until)
                events.Add(new UpcomingEventDto(UpcomingEventTypes.Birthday, e.FullName, "Birthday", next));
        }
    }

    /// <summary>
    /// The next occurrence of <paramref name="dob"/>'s month/day at or after <paramref name="now"/>,
    /// clamping a 29-Feb birthday to 28-Feb in common years so it never throws.
    /// </summary>
    private static DateTime NextBirthday(DateTime dob, DateTime now)
    {
        int Day(int year) => dob.Month == 2 && dob.Day == 29 && !DateTime.IsLeapYear(year) ? 28 : dob.Day;
        var thisYear = new DateTime(now.Year, dob.Month, Day(now.Year), 0, 0, 0, DateTimeKind.Utc);
        return thisYear >= now.Date ? thisYear : new DateTime(now.Year + 1, dob.Month, Day(now.Year + 1), 0, 0, 0, DateTimeKind.Utc);
    }
}
