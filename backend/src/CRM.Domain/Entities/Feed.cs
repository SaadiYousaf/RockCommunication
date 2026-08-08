using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>MS-Teams-style post kinds — drive presentation and (for Announcement) emphasis.</summary>
public enum FeedPostKind
{
    Update = 0,
    Announcement = 1,
    Praise = 2,
    Question = 3,
    Poll = 4,
}

/// <summary>
/// A post on the team "Pulse" feed — the social/collaboration timeline where people share updates,
/// react, comment, and @mention each other. Agency-scoped (TenantEntity).
/// </summary>
public class FeedPost : TenantEntity
{
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public FeedPostKind Kind { get; set; } = FeedPostKind.Update;
    /// <summary>Optional attached image, stored via IFileStorage (container "feed").</summary>
    public string? ImageKey { get; set; }
}

/// <summary>A comment on a <see cref="FeedPost"/>.</summary>
public class FeedComment : TenantEntity
{
    public Guid PostId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
}

/// <summary>One user's emoji reaction to a <see cref="FeedPost"/>. Unique per (post, user, emoji).</summary>
public class FeedReaction : TenantEntity
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = string.Empty;
}

/// <summary>An answer option on a Poll post.</summary>
public class FeedPollOption : TenantEntity
{
    public Guid PostId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>One user's vote on a poll. Unique per (post, user) — voting again moves the vote.</summary>
public class FeedPollVote : TenantEntity
{
    public Guid PostId { get; set; }
    public Guid OptionId { get; set; }
    public Guid UserId { get; set; }
}
