using System.Text.RegularExpressions;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Feed;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record FeedReactionDto(string Emoji, int Count, bool Mine);
public record FeedCommentDto(Guid Id, Guid AuthorUserId, string AuthorName, string Body, DateTime CreatedAt, bool CanDelete);
public record FeedPollOptionDto(Guid Id, string Text, int Votes);
public record FeedPollDto(IReadOnlyList<FeedPollOptionDto> Options, int TotalVotes, Guid? MyOptionId);
public record FeedPostDto(
    Guid Id, Guid AuthorUserId, string AuthorName, string Body, string Kind, bool HasImage, DateTime CreatedAt,
    IReadOnlyList<FeedReactionDto> Reactions, IReadOnlyList<FeedCommentDto> Comments, FeedPollDto? Poll, bool CanDelete);

// ── Requests ──────────────────────────────────────────────────────────────────

public record ListFeedQuery(int Skip = 0, int Take = 20) : IRequest<IReadOnlyList<FeedPostDto>>;
public record CreatePostCommand(string Body, FeedPostKind Kind = FeedPostKind.Update, string? ImageKey = null, IReadOnlyList<string>? Options = null) : IRequest<FeedPostDto>;
public record DeletePostCommand(Guid PostId) : IRequest<Unit>;
public record GetPostImageKeyQuery(Guid PostId) : IRequest<string?>;
public record VotePollCommand(Guid PostId, Guid OptionId) : IRequest<Unit>;
public record ToggleReactionCommand(Guid PostId, string Emoji) : IRequest<Unit>;
public record AddCommentCommand(Guid PostId, string Body) : IRequest<FeedCommentDto>;
public record DeleteCommentCommand(Guid CommentId) : IRequest<Unit>;

public class CreatePostValidator : AbstractValidator<CreatePostCommand>
{
    public CreatePostValidator()
    {
        // Body may be empty when an image is attached (an image-only post is fine).
        RuleFor(x => x.Body).MaximumLength(FeedConstants.MaxPostLength);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Body) || !string.IsNullOrEmpty(x.ImageKey)
                || (x.Kind == FeedPostKind.Poll && x.Options != null))
            .WithMessage("Add some text or an image.");
        RuleFor(x => x.Kind).IsInEnum();
        // A poll needs a question + at least two options.
        RuleFor(x => x.Body).NotEmpty().When(x => x.Kind == FeedPostKind.Poll).WithMessage("Add a question for the poll.");
        RuleFor(x => x.Options)
            .Must(o => o != null && o.Count(t => !string.IsNullOrWhiteSpace(t)) >= 2)
            .When(x => x.Kind == FeedPostKind.Poll)
            .WithMessage("A poll needs at least two options.");
    }
}
public class AddCommentValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(FeedConstants.MaxCommentLength);
}
public class ToggleReactionValidator : AbstractValidator<ToggleReactionCommand>
{
    public ToggleReactionValidator() => RuleFor(x => x.Emoji).NotEmpty().MaximumLength(FeedConstants.MaxEmojiLength);
}

public static class FeedConstants
{
    public const int MaxPostLength = 4000;
    public const int MaxCommentLength = 2000;
    public const int MaxEmojiLength = 16;
    public const int MaxFeedPageSize = 50;
    public const long MaxImageBytes = 8L * 1024 * 1024;   // 8 MB per attached image
    public const string ImageContainer = "feed";
    public const int MaxPollOptions = 8;
    // @mention token: 2–32 of letters/digits/dot/underscore/hyphen.
    public static readonly Regex MentionPattern = new(@"@([A-Za-z0-9._-]{2,32})", RegexOptions.Compiled);
}

// ── Handlers ──────────────────────────────────────────────────────────────────

