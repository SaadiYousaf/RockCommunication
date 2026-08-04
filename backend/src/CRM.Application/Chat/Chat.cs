using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Chat;

public record ChatRoomMemberDto(Guid UserId, DateTime? LastReadAt);
public record ChatRoomDto(
    Guid Id, string Name, bool IsDirect,
    IReadOnlyList<Guid> MemberUserIds, IReadOnlyList<ChatRoomMemberDto> Members,
    string? LastMessagePreview = null, DateTime? LastMessageAt = null, Guid? LastMessageSenderId = null);

/// <summary>One emoji reaction and everyone who applied it (the client derives count + "mine").</summary>
public record ReactionDto(string Emoji, IReadOnlyList<Guid> UserIds);

public record ChatMessageDto(
    Guid Id,
    Guid RoomId,
    Guid SenderUserId,
    string Body,
    DateTime SentAt,
    string? AttachmentName = null,
    string? AttachmentContentType = null,
    long? AttachmentSize = null,
    DateTime? EditedAt = null,
    IReadOnlyList<ReactionDto>? Reactions = null);

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

public class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty();
        // Body required unless an attachment carries the message; bound everything so SQLite
        // (which has no length limit) can't be flooded with an unbounded message.
        RuleFor(x => x.Body).NotEmpty().When(x => string.IsNullOrWhiteSpace(x.AttachmentUrl));
        RuleFor(x => x.Body).MaximumLength(4000);
        RuleFor(x => x.AttachmentName).MaximumLength(260);
        RuleFor(x => x.AttachmentContentType).MaximumLength(120);
        RuleFor(x => x.AttachmentUrl).MaximumLength(500);
        RuleFor(x => x.AttachmentSize).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100L * 1024 * 1024)
            .When(x => x.AttachmentSize.HasValue);
    }
}

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

        // Latest message per room, for the list preview + sort. Projected lightweight and grouped in
        // memory (internal chat volume is modest; the (RoomId, SentAt) index keeps the scan cheap).
        var lastByRoom = (await _db.ChatMessages
                .Where(m => roomIds.Contains(m.RoomId))
                .Select(m => new { m.RoomId, m.SentAt, m.Body, m.AttachmentName, m.SenderUserId })
                .ToListAsync(ct))
            .GroupBy(x => x.RoomId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SentAt).First());

        return rooms.Select(r =>
        {
            lastByRoom.TryGetValue(r.Id, out var last);
            var preview = last is null ? null
                : !string.IsNullOrWhiteSpace(last.Body) ? last.Body
                : last.AttachmentName is not null ? "📎 " + last.AttachmentName
                : null;
            return new ChatRoomDto(
                r.Id, r.Name, r.IsDirect,
                r.Members.Select(m => m.UserId).ToList(),
                r.Members.Select(m => new ChatRoomMemberDto(m.UserId, m.LastReadAt)).ToList(),
                preview, last?.SentAt, last?.SenderUserId);
        }).ToList();
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
        var msgs = await _db.ChatMessages
            .Where(m => m.RoomId == request.RoomId)
            .OrderByDescending(m => m.SentAt).Take(Math.Clamp(request.Take, 1, 200))   // negative Take => LIMIT -1 (unbounded) on SQLite
            .OrderBy(m => m.SentAt)
            .Select(m => new
            {
                m.Id, m.RoomId, m.SenderUserId, m.Body, m.SentAt,
                m.AttachmentName, m.AttachmentContentType, m.AttachmentSize, m.EditedAt,
            })
            .ToListAsync(ct);

        var ids = msgs.Select(m => m.Id).ToList();
        var reactions = await ChatReactions.ForMessagesAsync(_db, ids, ct);

        return msgs.Select(m => new ChatMessageDto(
            m.Id, m.RoomId, m.SenderUserId, m.Body, m.SentAt,
            m.AttachmentName, m.AttachmentContentType, m.AttachmentSize, m.EditedAt,
            reactions.TryGetValue(m.Id, out var rs) ? rs : null)).ToList();
    }
}

/// <summary>Reaction aggregation shared by the message-list read and the toggle broadcast.</summary>
internal static class ChatReactions
{
    public static async Task<Dictionary<Guid, List<ReactionDto>>> ForMessagesAsync(
        IApplicationDbContext db, IReadOnlyList<Guid> messageIds, CancellationToken ct)
    {
        if (messageIds.Count == 0) return new();
        return (await db.ChatMessageReactions
                .Where(x => messageIds.Contains(x.MessageId))
                .Select(x => new { x.MessageId, x.Emoji, x.UserId })
                .ToListAsync(ct))
            .GroupBy(x => x.MessageId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.Emoji)
                      .Select(eg => new ReactionDto(eg.Key, eg.Select(x => x.UserId).ToList()))
                      .ToList());
    }

    public static async Task<IReadOnlyList<ReactionDto>> ForMessageAsync(
        IApplicationDbContext db, Guid messageId, CancellationToken ct)
        => (await ForMessagesAsync(db, new[] { messageId }, ct)).TryGetValue(messageId, out var r)
            ? r : new List<ReactionDto>();
}

