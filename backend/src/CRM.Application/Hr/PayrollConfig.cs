using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using CcEntity = CRM.Domain.Entities.CallCenter;   // 'CallCenter' unqualified is a namespace here
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Hr;

// ── DTO ───────────────────────────────────────────────────────────────────────

/// <summary>A call centre's deduction rules (or the defaults, when it hasn't configured its own yet).</summary>
public record PayrollConfigDto(
    Guid CallCenterId, string CallCenterName,
    decimal LateComingFine, decimal HalfDayFactor, decimal AbsentDayFactor, decimal NcnsFactor,
    bool Saved);

public record SavePayrollConfigInput(
    decimal LateComingFine, decimal HalfDayFactor, decimal AbsentDayFactor, decimal NcnsFactor);

// ── Requests ──────────────────────────────────────────────────────────────────

public record GetPayrollConfigQuery(Guid CallCenterId) : IRequest<PayrollConfigDto>;
public record SavePayrollConfigCommand(Guid CallCenterId, SavePayrollConfigInput Input) : IRequest<PayrollConfigDto>;

/// <summary>
/// Bounds on the deduction rules. These factors multiply a day's pay for EVERY non-finalized payroll
/// row in the call centre and are re-applied on each read, so one bad value silently re-prices a
/// whole month for the whole centre. A negative factor would turn a deduction into a credit.
/// </summary>
public class SavePayrollConfigValidator : AbstractValidator<SavePayrollConfigCommand>
{
    public SavePayrollConfigValidator()
    {
        RuleFor(x => x.CallCenterId).NotEmpty();

        // NOTE: deliberately UPPER bounds only. The handler already floors a negative to zero
        // (Math.Max(0m, ...)), a forgiving behaviour covered by an integration test — rejecting
        // negatives here would break that contract. The real hazard was the top end: these factors
        // multiply a day's pay for every non-finalized row in the centre and are re-applied on each
        // read, so a mistyped 500 silently re-prices a whole month.
        RuleFor(x => x.Input.LateComingFine).LessThanOrEqualTo(1_000_000m)
            .WithMessage("That late-coming fine looks too large — check the figure.");
        // A factor is a fraction of one day's pay: 1 = a full day. A little above 1 is a legitimate
        // policy (a punitive no-show); 5 days' pay for one absence is a typo.
        RuleFor(x => x.Input.HalfDayFactor).LessThanOrEqualTo(5m)
            .WithMessage("The half-day factor can't exceed 5 days' pay.");
        RuleFor(x => x.Input.AbsentDayFactor).LessThanOrEqualTo(5m)
            .WithMessage("The absent-day factor can't exceed 5 days' pay.");
        RuleFor(x => x.Input.NcnsFactor).LessThanOrEqualTo(5m)
            .WithMessage("The no-call-no-show factor can't exceed 5 days' pay.");
    }
}

// ── Access ────────────────────────────────────────────────────────────────────

public static class PayrollConfigAccess
{
    /// <summary>
    /// ACCESS is gated by the <c>payroll.config</c> permission at the API (assignable via the Roles
    /// UI — HR/Admin/CEO/CallCenterAdmin hold it by default). This enforces SCOPE only: a user pinned
    /// to a call centre may configure only that centre; an agency-level holder may configure any
    /// centre in their agency (the tenant query filter enforces the agency boundary).
    /// </summary>
    public static void EnsureCanConfigure(ICurrentUser user, Guid callCenterId)
    {
        Guard.AgainstNull(user);
        if (user.CallCenterId is { } pinned && pinned != callCenterId)
            throw new ForbiddenAccessException("You can only configure your own call center's deduction rules.");
    }
}

// ── Shared calculator (used by both the config editor preview and payroll draft assembly) ──────

public static class PayrollDeductions
{
    /// <summary>
    /// Auto-computes the four attendance-driven deduction amounts from a call centre's rules.
    /// A day's pay = BasicSalary / WorkingDays; half/absent/NCNS are multiples of it, late is a flat fine.
    /// </summary>
    public static (decimal Late, decimal Half, decimal Absent, decimal Ncns) Auto(
        decimal basicSalary, int workingDays, int lateComing, int halfDays, int absentDays, int ncns,
        CallCenterPayrollConfig cfg)
    {
        var perDay = workingDays > 0 ? basicSalary / workingDays : 0m;
        static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
        return (
            R(lateComing * cfg.LateComingFine),
            R(halfDays * perDay * cfg.HalfDayFactor),
            R(absentDays * perDay * cfg.AbsentDayFactor),
            R(ncns * perDay * cfg.NcnsFactor));
    }
}

// ── Handlers ──────────────────────────────────────────────────────────────────

public class PayrollConfigHandlers :
    IRequestHandler<GetPayrollConfigQuery, PayrollConfigDto>,
    IRequestHandler<SavePayrollConfigCommand, PayrollConfigDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public PayrollConfigHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<PayrollConfigDto> Handle(GetPayrollConfigQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        PayrollConfigAccess.EnsureCanConfigure(_user, request.CallCenterId);
        var cc = await _db.CallCenters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CallCenterId, ct)
            ?? throw new NotFoundException("CallCenter", request.CallCenterId);
        var cfg = await _db.CallCenterPayrollConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CallCenterId == request.CallCenterId, ct);
        return ToDto(cc, cfg);
    }

    public async Task<PayrollConfigDto> Handle(SavePayrollConfigCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        PayrollConfigAccess.EnsureCanConfigure(_user, request.CallCenterId);
        var cc = await _db.CallCenters.FirstOrDefaultAsync(c => c.Id == request.CallCenterId, ct)
            ?? throw new NotFoundException("CallCenter", request.CallCenterId);

        var cfg = await _db.CallCenterPayrollConfigs
            .FirstOrDefaultAsync(x => x.CallCenterId == request.CallCenterId, ct);
        if (cfg is null)
        {
            cfg = new CallCenterPayrollConfig { AgencyId = cc.AgencyId, CallCenterId = cc.Id };
            _db.CallCenterPayrollConfigs.Add(cfg);
        }
        var i = request.Input;
        // Guard against negatives — a deduction rule can never be below zero.
        cfg.LateComingFine = Math.Max(0m, i.LateComingFine);
        cfg.HalfDayFactor = Math.Max(0m, i.HalfDayFactor);
        cfg.AbsentDayFactor = Math.Max(0m, i.AbsentDayFactor);
        cfg.NcnsFactor = Math.Max(0m, i.NcnsFactor);
        cfg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(cc, cfg);
    }

    private static PayrollConfigDto ToDto(CcEntity cc, CallCenterPayrollConfig? cfg) => new(
        cc.Id, cc.Name,
        cfg?.LateComingFine ?? PayrollConfigDefaults.LateComingFine,
        cfg?.HalfDayFactor ?? PayrollConfigDefaults.HalfDayFactor,
        cfg?.AbsentDayFactor ?? PayrollConfigDefaults.AbsentDayFactor,
        cfg?.NcnsFactor ?? PayrollConfigDefaults.NcnsFactor,
        cfg is not null);
}