public class FeedHandlers :
    IRequestHandler<ListFeedQuery, IReadOnlyList<FeedPostDto>>,
    IRequestHandler<CreatePostCommand, FeedPostDto>,
    IRequestHandler<DeletePostCommand, Unit>,
    IRequestHandler<GetPostImageKeyQuery, string?>,
    IRequestHandler<VotePollCommand, Unit>,
    IRequestHandler<ToggleReactionCommand, Unit>,
    IRequestHandler<AddCommentCommand, FeedCommentDto>,
    IRequestHandler<DeleteCommentCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly INotificationDispatcher _notify;

    public FeedHandlers(IApplicationDbContext db, ICurrentUser user, IIdentityService identity, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _notify = Guard.AgainstNull(notify);
    }

    private (Guid userId, Guid agencyId) Ctx()
    {
        if (_user.UserId is not { } uid || _user.AgencyId is not { } aid) throw new ForbiddenAccessException();
        return (uid, aid);
    }

    private bool IsAdmin => _user.IsSuperAdmin || _user.Roles.Contains(DomainRoles.Admin);

    public async Task<IReadOnlyList<FeedPostDto>> Handle(ListFeedQuery request, CancellationToken ct)
    {
        var (uid, aid) = Ctx();
        var take = Math.Clamp(request.Take, 1, FeedConstants.MaxFeedPageSize);
        var skip = Math.Max(0, request.Skip);

        var posts = await _db.FeedPosts.AsNoTracking()
            .Where(p => p.AgencyId == aid)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)   // deterministic tiebreaker so equal timestamps don't dup/skip across pages
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        if (posts.Count == 0) return Array.Empty<FeedPostDto>();

        var postIds = posts.Select(p => p.Id).ToList();
        var reactions = await _db.FeedReactions.AsNoTracking()
            .Where(r => postIds.Contains(r.PostId)).ToListAsync(ct);
        var comments = await _db.FeedComments.AsNoTracking()
            .Where(c => postIds.Contains(c.PostId)).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var pollPostIds = posts.Where(p => p.Kind == FeedPostKind.Poll).Select(p => p.Id).ToList();
        var pollOptions = pollPostIds.Count == 0 ? new() : await _db.FeedPollOptions.AsNoTracking()
            .Where(o => pollPostIds.Contains(o.PostId)).OrderBy(o => o.Order).ToListAsync(ct);
        var pollVotes = pollPostIds.Count == 0 ? new() : await _db.FeedPollVotes.AsNoTracking()
            .Where(v => pollPostIds.Contains(v.PostId)).ToListAsync(ct);

        var names = await _identity.ListUserNamesAsync(aid, ct);
        string Name(Guid id) => names.TryGetValue(id, out var n) ? n : "Someone";

        var reactionsByPost = reactions.GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.ToList());
        var commentsByPost = comments.GroupBy(c => c.PostId).ToDictionary(g => g.Key, g => g.ToList());
        var optionsByPost = pollOptions.GroupBy(o => o.PostId).ToDictionary(g => g.Key, g => g.ToList());
        var votesByPost = pollVotes.GroupBy(v => v.PostId).ToDictionary(g => g.Key, g => g.ToList());

        return posts.Select(p =>
        {
            var rx = (reactionsByPost.GetValueOrDefault(p.Id) ?? new())
                .GroupBy(r => r.Emoji)
                .Select(g => new FeedReactionDto(g.Key, g.Count(), g.Any(r => r.UserId == uid)))
                .OrderByDescending(r => r.Count).ToList();
            var cm = (commentsByPost.GetValueOrDefault(p.Id) ?? new())
                .Select(c => new FeedCommentDto(c.Id, c.AuthorUserId, Name(c.AuthorUserId), c.Body, c.CreatedAt,
                    c.AuthorUserId == uid || IsAdmin)).ToList();
            var poll = BuildPoll(p, optionsByPost.GetValueOrDefault(p.Id), votesByPost.GetValueOrDefault(p.Id), uid);
            return new FeedPostDto(p.Id, p.AuthorUserId, Name(p.AuthorUserId), p.Body, p.Kind.ToString(), p.ImageKey != null, p.CreatedAt,
                rx, cm, poll, p.AuthorUserId == uid || IsAdmin);
        }).ToList();
    }

    private static FeedPollDto? BuildPoll(FeedPost post, List<FeedPollOption>? options, List<FeedPollVote>? votes, Guid uid)
    {
        if (post.Kind != FeedPostKind.Poll || options is null || options.Count == 0) return null;
        var v = votes ?? new();
        var opts = options.OrderBy(o => o.Order)
            .Select(o => new FeedPollOptionDto(o.Id, o.Text, v.Count(x => x.OptionId == o.Id)))
            .ToList();
        return new FeedPollDto(opts, v.Count, v.FirstOrDefault(x => x.UserId == uid)?.OptionId);
    }

    public async Task<FeedPostDto> Handle(CreatePostCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        var post = new FeedPost
        {
            AgencyId = aid, AuthorUserId = uid, Body = (request.Body ?? "").Trim(),
            Kind = request.Kind, ImageKey = string.IsNullOrWhiteSpace(request.ImageKey) ? null : request.ImageKey.Trim(),
        };
        _db.FeedPosts.Add(post);

        // A Poll's options are stored alongside the post (trimmed, de-duped-by-position, capped).
        List<FeedPollOption> pollOptions = new();
        if (request.Kind == FeedPostKind.Poll && request.Options is not null)
        {
            var order = 0;
            foreach (var text in request.Options.Select(o => o?.Trim()).Where(o => !string.IsNullOrEmpty(o)).Take(FeedConstants.MaxPollOptions))
                pollOptions.Add(new FeedPollOption { AgencyId = aid, PostId = post.Id, Text = text!, Order = order++ });
            _db.FeedPollOptions.AddRange(pollOptions);
        }
        await _db.SaveChangesAsync(ct);

        // Notify the whole agency about the new post; anyone @mentioned gets a specific "you were
        // mentioned" notification instead of the generic one. Best-effort: the post already succeeded.
        await NotifyPostAsync(aid, uid, post, ct);

        var pollDto = pollOptions.Count == 0 ? null
            : new FeedPollDto(pollOptions.Select(o => new FeedPollOptionDto(o.Id, o.Text, 0)).ToList(), 0, null);
        return new FeedPostDto(post.Id, uid, _user.UserName ?? "You", post.Body, post.Kind.ToString(), post.ImageKey != null,
            post.CreatedAt, Array.Empty<FeedReactionDto>(), Array.Empty<FeedCommentDto>(), pollDto, true);
    }

    public async Task<Unit> Handle(VotePollCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        // The option must belong to a poll in the caller's agency (blocks cross-tenant / mismatched votes).
        var option = await _db.FeedPollOptions.FirstOrDefaultAsync(
            o => o.Id == request.OptionId && o.PostId == request.PostId && o.AgencyId == aid, ct)
            ?? throw new NotFoundException(nameof(FeedPollOption), request.OptionId);

        var existing = await _db.FeedPollVotes.FirstOrDefaultAsync(v => v.PostId == request.PostId && v.UserId == uid, ct);
        if (existing is null)
            _db.FeedPollVotes.Add(new FeedPollVote { AgencyId = aid, PostId = request.PostId, OptionId = option.Id, UserId = uid });
        else if (existing.OptionId == option.Id)
            _db.FeedPollVotes.Remove(existing);   // clicking your current choice again un-votes
        else
            existing.OptionId = option.Id;        // move the vote to the new option
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // A concurrent first-time vote (double-tap / two tabs) already inserted this (PostId,UserId)
            // row, tripping the unique index. The vote is recorded — treat this as an idempotent success.
        }
        return Unit.Value;
    }

    public async Task<string?> Handle(GetPostImageKeyQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (_, aid) = Ctx();
        // Agency-scoped: a post id from another tenant simply isn't found, so its image can't be fetched.
        var post = await _db.FeedPosts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PostId && p.AgencyId == aid, ct)
            ?? throw new NotFoundException(nameof(FeedPost), request.PostId);
        return post.ImageKey;
    }

    public async Task<Unit> Handle(DeletePostCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        var post = await _db.FeedPosts.FirstOrDefaultAsync(p => p.Id == request.PostId && p.AgencyId == aid, ct)
            ?? throw new NotFoundException(nameof(FeedPost), request.PostId);
        if (post.AuthorUserId != uid && !IsAdmin) throw new ForbiddenAccessException();

        // Soft-delete the post plus its comments and reactions (the audit interceptor makes Remove soft).
        var comments = await _db.FeedComments.Where(c => c.PostId == post.Id).ToListAsync(ct);
        var reactions = await _db.FeedReactions.Where(r => r.PostId == post.Id).ToListAsync(ct);
        _db.FeedComments.RemoveRange(comments);
        _db.FeedReactions.RemoveRange(reactions);
        _db.FeedPosts.Remove(post);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public async Task<Unit> Handle(ToggleReactionCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        _ = await _db.FeedPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PostId && p.AgencyId == aid, ct)
            ?? throw new NotFoundException(nameof(FeedPost), request.PostId);

        var emoji = request.Emoji.Trim();
        var existing = await _db.FeedReactions
            .FirstOrDefaultAsync(r => r.PostId == request.PostId && r.UserId == uid && r.Emoji == emoji, ct);
        if (existing is not null)
            _db.FeedReactions.Remove(existing);   // toggle off
        else
            _db.FeedReactions.Add(new FeedReaction { AgencyId = aid, PostId = request.PostId, UserId = uid, Emoji = emoji });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // A concurrent identical react (double-tap / two tabs) already inserted this
            // (PostId,UserId,Emoji) row, tripping the unique index. The reaction now exists —
            // treat the toggle-on as an idempotent success rather than a 500.
        }
        return Unit.Value;
    }

    public async Task<FeedCommentDto> Handle(AddCommentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        var post = await _db.FeedPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PostId && p.AgencyId == aid, ct)
            ?? throw new NotFoundException(nameof(FeedPost), request.PostId);

        var comment = new FeedComment { AgencyId = aid, PostId = post.Id, AuthorUserId = uid, Body = request.Body.Trim() };
        _db.FeedComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        // Notify the post author (unless commenting on your own) + anyone @mentioned.
        if (post.AuthorUserId != uid)
            await BestEffortNotifyAsync(aid, post.AuthorUserId, "New comment on your post", $"{_user.UserName} commented on your post.", ct);
        await NotifyMentionsAsync(aid, uid, comment.Body, "mentioned you in a comment", ct, excludeUserId: post.AuthorUserId);

        return new FeedCommentDto(comment.Id, uid, _user.UserName ?? "You", comment.Body, comment.CreatedAt, true);
    }

    public async Task<Unit> Handle(DeleteCommentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var (uid, aid) = Ctx();
        var comment = await _db.FeedComments.FirstOrDefaultAsync(c => c.Id == request.CommentId && c.AgencyId == aid, ct)
            ?? throw new NotFoundException(nameof(FeedComment), request.CommentId);
        if (comment.AuthorUserId != uid && !IsAdmin) throw new ForbiddenAccessException();
        _db.FeedComments.Remove(comment);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    // ── Mentions ──────────────────────────────────────────────────────────────

    private async Task NotifyMentionsAsync(Guid agencyId, Guid authorId, string body, string what, CancellationToken ct, Guid? excludeUserId = null)
    {
        var handles = FeedConstants.MentionPattern.Matches(body)
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (handles.Count == 0) return;

        // Best-effort — the post/comment is already committed; a name-lookup/dispatch hiccup must
        // never bubble a 500 (which would prompt a client retry and duplicate the post/comment).
        try
        {
            var names = await _identity.ListUserNamesAsync(agencyId, ct);   // id -> username (agency-scoped)
            var byHandle = names.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

            foreach (var handle in handles)
            {
                if (!byHandle.TryGetValue(handle, out var targetId)) continue;
                if (targetId == authorId || targetId == excludeUserId) continue;
                await BestEffortNotifyAsync(agencyId, targetId, "You were mentioned", $"{_user.UserName} {what}.", ct);
            }
        }
        catch { /* best-effort — the write already succeeded */ }
    }

    // Everyone in the agency is notified of a new post (one bulk insert, not N dispatcher round-trips;
    // the in-app bell poll surfaces it). @mentioned teammates get a specific mention notification
    // instead of the generic one. Best-effort — the post is already persisted.
    private async Task NotifyPostAsync(Guid agencyId, Guid authorId, FeedPost post, CancellationToken ct)
    {
        try
        {
            var names = await _identity.ListUserNamesAsync(agencyId, ct);   // id -> username
            var author = _user.UserName ?? "A teammate";

            // Resolve @mentions in the body to user ids (excluding the author).
            var byHandle = names.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
            var mentioned = FeedConstants.MentionPattern.Matches(post.Body)
                .Select(m => m.Groups[1].Value)
                .Select(h => byHandle.TryGetValue(h, out var id) ? id : (Guid?)null)
                .Where(id => id is { } g && g != authorId)
                .Select(id => id!.Value)
                .ToHashSet();

            var isAnnouncement = post.Kind == FeedPostKind.Announcement;
            var title = isAnnouncement ? "📣 New announcement" : "New post on Pulse";
            var body = isAnnouncement ? $"{author} posted an announcement." : $"{author} shared a post on Pulse.";

            var rows = new List<Notification>();
            foreach (var id in mentioned)
                rows.Add(new Notification { AgencyId = agencyId, UserId = id, Title = "You were mentioned", Body = $"{author} mentioned you in a post.", Url = "/pulse" });
            foreach (var id in names.Keys.Where(id => id != authorId && !mentioned.Contains(id)))
                rows.Add(new Notification { AgencyId = agencyId, UserId = id, Title = title, Body = body, Url = "/pulse" });

            if (rows.Count == 0) return;
            _db.Notifications.AddRange(rows);
            await _db.SaveChangesAsync(ct);
        }
        catch { /* best-effort — the post already succeeded */ }
    }

    // Notifications are best-effort — a dispatch failure must never fail the post/comment write.
    // A Url makes the bell entry clickable (jumps to the feed).
    private async Task BestEffortNotifyAsync(Guid agencyId, Guid userId, string title, string body, CancellationToken ct, string? url = "/pulse")
    {
        try
        {
            await _notify.DispatchAsync(new NotificationPayload(agencyId, userId, title, body, url),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* swallow — the feed write already succeeded */ }
    }
}
