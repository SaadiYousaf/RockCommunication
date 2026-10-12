using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Hr;

// ── DTOs ────────────────────────────────────────────────────────────────────

public record ShiftReflectionDto(
    Guid Id, Guid UserId, string UserName, DateTime ShiftDate,
    string? Learned, bool? DidJustice, string? JusticeNote,
    DateTime CreatedAt, DateTime? UpdatedAt);

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>
/// Files (or updates) the caller's reflection for today. Every field is optional — this is sent
/// when somebody answers at sign-out, and not sent at all when they press Skip.
/// </summary>
public record SubmitShiftReflectionCommand(string? Learned, bool? DidJustice, string? JusticeNote)
    : IRequest<ShiftReflectionDto>;

/// <summary>
/// Reflections for review. Defaults to the last fortnight across the caller's whole scope;
/// <paramref name="Scope"/> "mine" returns the caller's own history and needs no privilege.
/// </summary>
public record ListShiftReflectionsQuery(
    DateTime? From = null, DateTime? To = null, Guid? UserId = null, string? Scope = null)
    : IRequest<IReadOnlyList<ShiftReflectionDto>>;

// ── Validation ──────────────────────────────────────────────────────────────

public class SubmitShiftReflectionValidator : AbstractValidator<SubmitShiftReflectionCommand>
{
    public SubmitShiftReflectionValidator()
    {
        RuleFor(x => x.Learned).MaximumLength(ShiftReflectionConstants.MaxLearnedLength);
        RuleFor(x => x.JusticeNote).MaximumLength(ShiftReflectionConstants.MaxNoteLength);

        // Nothing at all to record is a client bug, not a user action: pressing Skip sends no
        // request. Rejecting it keeps empty rows out of a manager's review list.
        RuleFor(x => x).Must(x =>
                !string.IsNullOrWhiteSpace(x.Learned)
                || x.DidJustice is not null
                || !string.IsNullOrWhiteSpace(x.JusticeNote))
            .WithMessage("Answer at least one of the two questions, or skip them.");
    }
}

public static class ShiftReflectionConstants
{
    public const int MaxLearnedLength = 4000;
    public const int MaxNoteLength = 2000;
    /// <summary>How far back the review list reaches when no window is given.</summary>
    public const int DefaultWindowDays = 14;
    public const int MaxRows = 1000;
}

/// <summary>
/// Who may read other people's reflections: the platform operator, agency leadership and the
/// floor roles that run a shift. Everyone else can read only their own, which they always can.
/// </summary>
public static class ShiftReflectionAccess
{
    private static readonly string[] Reviewers =
    [
        DomainRoles.Admin, DomainRoles.CEO, DomainRoles.CallCenterAdmin,
        DomainRoles.ProjectManager, DomainRoles.ProgramManager,
        DomainRoles.TeamLead, DomainRoles.QAManager,
    ];

    public static bool CanReview(ICurrentUser user)
        => Guard.AgainstNull(user).IsSuperAdmin || user.Roles.Any(Reviewers.Contains);
}

// ── Handlers ────────────────────────────────────────────────────────────────

