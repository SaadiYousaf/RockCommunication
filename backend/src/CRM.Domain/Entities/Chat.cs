using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class ChatRoom : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsDirect { get; set; }
    public ICollection<ChatRoomMember> Members { get; set; } = new List<ChatRoomMember>();
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public class ChatRoomMember : TenantEntity
{
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public DateTime? LastReadAt { get; set; }
}

public class ChatMessage : TenantEntity
{
    public Guid RoomId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    // Optional file attachment. AttachmentUrl is an opaque storage key
    // (e.g. "chat-attachments/{roomId}/{guid}{ext}"); clients fetch the bytes
    // via the authorized download endpoint, never directly.
    public string? AttachmentUrl { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }
}

public class CallRecord : CallCenterEntity
{
    public Guid LeadId { get; set; }
    public Guid AgentUserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderCallId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Direction { get; set; } = "Outbound";
    public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? RecordingUrl { get; set; }
    public string? WrapUpCode { get; set; }
    public string? Notes { get; set; }
    public TimeSpan? TalkTime => AnsweredAt is null || EndedAt is null ? null : EndedAt - AnsweredAt;
    public TimeSpan? WaitBeforeAnswer => AnsweredAt is null ? null : AnsweredAt - InitiatedAt;
}