// ── Reactions + edit + delete ─────────────────────────────────────────────────

public record ToggleReactionCommand(Guid MessageId, string Emoji) : IRequest<Unit>;
public record EditMessageCommand(Guid MessageId, string Body) : IRequest<ChatMessageDto>;
public record DeleteMessageCommand(Guid MessageId) : IRequest<Unit>;

public class ChatMessageActionsValidator : AbstractValidator<EditMessageCommand>
{
    public ChatMessageActionsValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}
public class ToggleReactionValidator : AbstractValidator<ToggleReactionCommand>
{
    public ToggleReactionValidator()
    {
        RuleFor(x => x.Emoji).NotEmpty().MaximumLength(16);
    }
}

public class ChatMessageActionsHandler :
    IRequestHandler<ToggleReactionCommand, Unit>,
    IRequestHandler<EditMessageCommand, ChatMessageDto>,
    IRequestHandler<DeleteMessageCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IChatBroadcaster _broadcaster;

    public ChatMessageActionsHandler(IApplicationDbContext db, ICurrentUser user, IChatBroadcaster broadcaster)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _broadcaster = Guard.AgainstNull(broadcaster);
    }

    /// <summary>Loads a message the caller can act on, verifying room membership.</summary>
    private async Task<ChatMessage> LoadMemberMessageAsync(Guid messageId, CancellationToken ct)
    {
        if (_user.UserId is null) throw new ForbiddenAccessException();
        var msg = await _db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct)
            ?? throw new NotFoundException(nameof(ChatMessage), messageId);
        var member = await _db.ChatRoomMembers.AnyAsync(m => m.RoomId == msg.RoomId && m.UserId == _user.UserId, ct);
        if (!member) throw new ForbiddenAccessException("Not a member of this room.");
        return msg;
    }

    public async Task<Unit> Handle(ToggleReactionCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var msg = await LoadMemberMessageAsync(request.MessageId, ct);
        var emoji = request.Emoji.Trim();
        var existing = await _db.ChatMessageReactions.FirstOrDefaultAsync(
            x => x.MessageId == msg.Id && x.UserId == _user.UserId && x.Emoji == emoji, ct);
        if (existing is null)
            _db.ChatMessageReactions.Add(new ChatMessageReaction
            { AgencyId = msg.AgencyId, MessageId = msg.Id, UserId = _user.UserId!.Value, Emoji = emoji });
        else
            _db.ChatMessageReactions.Remove(existing);
        await _db.SaveChangesAsync(ct);

        var reactions = await ChatReactions.ForMessageAsync(_db, msg.Id, ct);
        await _broadcaster.BroadcastReactionsAsync(msg.RoomId, msg.Id, reactions, ct);
        return Unit.Value;
    }

    public async Task<ChatMessageDto> Handle(EditMessageCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var msg = await LoadMemberMessageAsync(request.MessageId, ct);
        if (msg.SenderUserId != _user.UserId) throw new ForbiddenAccessException("You can only edit your own messages.");
        if (string.IsNullOrEmpty(msg.AttachmentUrl) is false && string.IsNullOrWhiteSpace(request.Body))
            throw new ConflictException("Message cannot be empty.");

        msg.Body = request.Body.Trim();
        msg.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var reactions = await ChatReactions.ForMessageAsync(_db, msg.Id, ct);
        var dto = new ChatMessageDto(msg.Id, msg.RoomId, msg.SenderUserId, msg.Body, msg.SentAt,
            msg.AttachmentName, msg.AttachmentContentType, msg.AttachmentSize, msg.EditedAt, reactions);
        await _broadcaster.BroadcastMessageEditedAsync(msg.RoomId, dto, ct);
        return dto;
    }

    public async Task<Unit> Handle(DeleteMessageCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var msg = await LoadMemberMessageAsync(request.MessageId, ct);
        if (msg.SenderUserId != _user.UserId) throw new ForbiddenAccessException("You can only delete your own messages.");

        _db.ChatMessages.Remove(msg); // audit interceptor turns this into a soft delete (IsDeleted=true)
        await _db.SaveChangesAsync(ct);

        await _broadcaster.BroadcastMessageDeletedAsync(msg.RoomId, msg.Id, ct);
        return Unit.Value;
    }
}
