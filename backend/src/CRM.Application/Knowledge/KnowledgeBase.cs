using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Knowledge;

public record KbArticleDto(Guid Id, string Slug, string Title, string Body, string? Tags, string? Category, bool IsPublished, int ViewCount, DateTime? PublishedAt);

public record SearchKbQuery(string? Query, string? Category, bool PublishedOnly = true, int Take = 20)
    : IRequest<IReadOnlyList<KbArticleDto>>;

public record GetKbArticleQuery(string Slug) : IRequest<KbArticleDto?>;

public record UpsertKbArticleCommand(Guid? Id, string Slug, string Title, string Body, string? Tags, string? Category, bool IsPublished)
    : IRequest<KbArticleDto>;

public class UpsertKbArticleValidator : AbstractValidator<UpsertKbArticleCommand>
{
    public UpsertKbArticleValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().Matches(@"^[a-z0-9\-]+$");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty();
    }
}

public class KbHandler :
    IRequestHandler<SearchKbQuery, IReadOnlyList<KbArticleDto>>,
    IRequestHandler<GetKbArticleQuery, KbArticleDto?>,
    IRequestHandler<UpsertKbArticleCommand, KbArticleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public KbHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<KbArticleDto>> Handle(SearchKbQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var q = _db.KnowledgeArticles.AsNoTracking().Where(a => a.AgencyId == _user.AgencyId);
        if (request.PublishedOnly) q = q.Where(a => a.IsPublished);
        if (!string.IsNullOrWhiteSpace(request.Category)) q = q.Where(a => a.Category == request.Category);
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var qq = request.Query.ToLower();
            q = q.Where(a => a.Title.ToLower().Contains(qq) ||
                             a.Body.ToLower().Contains(qq) ||
                             (a.Tags != null && a.Tags.ToLower().Contains(qq)));
        }
        return await q.OrderByDescending(a => a.PublishedAt ?? a.CreatedAt).Take(request.Take)
            .Select(a => Map(a)).ToListAsync(ct);
    }

    public async Task<KbArticleDto?> Handle(GetKbArticleQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var article = await _db.KnowledgeArticles
            .FirstOrDefaultAsync(a => a.AgencyId == _user.AgencyId && a.Slug == request.Slug, ct);
        if (article is null) return null;
        article.ViewCount++;
        await _db.SaveChangesAsync(ct);
        return Map(article);
    }

    public async Task<KbArticleDto> Handle(UpsertKbArticleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        KnowledgeArticle a;
        if (request.Id is { } id)
            a = await _db.KnowledgeArticles.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(KnowledgeArticle), id);
        else { a = new KnowledgeArticle { AgencyId = _user.AgencyId!.Value, AuthorUserId = _user.UserId!.Value }; _db.KnowledgeArticles.Add(a); }
        a.Slug = request.Slug;
        a.Title = request.Title;
        a.Body = request.Body;
        a.Tags = request.Tags;
        a.Category = request.Category;
        if (!a.IsPublished && request.IsPublished) a.PublishedAt = DateTime.UtcNow;
        a.IsPublished = request.IsPublished;
        await _db.SaveChangesAsync(ct);
        return Map(a);
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager") && !_user.Roles.Contains("TeamLead"))
            throw new ForbiddenAccessException();
    }

    private static KbArticleDto Map(KnowledgeArticle a) =>
        new(a.Id, a.Slug, a.Title, a.Body, a.Tags, a.Category, a.IsPublished, a.ViewCount, a.PublishedAt);
}
