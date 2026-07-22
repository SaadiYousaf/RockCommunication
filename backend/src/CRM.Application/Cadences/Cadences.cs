using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Cadences;

public record CadenceStepDto(Guid Id, int Order, string StepKind, int DelayMinutes, string? ParametersJson, bool StopIfContacted);

public record CadenceDto(Guid Id, string Name, Guid? CampaignId, bool IsActive, string? Description,
    IReadOnlyList<CadenceStepDto> Steps);

public record UpsertCadenceStepDto(int Order, string StepKind, int DelayMinutes, string? ParametersJson, bool StopIfContacted);

public record UpsertCadenceCommand(Guid? Id, string Name, Guid? CampaignId, bool IsActive, string? Description,
    IReadOnlyList<UpsertCadenceStepDto> Steps) : IRequest<CadenceDto>;

public record ListCadencesQuery() : IRequest<IReadOnlyList<CadenceDto>>;
public record EnrollLeadInCadenceCommand(Guid CadenceId, Guid LeadId) : IRequest<Unit>;
public record StopCadenceEnrollmentCommand(Guid EnrollmentId, string? Reason) : IRequest<Unit>;
public record ListEnrollmentsQuery(Guid? CadenceId, string? Status, int Take = 100)
    : IRequest<IReadOnlyList<CadenceEnrollmentDto>>;
public record CadenceEnrollmentDto(Guid Id, Guid CadenceId, Guid LeadId, int CurrentStepOrder,
    DateTime EnrolledAt, DateTime NextRunAt, string Status, DateTime? CompletedAt, string? StopReason);

public class UpsertCadenceValidator : AbstractValidator<UpsertCadenceCommand>
{
    public UpsertCadenceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleForEach(x => x.Steps).ChildRules(s =>
        {
            s.RuleFor(x => x.StepKind).NotEmpty();
            s.RuleFor(x => x.DelayMinutes).GreaterThanOrEqualTo(0);
        });
    }
}

public class CadenceHandler :
    IRequestHandler<UpsertCadenceCommand, CadenceDto>,
    IRequestHandler<ListCadencesQuery, IReadOnlyList<CadenceDto>>,
    IRequestHandler<EnrollLeadInCadenceCommand, Unit>,
    IRequestHandler<StopCadenceEnrollmentCommand, Unit>,
    IRequestHandler<ListEnrollmentsQuery, IReadOnlyList<CadenceEnrollmentDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public CadenceHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<CadenceDto> Handle(UpsertCadenceCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        Cadence c;
        if (request.Id is { } id)
        {
            c = await _db.Cadences.Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(Cadence), id);
            _db.CadenceSteps.RemoveRange(c.Steps);
            c.Steps.Clear();
        }
        else { c = new Cadence { AgencyId = _user.AgencyId!.Value }; _db.Cadences.Add(c); }
        c.Name = request.Name.Trim();
        c.CampaignId = request.CampaignId;
        c.IsActive = request.IsActive;
        c.Description = request.Description;

        foreach (var s in request.Steps.OrderBy(x => x.Order))
        {
            c.Steps.Add(new CadenceStep
            {
                AgencyId = c.AgencyId,
                Order = s.Order,
                StepKind = s.StepKind,
                DelayMinutes = s.DelayMinutes,
                ParametersJson = s.ParametersJson,
                StopIfContacted = s.StopIfContacted
            });
        }
        await _db.SaveChangesAsync(ct);
        return Map(c);
    }

    public async Task<IReadOnlyList<CadenceDto>> Handle(ListCadencesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        var list = await _db.Cadences.Include(c => c.Steps)
            .Where(c => c.AgencyId == _user.AgencyId)
            .ToListAsync(ct);
        return list.Select(Map).ToList();
    }

    public async Task<Unit> Handle(EnrollLeadInCadenceCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var cadence = await _db.Cadences.Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == request.CadenceId && c.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Cadence), request.CadenceId);
        if (!cadence.IsActive) throw new ConflictException("Cadence not active.");

        var existing = await _db.CadenceEnrollments
            .FirstOrDefaultAsync(e => e.CadenceId == cadence.Id && e.LeadId == request.LeadId, ct);
        if (existing is not null)
        {
            if (existing.Status == "Active") return Unit.Value;
            existing.Status = "Active";
            existing.CurrentStepOrder = 0;
            existing.NextRunAt = DateTime.UtcNow.AddMinutes(cadence.Steps.OrderBy(s => s.Order).First().DelayMinutes);
            await _db.SaveChangesAsync(ct);
            return Unit.Value;
        }

        var firstStep = cadence.Steps.OrderBy(s => s.Order).FirstOrDefault();
        _db.CadenceEnrollments.Add(new CadenceEnrollment
        {
            AgencyId = cadence.AgencyId,
            CadenceId = cadence.Id,
            LeadId = request.LeadId,
            CurrentStepOrder = 0,
            NextRunAt = firstStep is null ? DateTime.UtcNow : DateTime.UtcNow.AddMinutes(firstStep.DelayMinutes),
            Status = "Active"
        });
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public async Task<Unit> Handle(StopCadenceEnrollmentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        var en = await _db.CadenceEnrollments
            .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId && e.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(CadenceEnrollment), request.EnrollmentId);
        en.Status = "Stopped";
        en.StopReason = request.Reason;
        en.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public async Task<IReadOnlyList<CadenceEnrollmentDto>> Handle(ListEnrollmentsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        var q = _db.CadenceEnrollments.Where(e => e.AgencyId == _user.AgencyId);
        if (request.CadenceId is { } cid) q = q.Where(e => e.CadenceId == cid);
        if (!string.IsNullOrEmpty(request.Status)) q = q.Where(e => e.Status == request.Status);
        return await q.OrderByDescending(e => e.EnrolledAt).Take(request.Take)
            .Select(e => new CadenceEnrollmentDto(e.Id, e.CadenceId, e.LeadId, e.CurrentStepOrder,
                e.EnrolledAt, e.NextRunAt, e.Status, e.CompletedAt, e.StopReason))
            .ToListAsync(ct);
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager"))
            throw new ForbiddenAccessException();
    }

    private static CadenceDto Map(Cadence c) =>
        new(c.Id, c.Name, c.CampaignId, c.IsActive, c.Description,
            c.Steps.OrderBy(s => s.Order).Select(s =>
                new CadenceStepDto(s.Id, s.Order, s.StepKind, s.DelayMinutes, s.ParametersJson, s.StopIfContacted)).ToList());
}