public class ShiftReflectionHandlers :
    IRequestHandler<SubmitShiftReflectionCommand, ShiftReflectionDto>,
    IRequestHandler<ListShiftReflectionsQuery, IReadOnlyList<ShiftReflectionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly INotificationDispatcher _notify;

    public ShiftReflectionHandlers(
        IApplicationDbContext db, ICurrentUser user, IIdentityService identity, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _notify = Guard.AgainstNull(notify);
    }

    private (Guid userId, Guid agencyId) Ctx()
    {
        if (_user.UserId is not { } uid) throw new ForbiddenAccessException();
        return (uid, _user.AgencyId ?? Guid.Empty);
    }

    public async Task<ShiftReflectionDto> Handle(SubmitShiftReflectionCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        var today = DateTime.UtcNow.Date;

        var learned = Clean(request.Learned, ShiftReflectionConstants.MaxLearnedLength);
        var note = Clean(request.JusticeNote, ShiftReflectionConstants.MaxNoteLength);

        var row = await _db.ShiftReflections
            .FirstOrDefaultAsync(r => r.UserId == uid && r.ShiftDate == today, ct);

        var isNew = row is null;
        if (row is null)
        {
            row = new ShiftReflection
            {
                AgencyId = aid,
                UserId = uid,
                ShiftDate = today,
                CallCenterId = _user.CallCenterId,
            };
            _db.ShiftReflections.Add(row);
        }
        else
        {
            row.UpdatedAt = DateTime.UtcNow;
        }

        // Only fields they actually filled in are written. Signing out a second time the same day
        // and leaving a box empty must not wipe what they wrote earlier.
        if (learned is not null) row.Learned = learned;
        if (request.DidJustice is not null) row.DidJustice = request.DidJustice;
        if (note is not null) row.JusticeNote = note;

        await _db.SaveChangesAsync(ct);

        // A "no" is the one answer worth interrupting somebody for — it is a person telling their
        // management the day went badly, and it is useless if it sits in a list nobody opens.
        // A "yes" notifies nobody: on a fifty-seat floor that is fifty notifications an evening,
        // which trains everyone to ignore the bell.
        if (isNew && request.DidJustice == false) await AlertLeadershipAsync(aid, uid, row, ct);

        var names = await _identity.ListUserNamesAsync(aid, ct);
        return Map(row, names);
    }

    public async Task<IReadOnlyList<ShiftReflectionDto>> Handle(ListShiftReflectionsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();

        var mineOnly = string.Equals(request.Scope, "mine", StringComparison.OrdinalIgnoreCase)
            || !ShiftReflectionAccess.CanReview(_user);

        var from = (request.From ?? DateTime.UtcNow.Date.AddDays(-ShiftReflectionConstants.DefaultWindowDays)).Date;
        var to = (request.To ?? DateTime.UtcNow.Date).Date;

        var q = _db.ShiftReflections.AsNoTracking()
            .Where(r => r.ShiftDate >= from && r.ShiftDate <= to);

        if (mineOnly) q = q.Where(r => r.UserId == uid);
        else if (request.UserId is { } who) q = q.Where(r => r.UserId == who);

        var rows = await q
            .OrderByDescending(r => r.ShiftDate).ThenByDescending(r => r.CreatedAt)
            .Take(ShiftReflectionConstants.MaxRows)
            .ToListAsync(ct);

        var names = await _identity.ListUserNamesAsync(aid, ct);
        return rows.Select(r => Map(r, names)).ToList();
    }

    private async Task AlertLeadershipAsync(Guid agencyId, Guid authorId, ShiftReflection row, CancellationToken ct)
    {
        try
        {
            var users = await _identity.ListUsersAsync(agencyId, ct);
            var names = (IReadOnlyDictionary<Guid, string>)users.ToDictionary(u => u.Id, u => u.UserName);
            var author = Name(names, authorId);
            var body = string.IsNullOrWhiteSpace(row.JusticeNote)
                ? $"{author} signed out saying they don't feel they did justice with their job today."
                : $"{author} signed out saying they don't feel they did justice with their job today: \"{Trim(row.JusticeNote!, 160)}\"";

            foreach (var lead in users.Where(u =>
                         u.Roles.Contains(DomainRoles.Admin) || u.Roles.Contains(DomainRoles.CEO)))
            {
                if (lead.Id == authorId) continue;
                await _notify.DispatchAsync(
                    new NotificationPayload(agencyId, lead.Id, "End-of-day check-in needs a look", body, "/hr/reflections"),
                    new[] { NotificationChannelType.InApp }, ct);
            }
        }
        catch { /* graceful: a reflection must still save when the bell is broken */ }
    }

    private static ShiftReflectionDto Map(ShiftReflection r, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.UserId, Name(names, r.UserId), r.ShiftDate,
        r.Learned, r.DidJustice, r.JusticeNote, r.CreatedAt, r.UpdatedAt);

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid id)
        => names.TryGetValue(id, out var n) ? n : "Someone";

    private static string? Clean(string? v, int max)
        => string.IsNullOrWhiteSpace(v) ? null : (v.Trim().Length > max ? v.Trim()[..max] : v.Trim());

    private static string Trim(string v, int max) => v.Length <= max ? v : v[..max] + "…";
}
