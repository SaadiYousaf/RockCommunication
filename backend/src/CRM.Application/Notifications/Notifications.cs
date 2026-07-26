using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Notifications;

// ─────────────────────────────────────────────────────────────────────────────
// A user's in-app notification inbox. The InAppNotificationChannel already PERSISTS
// a Notification row (and live-pushes via SignalR); these queries/commands are the
// missing READ side so the bell can show durable, unread-counted notifications —
// not just a transient toast the user misses if their tab was closed.
// ─────────────────────────────────────────────────────────────────────────────

public record NotificationDto(Guid Id, string Title, string Body, string? Url, bool IsRead, DateTime CreatedAt);

public record ListMyNotificationsQuery(int Take = 30, bool UnreadOnly = false) : IRequest<IReadOnlyList<NotificationDto>>;
public record MyUnreadNotificationCountQuery() : IRequest<int>;
public record MarkNotificationReadCommand(Guid Id) : IRequest<Unit>;
public record MarkAllNotificationsReadCommand() : IRequest<Unit>;

public class NotificationsHandler :
    IRequestHandler<ListMyNotificationsQuery, IReadOnlyList<NotificationDto>>,
    IRequestHandler<MyUnreadNotificationCountQuery, int>,
    IRequestHandler<MarkNotificationReadCommand, Unit>,
    IRequestHandler<MarkAllNotificationsReadCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public NotificationsHandler(IApplicationDbContext db, ICurrentUser user)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    /// <summary>
    /// A notification belongs to a USER regardless of which agency's work it is about — a central
    /// Submission Agent (agency = Guid.Empty) receives alerts stamped with the sale's real agency.
    /// Scope strictly by UserId and bypass the tenant filter so those cross-agency alerts aren't
    /// hidden; it stays safe because the only predicate is the caller's own UserId.
    /// </summary>
    private IQueryable<Notification> Mine() =>
        _db.Notifications.IgnoreQueryFilters().Where(n => !n.IsDeleted && n.UserId == _user.UserId);

    public async Task<IReadOnlyList<NotificationDto>> Handle(ListMyNotificationsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) return Array.Empty<NotificationDto>();
        var q = Mine();
        if (request.UnreadOnly) q = q.Where(n => !n.IsRead);
        return await q.OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.Url, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<int> Handle(MyUnreadNotificationCountQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) return 0;
        return await Mine().CountAsync(n => !n.IsRead, ct);
    }

    public async Task<Unit> Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var n = await Mine().FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (n is { IsRead: false }) { n.IsRead = true; await _db.SaveChangesAsync(ct); }
        return Unit.Value;
    }

    public async Task<Unit> Handle(MarkAllNotificationsReadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) return Unit.Value;
        var unread = await Mine().Where(n => !n.IsRead).ToListAsync(ct);
        foreach (var n in unread) n.IsRead = true;
        if (unread.Count > 0) await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
