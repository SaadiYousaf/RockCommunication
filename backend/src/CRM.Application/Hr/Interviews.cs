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

public record UpcomingTrainingDto(Guid Id, string CandidateName, string? PositionOffered, DateTime TrainingScheduledAt);
/// <summary>Candidates with a training scheduled within the next N days (soonest first).</summary>
public record UpcomingTrainingsQuery(int Days = 14) : IRequest<IReadOnlyList<UpcomingTrainingDto>>;
public record GetInterviewQuery(Guid Id) : IRequest<InterviewDto>;
public record CreateInterviewCommand(InterviewInput Input) : IRequest<InterviewDto>;
public record UpdateInterviewCommand(Guid Id, InterviewInput Input) : IRequest<InterviewDto>;
public record DeleteInterviewCommand(Guid Id) : IRequest<Unit>;
/// <summary>Move several candidates to one offer status at once (only the status field is touched,
/// so a concurrent edit to other fields is never clobbered). Returns the number updated.</summary>
public record BulkSetInterviewStatusCommand(IReadOnlyList<Guid> Ids, OfferStatus Status) : IRequest<int>;

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
    IRequestHandler<UpcomingTrainingsQuery, IReadOnlyList<UpcomingTrainingDto>>,
    IRequestHandler<GetInterviewQuery, InterviewDto>,
    IRequestHandler<CreateInterviewCommand, InterviewDto>,
    IRequestHandler<UpdateInterviewCommand, InterviewDto>,
    IRequestHandler<DeleteInterviewCommand, Unit>,
    IRequestHandler<BulkSetInterviewStatusCommand, int>
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

    public async Task<IReadOnlyList<UpcomingTrainingDto>> Handle(UpcomingTrainingsQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var now = DateTime.UtcNow;
        var until = now.AddDays(request.Days);
        return await _db.Interviews.AsNoTracking()
            .Where(i => i.TrainingScheduledAt != null && i.TrainingScheduledAt >= now && i.TrainingScheduledAt <= until)
            .OrderBy(i => i.TrainingScheduledAt)
            .Select(i => new UpcomingTrainingDto(i.Id, i.CandidateName, i.PositionOffered, i.TrainingScheduledAt!.Value))
            .ToListAsync(ct);
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

    public async Task<int> Handle(BulkSetInterviewStatusCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        if (request.Ids is null || request.Ids.Count == 0) return 0;
        var ids = request.Ids.Distinct().ToList();
        var rows = await _db.Interviews.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        var now = DateTime.UtcNow;
        foreach (var i in rows) { i.Status = request.Status; i.UpdatedAt = now; }
        await _db.SaveChangesAsync(ct);
        return rows.Count;
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
        // Rescheduling (or clearing) the training re-arms the reminder so a fresh one fires for the new time.
        if (i.TrainingScheduledAt != input.TrainingScheduledAt) i.TrainingReminderSentAt = null;
        i.TrainingScheduledAt = input.TrainingScheduledAt;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static InterviewDto Map(Interview i) => new(
        i.Id, i.InterviewDate, i.CandidateName, i.Cnic, i.PhoneNumber, i.PositionAppliedFor, i.Experience,
        i.ExpectedSalary, i.AbleToJoinOn, i.Status, i.PositionOffered, i.SalaryOffered, i.Remarks,
        i.TrainingScheduledAt, i.CreatedAt);
}
