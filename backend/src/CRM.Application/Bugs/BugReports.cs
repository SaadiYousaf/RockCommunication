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

namespace CRM.Application.Bugs;

// ── DTOs ────────────────────────────────────────────────────────────────────

public record BugReportDto(
    Guid Id, string Title, string Description, string Severity, string Status,
    Guid ReporterUserId, string ReporterName, Guid? AssignedToUserId, string? AssignedToName,
    string? PageUrl, string? Resolution, DateTime CreatedAt, DateTime? UpdatedAt, bool CanManage);

public record BugActivityDto(
    Guid Id, Guid UserId, string UserName, string? FromStatus, string? ToStatus, string? Comment, DateTime CreatedAt);

public record BugReportDetailDto(BugReportDto Bug, IReadOnlyList<BugActivityDto> Activity);

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>Anyone signed in can file a bug. PageUrl/UserAgent are captured client-side for repro.</summary>
public record CreateBugReportCommand(string Title, string Description, BugSeverity Severity, string? PageUrl, string? UserAgent)
    : IRequest<BugReportDto>;

/// <summary>List bugs in the caller's agency. <paramref name="Scope"/> "mine" limits to the caller's own reports.</summary>
public record ListBugReportsQuery(BugStatus? Status = null, string? Scope = null) : IRequest<IReadOnlyList<BugReportDto>>;
public record GetBugReportQuery(Guid Id) : IRequest<BugReportDetailDto>;

/// <summary>Move a bug to a new status (triager only). Optional resolution note is recorded on the transition.</summary>
public record UpdateBugStatusCommand(Guid Id, BugStatus Status, string? Resolution) : IRequest<BugReportDto>;
public record AssignBugCommand(Guid Id, Guid? AssignedToUserId) : IRequest<BugReportDto>;
public record CommentBugCommand(Guid Id, string Comment) : IRequest<BugActivityDto>;

// ── Validation ──────────────────────────────────────────────────────────────

public class CreateBugReportValidator : AbstractValidator<CreateBugReportCommand>
{
    public CreateBugReportValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(BugConstants.MaxTitleLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(BugConstants.MaxDescriptionLength);
        RuleFor(x => x.Severity).IsInEnum();
        RuleFor(x => x.PageUrl).MaximumLength(BugConstants.MaxUrlLength);
        RuleFor(x => x.UserAgent).MaximumLength(BugConstants.MaxUserAgentLength);
    }
}
public class UpdateBugStatusValidator : AbstractValidator<UpdateBugStatusCommand>
{
    public UpdateBugStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Resolution).MaximumLength(BugConstants.MaxCommentLength);
    }
}
public class CommentBugValidator : AbstractValidator<CommentBugCommand>
{
    public CommentBugValidator() => RuleFor(x => x.Comment).NotEmpty().MaximumLength(BugConstants.MaxCommentLength);
}

public static class BugConstants
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 5000;
    public const int MaxCommentLength = 2000;
    public const int MaxUrlLength = 500;
    public const int MaxUserAgentLength = 400;
}

/// <summary>Who may triage (change status / assign): Admins and the platform SuperAdmin.</summary>
public static class BugAccess
{
    public static bool CanManage(ICurrentUser user)
        => user.IsSuperAdmin || user.Roles.Contains(DomainRoles.Admin);
}

// ── Handlers ────────────────────────────────────────────────────────────────

