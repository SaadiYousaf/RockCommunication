using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Chat;

// ─────────────────────────────────────────────────────────────────────────────
// SuperAdmin chat oversight — read-only visibility into every chat room and its
// transcript across ALL agencies. Deliberately cross-tenant (IgnoreQueryFilters)
// and gated to SuperAdmin only. Intended as a reusable oversight surface.
// ─────────────────────────────────────────────────────────────────────────────

public record ChatOversightRoomDto(
    Guid Id, string Name, bool IsDirect, string AgencyName,
    IReadOnlyList<string> Members, int MessageCount, DateTime? LastMessageAt);

public record ChatOversightMessageDto(
    Guid Id, string Sender, string Body, string? AttachmentName, DateTime SentAt);

/// <summary>Every chat room across every agency (optionally scoped to one agency).</summary>
public record ListAllChatRoomsQuery(Guid? AgencyId = null) : IRequest<IReadOnlyList<ChatOversightRoomDto>>;

/// <summary>Full message transcript of one room (any agency).</summary>
public record GetChatRoomTranscriptQuery(Guid RoomId) : IRequest<IReadOnlyList<ChatOversightMessageDto>>;

public class ChatOversightHandler :
    IRequestHandler<ListAllChatRoomsQuery, IReadOnlyList<ChatOversightRoomDto>>,
    IRequestHandler<GetChatRoomTranscriptQuery, IReadOnlyList<ChatOversightMessageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public ChatOversightHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
    }

    // Oversight is cross-tenant PII — SuperAdmin only, enforced here on top of the controller gate.
    private void EnsureSuperAdmin()
    {
        if (!_user.Roles.Contains(DomainRoles.SuperAdmin)) throw new ForbiddenAccessException();
    }

    public async Task<IReadOnlyList<ChatOversightRoomDto>> Handle(ListAllChatRoomsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();

        var roomsQ = _db.ChatRooms.IgnoreQueryFilters().Where(r => !r.IsDeleted);
        if (request.AgencyId is { } aid) roomsQ = roomsQ.Where(r => r.AgencyId == aid);
        var rooms = await roomsQ.AsNoTracking().ToListAsync(ct);
        if (rooms.Count == 0) return Array.Empty<ChatOversightRoomDto>();

        var roomIds = rooms.Select(r => r.Id).ToList();
        var agencyNames = await _db.Agencies.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var names = await _identity.ListUserNamesAsync(null, ct);   // every agency's users

        var members = await _db.ChatRoomMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => roomIds.Contains(m.RoomId) && !m.IsDeleted)
            .Select(m => new { m.RoomId, m.UserId }).ToListAsync(ct);
        var membersByRoom = members.GroupBy(m => m.RoomId)
            .ToDictionary(g => g.Key, g => g.Select(x => names.TryGetValue(x.UserId, out var n) ? n : "—").ToList());

        var stats = await _db.ChatMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => roomIds.Contains(m.RoomId) && !m.IsDeleted)
            .GroupBy(m => m.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Count(), Last = (DateTime?)g.Max(x => x.SentAt) })
            .ToListAsync(ct);
        var statsByRoom = stats.ToDictionary(s => s.RoomId, s => (s.Count, s.Last));

        return rooms
            .Select(r =>
            {
                statsByRoom.TryGetValue(r.Id, out var st);
                return new ChatOversightRoomDto(
                    r.Id, r.Name, r.IsDirect,
                    agencyNames.TryGetValue(r.AgencyId, out var an) ? an : "—",
                    membersByRoom.TryGetValue(r.Id, out var mem) ? mem : new List<string>(),
                    st.Count, st.Last);
            })
            .OrderByDescending(r => r.LastMessageAt ?? DateTime.MinValue)
            .ToList();
    }

    public async Task<IReadOnlyList<ChatOversightMessageDto>> Handle(GetChatRoomTranscriptQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();

        var msgs = await _db.ChatMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.RoomId == request.RoomId && !m.IsDeleted)
            .OrderBy(m => m.SentAt)
            .Select(m => new { m.Id, m.SenderUserId, m.Body, m.AttachmentName, m.SentAt })
            .ToListAsync(ct);
        var names = await _identity.ListUserNamesAsync(null, ct);
        return msgs
            .Select(m => new ChatOversightMessageDto(
                m.Id, names.TryGetValue(m.SenderUserId, out var n) ? n : "—", m.Body, m.AttachmentName, m.SentAt))
            .ToList();
    }
}
