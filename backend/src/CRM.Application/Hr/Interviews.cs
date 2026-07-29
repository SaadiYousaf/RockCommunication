using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Hr;

// ── DTOs ────────────────────────────────────────────────────────────────────

public record InterviewDto(
    Guid Id, DateTime? InterviewDate, string CandidateName, string? Cnic, string? PhoneNumber,
    string? PositionAppliedFor, string? Experience, decimal? ExpectedSalary, DateTime? AbleToJoinOn,
    OfferStatus Status, string? PositionOffered, decimal? SalaryOffered, string? Remarks,
    DateTime? TrainingScheduledAt, DateTime CreatedAt);

public record InterviewInput(
    DateTime? InterviewDate, string CandidateName, string? Cnic, string? PhoneNumber,
    string? PositionAppliedFor, string? Experience, decimal? ExpectedSalary, DateTime? AbleToJoinOn,
    OfferStatus Status, string? PositionOffered, decimal? SalaryOffered, string? Remarks,
    DateTime? TrainingScheduledAt);

// ── Requests ────────────────────────────────────────────────────────────────

public record ListInterviewsQuery(string? Search = null, OfferStatus? Status = null)
    : IRequest<IReadOnlyList<InterviewDto>>;
public record GetInterviewQuery(Guid Id) : IRequest<InterviewDto>;
public record CreateInterviewCommand(InterviewInput Input) : IRequest<InterviewDto>;
public record UpdateInterviewCommand(Guid Id, InterviewInput Input) : IRequest<InterviewDto>;
public record DeleteInterviewCommand(Guid Id) : IRequest<Unit>;

// ── Validation ──────────────────────────────────────────────────────────────

public class InterviewInputValidator : AbstractValidator<InterviewInput>
{
    public InterviewInputValidator()
    {
        RuleFor(x => x.CandidateName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.PositionAppliedFor).MaximumLength(120);
        RuleFor(x => x.PositionOffered).MaximumLength(120);
        RuleFor(x => x.ExpectedSalary).GreaterThanOrEqualTo(0).When(x => x.ExpectedSalary.HasValue);
        RuleFor(x => x.SalaryOffered).GreaterThanOrEqualTo(0).When(x => x.SalaryOffered.HasValue);
    }
}
public class CreateInterviewValidator : AbstractValidator<CreateInterviewCommand>
{ public CreateInterviewValidator() => RuleFor(x => x.Input).SetValidator(new InterviewInputValidator()); }
public class UpdateInterviewValidator : AbstractValidator<UpdateInterviewCommand>
{ public UpdateInterviewValidator() => RuleFor(x => x.Input).SetValidator(new InterviewInputValidator()); }

// ── Handlers ────────────────────────────────────────────────────────────────

public class InterviewHandlers :
    IRequestHandler<ListInterviewsQuery, IReadOnlyList<InterviewDto>>,
    IRequestHandler<GetInterviewQuery, InterviewDto>,
    IRequestHandler<CreateInterviewCommand, InterviewDto>,
    IRequestHandler<UpdateInterviewCommand, InterviewDto>,
    IRequestHandler<DeleteInterviewCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public InterviewHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<InterviewDto>> Handle(ListInterviewsQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var q = _db.Interviews.AsNoTracking().AsQueryable();
        if (request.Status is { } s) q = q.Where(i => i.Status == s);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var t = request.Search.Trim();
            q = q.Where(i => i.CandidateName.Contains(t)
                || (i.PhoneNumber != null && i.PhoneNumber.Contains(t))
                || (i.PositionAppliedFor != null && i.PositionAppliedFor.Contains(t)));
        }
        var rows = await q.OrderByDescending(i => i.InterviewDate ?? i.CreatedAt).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<InterviewDto> Handle(GetInterviewQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var i = await _db.Interviews.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Interview), request.Id);
        return Map(i);
    }

    public async Task<InterviewDto> Handle(CreateInterviewCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var i = new Interview { AgencyId = _user.AgencyId ?? Guid.Empty };
        Apply(i, request.Input);
        _db.Interviews.Add(i);
        await _db.SaveChangesAsync(ct);
        return Map(i);
    }

    public async Task<InterviewDto> Handle(UpdateInterviewCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var i = await _db.Interviews.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Interview), request.Id);
        Apply(i, request.Input);
        i.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(i);
    }

    public async Task<Unit> Handle(DeleteInterviewCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var i = await _db.Interviews.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Interview), request.Id);
        i.IsDeleted = true;
        i.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static void Apply(Interview i, InterviewInput input)
    {
        i.InterviewDate = input.InterviewDate;
        i.CandidateName = input.CandidateName.Trim();
        i.Cnic = Clean(input.Cnic);
        i.PhoneNumber = Clean(input.PhoneNumber);
        i.PositionAppliedFor = Clean(input.PositionAppliedFor);
        i.Experience = Clean(input.Experience);
        i.ExpectedSalary = input.ExpectedSalary;
        i.AbleToJoinOn = input.AbleToJoinOn;
        i.Status = input.Status;
        i.PositionOffered = Clean(input.PositionOffered);
        i.SalaryOffered = input.SalaryOffered;
        i.Remarks = Clean(input.Remarks);
        i.TrainingScheduledAt = input.TrainingScheduledAt;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static InterviewDto Map(Interview i) => new(
        i.Id, i.InterviewDate, i.CandidateName, i.Cnic, i.PhoneNumber, i.PositionAppliedFor, i.Experience,
        i.ExpectedSalary, i.AbleToJoinOn, i.Status, i.PositionOffered, i.SalaryOffered, i.Remarks,
        i.TrainingScheduledAt, i.CreatedAt);
}