public class BugReportHandlers :
    IRequestHandler<CreateBugReportCommand, BugReportDto>,
    IRequestHandler<ListBugReportsQuery, IReadOnlyList<BugReportDto>>,
    IRequestHandler<GetBugReportQuery, BugReportDetailDto>,
    IRequestHandler<UpdateBugStatusCommand, BugReportDto>,
    IRequestHandler<AssignBugCommand, BugReportDto>,
    IRequestHandler<CommentBugCommand, BugActivityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly INotificationDispatcher _notify;

    public BugReportHandlers(IApplicationDbContext db, ICurrentUser user, IIdentityService identity, INotificationDispatcher notify)
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

    public async Task<BugReportDto> Handle(CreateBugReportCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        var bug = new BugReport
        {
            AgencyId = aid,
            ReporterUserId = uid,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Severity = request.Severity,
            Status = BugStatus.New,
            PageUrl = Clean(request.PageUrl, BugConstants.MaxUrlLength),
            UserAgent = Clean(request.UserAgent, BugConstants.MaxUserAgentLength),
        };
        _db.BugReports.Add(bug);
        await _db.SaveChangesAsync(ct);

        var names = await _identity.ListUserNamesAsync(aid, ct);
        return Map(bug, names, _user);
    }

    public async Task<IReadOnlyList<BugReportDto>> Handle(ListBugReportsQuery request, CancellationToken ct)
    {
        var (uid, aid) = Ctx();
        var q = _db.BugReports.AsNoTracking().AsQueryable();
        if (request.Status is { } s) q = q.Where(b => b.Status == s);
        if (string.Equals(request.Scope, "mine", StringComparison.OrdinalIgnoreCase))
            q = q.Where(b => b.ReporterUserId == uid);
        var bugs = await q.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync(ct);
        var names = await _identity.ListUserNamesAsync(aid, ct);
        return bugs.Select(b => Map(b, names, _user)).ToList();
    }

    public async Task<BugReportDetailDto> Handle(GetBugReportQuery request, CancellationToken ct)
    {
        var (_, aid) = Ctx();
        var bug = await _db.BugReports.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(BugReport), request.Id);
        var activity = await _db.BugReportActivities.AsNoTracking()
            .Where(a => a.BugReportId == bug.Id).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var names = await _identity.ListUserNamesAsync(aid, ct);
        return new BugReportDetailDto(
            Map(bug, names, _user),
            activity.Select(a => new BugActivityDto(a.Id, a.UserId, Name(names, a.UserId),
                a.FromStatus?.ToString(), a.ToStatus?.ToString(), a.Comment, a.CreatedAt)).ToList());
    }

    public async Task<BugReportDto> Handle(UpdateBugStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        if (!BugAccess.CanManage(_user)) throw new ForbiddenAccessException();
        var bug = await _db.BugReports.FirstOrDefaultAsync(b => b.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(BugReport), request.Id);

        if (bug.Status != request.Status)
        {
            _db.BugReportActivities.Add(new BugReportActivity
            {
                AgencyId = bug.AgencyId, BugReportId = bug.Id, UserId = uid,
                FromStatus = bug.Status, ToStatus = request.Status,
                Comment = Clean(request.Resolution, BugConstants.MaxCommentLength),
            });
            var from = bug.Status;
            bug.Status = request.Status;
            if (!string.IsNullOrWhiteSpace(request.Resolution)) bug.Resolution = request.Resolution.Trim();
            bug.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await NotifyReporterAsync(bug, from, ct);
        }

        var names = await _identity.ListUserNamesAsync(aid, ct);
        return Map(bug, names, _user);
    }

    public async Task<BugReportDto> Handle(AssignBugCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        if (!BugAccess.CanManage(_user)) throw new ForbiddenAccessException();
        var bug = await _db.BugReports.FirstOrDefaultAsync(b => b.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(BugReport), request.Id);
        bug.AssignedToUserId = request.AssignedToUserId;
        bug.UpdatedAt = DateTime.UtcNow;
        var names = await _identity.ListUserNamesAsync(aid, ct);
        _db.BugReportActivities.Add(new BugReportActivity
        {
            AgencyId = bug.AgencyId, BugReportId = bug.Id, UserId = uid,
            Comment = request.AssignedToUserId is { } a ? $"Assigned to {Name(names, a)}" : "Unassigned",
        });
        await _db.SaveChangesAsync(ct);
        return Map(bug, names, _user);
    }

    public async Task<BugActivityDto> Handle(CommentBugCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        var bug = await _db.BugReports.FirstOrDefaultAsync(b => b.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(BugReport), request.Id);
        var activity = new BugReportActivity
        {
            AgencyId = bug.AgencyId, BugReportId = bug.Id, UserId = uid, Comment = request.Comment.Trim(),
        };
        _db.BugReportActivities.Add(activity);
        await _db.SaveChangesAsync(ct);
        var names = await _identity.ListUserNamesAsync(aid, ct);
        return new BugActivityDto(activity.Id, uid, Name(names, uid), null, null, activity.Comment, activity.CreatedAt);
    }

    // Best-effort: tell the reporter their bug moved (never let a notification failure fail the transition).
    private async Task NotifyReporterAsync(BugReport bug, BugStatus from, CancellationToken ct)
    {
        if (bug.ReporterUserId == _user.UserId) return;   // don't notify yourself
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(bug.AgencyId, bug.ReporterUserId,
                    "Bug update",
                    $"Your report \"{Trim(bug.Title, 60)}\" moved from {BugStatusLabel(from)} to {BugStatusLabel(bug.Status)}.",
                    "/bugs"),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* graceful: the transition already succeeded */ }
    }

    private BugReportDto Map(BugReport b, IReadOnlyDictionary<Guid, string> names, ICurrentUser user) => new(
        b.Id, b.Title, b.Description, b.Severity.ToString(), b.Status.ToString(),
        b.ReporterUserId, Name(names, b.ReporterUserId),
        b.AssignedToUserId, b.AssignedToUserId is { } a ? Name(names, a) : null,
        b.PageUrl, b.Resolution, b.CreatedAt, b.UpdatedAt, BugAccess.CanManage(user));

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid id)
        => names.TryGetValue(id, out var n) ? n : "Someone";

    private static string? Clean(string? v, int max)
        => string.IsNullOrWhiteSpace(v) ? null : (v.Trim().Length > max ? v.Trim()[..max] : v.Trim());

    private static string Trim(string v, int max) => v.Length <= max ? v : v[..max] + "…";

    private static string BugStatusLabel(BugStatus s) => s switch
    {
        BugStatus.InProgress => "In Progress",
        BugStatus.WontFix => "Won't Fix",
        BugStatus.CannotReproduce => "Can't Reproduce",
        _ => s.ToString(),
    };
}
