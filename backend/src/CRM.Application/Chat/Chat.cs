using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Chat;

public record ChatRoomMemberDto(Guid UserId, DateTime? LastReadAt);
public record ChatRoomDto(Guid Id, string Name, bool IsDirect, IReadOnlyList<Guid> MemberUserIds, IReadOnlyList<ChatRoomMemberDto> Members);
public record ChatMessageDto(
    Guid Id,
    Guid RoomId,
    Guid SenderUserId,
    string Body,
    DateTime SentAt,
    string? AttachmentName = null,
    string? AttachmentContentType = null,
    long? AttachmentSize = null);

public record CreateRoomDto(string Name, bool IsDirect, IReadOnlyList<Guid> MemberUserIds);
public record CreateRoomCommand(CreateRoomDto Input) : IRequest<ChatRoomDto>;

public class CreateRoomValidator : AbstractValidator<CreateRoomCommand>
{
    public CreateRoomValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Input.MemberUserIds).NotNull();
    }
}

public class CreateRoomHandler : IRequestHandler<CreateRoomCommand, ChatRoomDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public CreateRoomHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<ChatRoomDto> Handle(CreateRoomCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var room = new ChatRoom
        {
            AgencyId = _user.AgencyId.Value,
            Name = request.Input.Name,
            IsDirect = request.Input.IsDirect
        };
        var members = request.Input.MemberUserIds.Append(_user.UserId.Value).Distinct().ToList();
        foreach (var uid in members)
        {
            room.Members.Add(new ChatRoomMember { AgencyId = room.AgencyId, UserId = uid });
        }
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync(ct);
        return new ChatRoomDto(
            room.Id, room.Name, room.IsDirect,
            members,
            members.Select(uid => new ChatRoomMemberDto(uid, null)).ToList());
    }
}

/// <summary>
/// Opens (or reuses) a 1:1 direct conversation with another user in the SAME office.
/// The agency/office boundary is enforced by comparing the target's AgencyId to the
/// caller's — you can only DM colleagues in your own office.
/// </summary>
public record StartDirectMessageCommand(Guid OtherUserId) : IRequest<ChatRoomDto>;

public class StartDirectMessageHandler : IRequestHandler<StartDirectMessageCommand, ChatRoomDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public StartDirectMessageHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _identity = Guard.AgainstNull(identity);
    }

    public async Task<ChatRoomDto> Handle(StartDirectMessageCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var me = _user.UserId.Value;
        if (request.OtherUserId == me) throw new ConflictException("You can't message yourself.");

        var other = await _identity.GetUserAsync(request.OtherUserId, ct)
            ?? throw new NotFoundException("User", request.OtherUserId);
        // Same-office guard.
        if (other.AgencyId != _user.AgencyId.Value)
            throw new ForbiddenAccessException("You can only message colleagues in your own office.");

        // Reuse an existing direct room between exactly these two users.
        var myRoomIds = await _db.ChatRoomMembers
            .Where(m => m.UserId == me)
            .Select(m => m.RoomId)
            .ToListAsync(ct);

        var existing = await _db.ChatRooms
            .Include(r => r.Members)
            .Where(r => r.IsDirect && myRoomIds.Contains(r.Id))
            .ToListAsync(ct);

        var match = existing.FirstOrDefault(r =>
            r.Members.Count == 2 &&
            r.Members.Any(m => m.UserId == me) &&
            r.Members.Any(m => m.UserId == request.OtherUserId));

        if (match is not null)
            return new ChatRoomDto(
                match.Id, match.Name, match.IsDirect,
                match.Members.Select(m => m.UserId).ToList(),
                match.Members.Select(m => new ChatRoomMemberDto(m.UserId, m.LastReadAt)).ToList());

        var room = new ChatRoom
        {
            AgencyId = _user.AgencyId.Value,
            Name = $"DM: {other.UserName}",
            IsDirect = true,
        };
        room.Members.Add(new ChatRoomMember { AgencyId = room.AgencyId, UserId = me });
        room.Members.Add(new ChatRoomMember { AgencyId = room.AgencyId, UserId = request.OtherUserId });
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync(ct);

        var members = new[] { me, request.OtherUserId };
        return new ChatRoomDto(
            room.Id, room.Name, room.IsDirect,
            members,
            members.Select(uid => new ChatRoomMemberDto(uid, null)).ToList());
    }
}

public record SendMessageCommand(
    Guid RoomId,
    string Body,
    string? AttachmentUrl = null,
    string? AttachmentName = null,
    string? AttachmentContentType = null,
    long? AttachmentSize = null) : IRequest<ChatMessageDto>;

