using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Chat;

public record UnreadInfo(Guid RoomId, int UnreadCount, DateTime? LastReadAt);

public record UnreadCountsQuery() : IRequest<IReadOnlyList<UnreadInfo>>;

public class UnreadCountsHandler : IRequestHandler<UnreadCountsQuery, IReadOnlyList<UnreadInfo>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public UnreadCountsHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<UnreadInfo>> Handle(UnreadCountsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var memberships = await _db.ChatRoomMembers
            .Where(m => m.UserId == _user.UserId)
            .Select(m => new { m.RoomId, m.LastReadAt })
            .ToListAsync(ct);

        var roomIds = memberships.Select(m => m.RoomId).ToList();
        var msgCounts = await _db.ChatMessages
            .Where(m => roomIds.Contains(m.RoomId))
            .GroupBy(m => m.RoomId)
            .Select(g => new { RoomId = g.Key, Messages = g.Select(x => new { x.SentAt, x.SenderUserId }).ToList() })
            .ToListAsync(ct);

        var lookup = msgCounts.ToDictionary(x => x.RoomId);

        return memberships.Select(m =>
        {
            if (!lookup.TryGetValue(m.RoomId, out var data)) return new UnreadInfo(m.RoomId, 0, m.LastReadAt);
            var since = m.LastReadAt ?? DateTime.MinValue;
            var count = data.Messages.Count(x => x.SentAt > since && x.SenderUserId != _user.UserId);
            return new UnreadInfo(m.RoomId, count, m.LastReadAt);
        }).ToList();
    }
}

public record MarkRoomReadCommand(Guid RoomId) : IRequest<Unit>;

public class MarkRoomReadHandler : IRequestHandler<MarkRoomReadCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IChatBroadcaster _broadcaster;
    public MarkRoomReadHandler(IApplicationDbContext db, ICurrentUser user, IChatBroadcaster broadcaster)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _broadcaster = Guard.AgainstNull(broadcaster); }

    public async Task<Unit> Handle(MarkRoomReadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        var member = await _db.ChatRoomMembers.FirstOrDefaultAsync(
            m => m.RoomId == request.RoomId && m.UserId == _user.UserId, ct)
            ?? throw new ForbiddenAccessException();
        var now = DateTime.UtcNow;
        member.LastReadAt = now;
        await _db.SaveChangesAsync(ct);
        await _broadcaster.BroadcastRoomReadAsync(request.RoomId, _user.UserId.Value, now, ct);
        return Unit.Value;
    }
}
