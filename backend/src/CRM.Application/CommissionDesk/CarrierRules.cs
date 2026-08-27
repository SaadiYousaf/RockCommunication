using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.CommissionDesk;

/// <summary>A carrier's advancing terms — the rate it pays and how many months it advances.</summary>
public record CarrierRuleDto(Guid Id, string Carrier, decimal CommissionRate, int AdvancedMonths,
    string? Notes, bool IsActive);

public record ListCarrierRulesQuery(bool IncludeInactive = true) : IRequest<IReadOnlyList<CarrierRuleDto>>;
public record UpsertCarrierRuleCommand(Guid? Id, string Carrier, decimal CommissionRate, int AdvancedMonths,
    string? Notes, bool IsActive = true) : IRequest<CarrierRuleDto>;
public record DeleteCarrierRuleCommand(Guid Id) : IRequest<Unit>;

public class UpsertCarrierRuleValidator : AbstractValidator<UpsertCarrierRuleCommand>
{
    public UpsertCarrierRuleValidator()
    {
        RuleFor(x => x.Carrier).NotEmpty().MaximumLength(120);
        RuleFor(x => x.CommissionRate).InclusiveBetween(0m, 200m)
            .WithMessage("Commission rate must be a percentage between 0 and 200.");
        RuleFor(x => x.AdvancedMonths).InclusiveBetween(0, 120)
            .WithMessage("Advanced months must be between 0 and 120.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>
/// CRUD for the GLOBAL carrier advancing rules. Global by design: one carrier advances the same way
/// for every agency, and the Commission Agent works cross-agency — so these are a
/// <see cref="CarrierAdvancingRule"/> (BaseEntity), not tenant rows. The commission desk joins them
/// into its sales list read-only; this is the only place they're edited.
/// </summary>
public class CarrierRulesHandler :
    IRequestHandler<ListCarrierRulesQuery, IReadOnlyList<CarrierRuleDto>>,
    IRequestHandler<UpsertCarrierRuleCommand, CarrierRuleDto>,
    IRequestHandler<DeleteCarrierRuleCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public CarrierRulesHandler(IApplicationDbContext db, ICurrentUser user)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    /// <summary>
    /// Carrier rules are GLOBAL — one row drives the advancing figures on EVERY agency's desk. So a
    /// write here is a platform-wide change and must not be reachable by an agency-scoped holder of
    /// the permission; only a cross-agency commission agent or a SuperAdmin may make one. Reads stay
    /// open to anyone with the permission, since the desk needs them to render.
    /// </summary>
    private void EnsureMayWriteGlobalRules()
    {
        if (_user.IsSuperAdmin) return;
        if (DomainRoles.IsCentralCommissionAgent(_user.AgencyId, _user.Roles)) return;
        throw new ForbiddenAccessException();
    }

    public async Task<IReadOnlyList<CarrierRuleDto>> Handle(ListCarrierRulesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();

        var q = _db.CarrierAdvancingRules.AsNoTracking().Where(r => !r.IsDeleted);
        if (!request.IncludeInactive) q = q.Where(r => r.IsActive);
        return await q.OrderBy(r => r.Carrier)
            .Select(r => new CarrierRuleDto(r.Id, r.Carrier, r.CommissionRate, r.AdvancedMonths, r.Notes, r.IsActive))
            .ToListAsync(ct);
    }

    public async Task<CarrierRuleDto> Handle(UpsertCarrierRuleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        EnsureMayWriteGlobalRules();

        var carrier = request.Carrier.Trim();

        // One rule per carrier — otherwise a sale couldn't resolve which terms apply.
        var clash = await _db.CarrierAdvancingRules
            .FirstOrDefaultAsync(r => !r.IsDeleted && r.Carrier.ToLower() == carrier.ToLower()
                                      && (request.Id == null || r.Id != request.Id), ct);
        if (clash is not null)
            throw new ConflictException($"A rule for \"{carrier}\" already exists.");

        CarrierAdvancingRule rule;
        if (request.Id is { } id)
        {
            rule = await _db.CarrierAdvancingRules.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
                ?? throw new NotFoundException(nameof(CarrierAdvancingRule), id);
        }
        else
        {
            rule = new CarrierAdvancingRule();
            _db.CarrierAdvancingRules.Add(rule);
        }

        rule.Carrier = carrier;
        rule.CommissionRate = request.CommissionRate;
        rule.AdvancedMonths = request.AdvancedMonths;
        rule.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        rule.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return new CarrierRuleDto(rule.Id, rule.Carrier, rule.CommissionRate, rule.AdvancedMonths, rule.Notes, rule.IsActive);
    }

    public async Task<Unit> Handle(DeleteCarrierRuleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        EnsureMayWriteGlobalRules();

        var rule = await _db.CarrierAdvancingRules.FirstOrDefaultAsync(r => r.Id == request.Id && !r.IsDeleted, ct);
        if (rule is null) return Unit.Value;      // already gone — idempotent
        _db.CarrierAdvancingRules.Remove(rule);   // audit interceptor turns this into a soft delete
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