public class SendMessageHandler : IRequestHandler<SendMessageCommand, ChatMessageDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IChatBroadcaster _broadcaster;

    public SendMessageHandler(IApplicationDbContext db, ICurrentUser user, IChatBroadcaster broadcaster)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _broadcaster = Guard.AgainstNull(broadcaster);
    }

    public async Task<ChatMessageDto> Handle(SendMessageCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var hasAttachment = !string.IsNullOrEmpty(request.AttachmentUrl);
        if (string.IsNullOrWhiteSpace(request.Body) && !hasAttachment)
            throw new ConflictException("Message cannot be empty.");

        var member = await _db.ChatRoomMembers.FirstOrDefaultAsync(
            m => m.RoomId == request.RoomId && m.UserId == _user.UserId, ct)
            ?? throw new ForbiddenAccessException("Not a member of this room.");

        var msg = new ChatMessage
        {
            AgencyId = _user.AgencyId.Value,
            RoomId = request.RoomId,
            SenderUserId = _user.UserId.Value,
            Body = (request.Body ?? string.Empty).Trim(),
            AttachmentUrl = request.AttachmentUrl,
            AttachmentName = request.AttachmentName,
            AttachmentContentType = request.AttachmentContentType,
            AttachmentSize = request.AttachmentSize,
        };
        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync(ct);

        var dto = new ChatMessageDto(
            msg.Id, msg.RoomId, msg.SenderUserId, msg.Body, msg.SentAt,
            msg.AttachmentName, msg.AttachmentContentType, msg.AttachmentSize);
        // Push to every connected SignalR client in this room — that's what makes
        // the message appear in real time without the receiver having to refresh.
        await _broadcaster.BroadcastMessageAsync(msg.RoomId, dto, ct);
        return dto;
    }
}

public record ListMyRoomsQuery() : IRequest<IReadOnlyList<ChatRoomDto>>;
public class ListMyRoomsHandler : IRequestHandler<ListMyRoomsQuery, IReadOnlyList<ChatRoomDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public ListMyRoomsHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<ChatRoomDto>> Handle(ListMyRoomsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var roomIds = await _db.ChatRoomMembers
            .Where(m => m.UserId == _user.UserId).Select(m => m.RoomId).ToListAsync(ct);
        var rooms = await _db.ChatRooms
            .Include(r => r.Members)
            .Where(r => roomIds.Contains(r.Id) && r.AgencyId == _user.AgencyId)
            .ToListAsync(ct);
        return rooms.Select(r => new ChatRoomDto(
            r.Id, r.Name, r.IsDirect,
            r.Members.Select(m => m.UserId).ToList(),
            r.Members.Select(m => new ChatRoomMemberDto(m.UserId, m.LastReadAt)).ToList()
        )).ToList();
    }
}

public record AttachmentInfo(string StorageKey, string FileName, string ContentType, long Size);
public record GetAttachmentQuery(Guid MessageId) : IRequest<AttachmentInfo>;

public class GetAttachmentHandler : IRequestHandler<GetAttachmentQuery, AttachmentInfo>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public GetAttachmentHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<AttachmentInfo> Handle(GetAttachmentQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        var msg = await _db.ChatMessages
            .Where(m => m.Id == request.MessageId)
            .Select(m => new { m.Id, m.RoomId, m.AttachmentUrl, m.AttachmentName, m.AttachmentContentType, m.AttachmentSize })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(ChatMessage), request.MessageId);
        if (string.IsNullOrEmpty(msg.AttachmentUrl)) throw new NotFoundException("Attachment", request.MessageId);
        var member = await _db.ChatRoomMembers.AnyAsync(m => m.RoomId == msg.RoomId && m.UserId == _user.UserId, ct);
        if (!member) throw new ForbiddenAccessException();
        return new AttachmentInfo(
            msg.AttachmentUrl,
            msg.AttachmentName ?? "file",
            string.IsNullOrEmpty(msg.AttachmentContentType) ? "application/octet-stream" : msg.AttachmentContentType,
            msg.AttachmentSize ?? 0);
    }
}

public record RoomMessagesQuery(Guid RoomId, int Take = 50) : IRequest<IReadOnlyList<ChatMessageDto>>;
public class RoomMessagesHandler : IRequestHandler<RoomMessagesQuery, IReadOnlyList<ChatMessageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public RoomMessagesHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<ChatMessageDto>> Handle(RoomMessagesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        var member = await _db.ChatRoomMembers.AnyAsync(
            m => m.RoomId == request.RoomId && m.UserId == _user.UserId, ct);
        if (!member) throw new ForbiddenAccessException();
        return await _db.ChatMessages
            .Where(m => m.RoomId == request.RoomId)
            .OrderByDescending(m => m.SentAt).Take(Math.Min(request.Take, 200))
            .OrderBy(m => m.SentAt)
            .Select(m => new ChatMessageDto(
                m.Id, m.RoomId, m.SenderUserId, m.Body, m.SentAt,
                m.AttachmentName, m.AttachmentContentType, m.AttachmentSize))
            .ToListAsync(ct);
    }
}
