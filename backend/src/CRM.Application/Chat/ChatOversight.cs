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

/// <summary>
/// Every chat room across every agency, optionally scoped to one agency and — since chat rooms are
/// agency-level (no CallCenterId) — optionally to one call center by its members. CallCenterId set
/// to <see cref="Guid.Empty"/> means "agency-wide" (rooms with at least one agency-level member).
/// </summary>
public record ListAllChatRoomsQuery(Guid? AgencyId = null, Guid? CallCenterId = null)
    : IRequest<IReadOnlyList<ChatOversightRoomDto>>;

/// <summary>Full message transcript of one room (any agency).</summary>
public record GetChatRoomTranscriptQuery(Guid RoomId) : IRequest<IReadOnlyList<ChatOversightMessageDto>>;

// ── Drill-down: agencies → call centers → chats ──────────────────────────────
public record OversightAgencyDto(Guid Id, string Name, int RoomCount);
/// <summary>Top level of the oversight drill-down: every agency with its chat-room count.</summary>
public record ListOversightAgenciesQuery() : IRequest<IReadOnlyList<OversightAgencyDto>>;

public record OversightCallCenterDto(Guid Id, string Name, int RoomCount);
/// <summary>
/// Second level: the call centers of an agency, each with how many chat rooms involve their members,
/// plus a synthetic "Agency-wide" bucket (<see cref="Guid.Empty"/>) for rooms with agency-level members.
/// </summary>
public record ListOversightCallCentersQuery(Guid AgencyId) : IRequest<IReadOnlyList<OversightCallCenterDto>>;

public class ChatOversightHandler :
    IRequestHandler<ListAllChatRoomsQuery, IReadOnlyList<ChatOversightRoomDto>>,
    IRequestHandler<GetChatRoomTranscriptQuery, IReadOnlyList<ChatOversightMessageDto>>,
    IRequestHandler<ListOversightAgenciesQuery, IReadOnlyList<OversightAgencyDto>>,
    IRequestHandler<ListOversightCallCentersQuery, IReadOnlyList<OversightCallCenterDto>>
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

        // Optional call-center scope: chat rooms are agency-level, so "belongs to a call center" means
        // a member of that room is pinned to it. Guid.Empty = the agency-wide bucket (agency-level member).
        if (request.CallCenterId is { } ccFilter && request.AgencyId is { } agId)
        {
            var ccByUser = (await _identity.ListUsersAsync(agId, ct)).ToDictionary(u => u.Id, u => u.CallCenterId);
            bool Matches(Guid roomId)
            {
                var ccs = members.Where(m => m.RoomId == roomId)
                    .Select(m => ccByUser.TryGetValue(m.UserId, out var cc) ? cc : null);
                return ccFilter == Guid.Empty ? ccs.Any(c => c is null) : ccs.Any(c => c == ccFilter);
            }
            rooms = rooms.Where(r => Matches(r.Id)).ToList();
        }

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

    public async Task<IReadOnlyList<OversightAgencyDto>> Handle(ListOversightAgenciesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();

        var roomCounts = (await _db.ChatRooms.IgnoreQueryFilters().AsNoTracking()
                .Where(r => !r.IsDeleted).Select(r => r.AgencyId).ToListAsync(ct))
            .GroupBy(a => a).ToDictionary(g => g.Key, g => g.Count());

        var agencies = await _db.Agencies.IgnoreQueryFilters().AsNoTracking()
            .Select(a => new { a.Id, a.Name }).ToListAsync(ct);

        return agencies
            .Select(a => new OversightAgencyDto(a.Id, a.Name, roomCounts.TryGetValue(a.Id, out var c) ? c : 0))
            .OrderByDescending(a => a.RoomCount).ThenBy(a => a.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<OversightCallCenterDto>> Handle(ListOversightCallCentersQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();

        var roomIds = await _db.ChatRooms.IgnoreQueryFilters().AsNoTracking()
            .Where(r => !r.IsDeleted && r.AgencyId == request.AgencyId).Select(r => r.Id).ToListAsync(ct);
        var members = await _db.ChatRoomMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => roomIds.Contains(m.RoomId) && !m.IsDeleted)
            .Select(m => new { m.RoomId, m.UserId }).ToListAsync(ct);
        var ccByUser = (await _identity.ListUsersAsync(request.AgencyId, ct)).ToDictionary(u => u.Id, u => u.CallCenterId);

        // room → distinct member call centers (null member CC → the agency-wide bucket).
        int CountRoomsFor(Guid? cc) => roomIds.Count(rid =>
            members.Where(m => m.RoomId == rid)
                   .Select(m => ccByUser.TryGetValue(m.UserId, out var c) ? c : null)
                   .Any(c => c == cc));

        var callCenters = await _db.CallCenters.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.AgencyId == request.AgencyId && !c.IsDeleted)
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);

        var result = callCenters
            .Select(c => new OversightCallCenterDto(c.Id, c.Name, CountRoomsFor(c.Id)))
            .OrderBy(c => c.Name)
            .ToList();

        // Synthetic "Agency-wide" bucket for rooms whose members are agency-level (no call center).
        var agencyWide = CountRoomsFor(null);
        if (agencyWide > 0)
            result.Insert(0, new OversightCallCenterDto(Guid.Empty, "Agency-wide", agencyWide));

        return result;
    }
}
