using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.QA;

public record RubricItemDto(Guid Id, string Label, int MaxScore, int Order);
public record RubricDto(Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<RubricItemDto> Items);

public record CreateRubricDto(string Name, string? Description, IReadOnlyList<CreateRubricItemDto> Items);
public record CreateRubricItemDto(string Label, int MaxScore, int Order);

public record CreateRubricCommand(CreateRubricDto Input) : IRequest<RubricDto>;

public class CreateRubricValidator : AbstractValidator<CreateRubricCommand>
{
    public CreateRubricValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(120);
        RuleForEach(x => x.Input.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Label).NotEmpty();
            i.RuleFor(x => x.MaxScore).GreaterThan(0);
        });
    }
}

public class CreateRubricHandler : IRequestHandler<CreateRubricCommand, RubricDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public CreateRubricHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = db; _user = user;
    }

    public async Task<RubricDto> Handle(CreateRubricCommand request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var input = request.Input;
        var rubric = new QaRubric
        {
            AgencyId = _user.AgencyId.Value,
            Name = input.Name,
            Description = input.Description
        };
        foreach (var i in input.Items)
        {
            rubric.Items.Add(new QaRubricItem
            {
                AgencyId = _user.AgencyId.Value,
                Label = i.Label,
                MaxScore = i.MaxScore,
                Order = i.Order
            });
        }
        _db.QaRubrics.Add(rubric);
        await _db.SaveChangesAsync(ct);

        return Map(rubric);
    }

    public static RubricDto Map(QaRubric r) =>
        new(r.Id, r.Name, r.Description, r.IsActive,
            r.Items.OrderBy(i => i.Order).Select(i => new RubricItemDto(i.Id, i.Label, i.MaxScore, i.Order)).ToList());
}

public record ListRubricsQuery() : IRequest<IReadOnlyList<RubricDto>>;
public class ListRubricsHandler : IRequestHandler<ListRubricsQuery, IReadOnlyList<RubricDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public ListRubricsHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<RubricDto>> Handle(ListRubricsQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var rubrics = await _db.QaRubrics.Include(r => r.Items)
            .Where(r => r.AgencyId == _user.AgencyId)
            .ToListAsync(ct);
        return rubrics.Select(CreateRubricHandler.Map).ToList();
    }
}

public record SubmitReviewItemDto(Guid RubricItemId, int Score, string? Comment);
public record SubmitReviewDto(Guid LeadId, Guid? SaleId, Guid AgentUserId, Guid RubricId,
    IReadOnlyList<SubmitReviewItemDto> Items, string? Notes);

public record ReviewDto(Guid Id, Guid LeadId, Guid AgentUserId, Guid ReviewerUserId, Guid RubricId,
    decimal TotalScore, decimal MaxScore, string? Notes, DateTime ReviewedAt);

public record SubmitReviewCommand(SubmitReviewDto Input) : IRequest<ReviewDto>;

public class SubmitReviewHandler : IRequestHandler<SubmitReviewCommand, ReviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public SubmitReviewHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = db; _user = user;
    }

    public async Task<ReviewDto> Handle(SubmitReviewCommand request, CancellationToken ct)
    {
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var input = request.Input;

        var rubric = await _db.QaRubrics.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == input.RubricId && r.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(QaRubric), input.RubricId);

        var max = rubric.Items.Sum(i => (decimal)i.MaxScore);
        var total = 0m;
        var review = new QaReview
        {
            AgencyId = _user.AgencyId.Value,
            LeadId = input.LeadId,
            SaleId = input.SaleId,
            AgentUserId = input.AgentUserId,
            ReviewerUserId = _user.UserId.Value,
            RubricId = input.RubricId,
            Notes = input.Notes,
            MaxScore = max
        };

        foreach (var item in input.Items)
        {
            var rubricItem = rubric.Items.FirstOrDefault(i => i.Id == item.RubricItemId)
                ?? throw new ConflictException($"Rubric item {item.RubricItemId} not found.");
            var bounded = Math.Max(0, Math.Min(item.Score, rubricItem.MaxScore));
            total += bounded;
            review.Items.Add(new QaReviewItem
            {
                AgencyId = _user.AgencyId.Value,
                RubricItemId = item.RubricItemId,
                Score = bounded,
                Comment = item.Comment
            });
        }
        review.TotalScore = total;

        _db.QaReviews.Add(review);
        await _db.SaveChangesAsync(ct);

        return new ReviewDto(review.Id, review.LeadId, review.AgentUserId, review.ReviewerUserId,
            review.RubricId, review.TotalScore, review.MaxScore, review.Notes, review.ReviewedAt);
    }
}
