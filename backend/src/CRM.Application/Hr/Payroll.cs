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

/// <summary>One employee's payroll for a month — values plus the computed totals.</summary>
public record PayrollRowDto(
    Guid EmployeeId, string FullName, string AgentCode, Guid? CallCenterId, string? CallCenterName, int Year, int Month,
    decimal BasicSalary, decimal Punctuality, decimal DailyBonus, decimal MonthlyCommissions,
    decimal TransportAllowance, decimal SpecialAllowance, decimal AdvanceSalary, decimal Docks,
    int WorkingDays, int PresentDays, int LeavesApproved, int LateComing, int HalfDays, int AbsentDays, int Ncns,
    // The "amount against" each attendance-driven deduction line (its day count is the number).
    decimal LateComingAmount, decimal HalfDaysAmount, decimal AbsentDaysAmount, decimal NcnsAmount,
    decimal GrossEarnings, decimal Deductions, decimal NetPay,
    string? Notes, bool Finalized, bool Saved);

public record SavePayrollInput(
    decimal BasicSalary, decimal Punctuality, decimal DailyBonus, decimal MonthlyCommissions,
    decimal TransportAllowance, decimal SpecialAllowance, decimal AdvanceSalary, decimal Docks,
    int WorkingDays, int PresentDays, int LeavesApproved, int LateComing, int HalfDays, int AbsentDays, int Ncns,
    decimal LateComingAmount, decimal HalfDaysAmount, decimal AbsentDaysAmount, decimal NcnsAmount,
    string? Notes, bool Finalized);

// ── Requests ────────────────────────────────────────────────────────────────

public record ListPayrollQuery(int Year, int Month, Guid? CallCenterId = null)
    : IRequest<IReadOnlyList<PayrollRowDto>>;
public record GetPayrollSlipQuery(Guid EmployeeId, int Year, int Month) : IRequest<PayrollRowDto>;
public record SavePayrollCommand(Guid EmployeeId, int Year, int Month, SavePayrollInput Input)
    : IRequest<PayrollRowDto>;

// Reject an out-of-range year/month before the handler builds a DateTime from it — otherwise an
// invalid month throws (HTTP 500) on read, and persists an unreachable junk row on save.
public class ListPayrollValidator : AbstractValidator<ListPayrollQuery>
{
    public ListPayrollValidator()
    {
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).InclusiveBetween(2000, 9999);
    }
}
public class GetPayrollSlipValidator : AbstractValidator<GetPayrollSlipQuery>
{
    public GetPayrollSlipValidator()
    {
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).InclusiveBetween(2000, 9999);
    }
}
/// <summary>
/// Bounds on a saved payslip. Only the identifiers and dates used to be checked, so every money and
/// day-count field was unbounded: a negative basic salary or an inflated advance produced a negative
/// net pay, and a zero/negative WorkingDays either zeroed every per-day deduction or inverted the
/// per-day wage so deductions became credits. Finalising then froze the junk as an authoritative
/// snapshot. Ranges are generous on purpose — they catch typos and sign errors, not policy choices.
/// </summary>
public class SavePayrollValidator : AbstractValidator<SavePayrollCommand>
{
    private const decimal MaxMoney = 100_000_000m;   // far above any real monthly figure

    public SavePayrollValidator()
    {
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).InclusiveBetween(2000, 9999);

        // Money — never negative, and sane at the top end.
        RuleFor(x => x.Input.BasicSalary).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.Punctuality).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.DailyBonus).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.MonthlyCommissions).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.TransportAllowance).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.SpecialAllowance).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.AdvanceSalary).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.Docks).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.LateComingAmount).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.HalfDaysAmount).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.AbsentDaysAmount).InclusiveBetween(0m, MaxMoney);
        RuleFor(x => x.Input.NcnsAmount).InclusiveBetween(0m, MaxMoney);

        // A month must have working days — 0 silently zeroes every per-day deduction.
        RuleFor(x => x.Input.WorkingDays).InclusiveBetween(1, 31)
            .WithMessage("Working days must be between 1 and 31.");

        // Day counts: non-negative and within a month.
        RuleFor(x => x.Input.PresentDays).InclusiveBetween(0, 31);
        RuleFor(x => x.Input.LeavesApproved).InclusiveBetween(0, 31);
        RuleFor(x => x.Input.LateComing).InclusiveBetween(0, 31);
        RuleFor(x => x.Input.HalfDays).InclusiveBetween(0, 31);
        RuleFor(x => x.Input.AbsentDays).InclusiveBetween(0, 31);
        RuleFor(x => x.Input.Ncns).InclusiveBetween(0, 31);

        // The attendance breakdown can't exceed the month it belongs to.
        RuleFor(x => x.Input)
            .Must(i => i.PresentDays + i.LeavesApproved + i.AbsentDays + i.Ncns <= i.WorkingDays)
            .WithMessage("Present, leave, absent and no-show days can't add up to more than the working days.");
    }
}

// ── Handlers ────────────────────────────────────────────────────────────────

public class PayrollHandlers :
    IRequestHandler<ListPayrollQuery, IReadOnlyList<PayrollRowDto>>,
    IRequestHandler<GetPayrollSlipQuery, PayrollRowDto>,
    IRequestHandler<SavePayrollCommand, PayrollRowDto>
{
    private const string LicenseAgentRule = CommissionRuleNames.LicenseAgentApproval;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public PayrollHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<PayrollRowDto>> Handle(ListPayrollQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var employees = await EmployeesQuery(request.CallCenterId).ToListAsync(ct);
        var empIds = employees.Select(e => e.Id).ToList();

        var saved = (await _db.EmployeePayrolls.AsNoTracking()
                .Where(p => p.Year == request.Year && p.Month == request.Month && empIds.Contains(p.EmployeeId))
                .ToListAsync(ct))
            .ToDictionary(p => p.EmployeeId);

        var attendance = await AttendanceCountsAsync(empIds, request.Year, request.Month, ct);
        var commissions = await CommissionByUserAsync(employees, request.Year, request.Month, ct);
        var prior = await PriorMonthAsync(empIds, request.Year, request.Month, ct);
        var (configs, names) = await LoadCallCenterInfoAsync(employees.Select(e => e.CallCenterId), ct);

        return employees.Select(e => Build(e, request.Year, request.Month,
            saved.GetValueOrDefault(e.Id), attendance.GetValueOrDefault(e.Id), prior.GetValueOrDefault(e.Id),
            e.UserId is { } uid ? commissions.GetValueOrDefault(uid) : 0m,
            CfgFor(e.CallCenterId, configs), NameFor(e.CallCenterId, names))).ToList();
    }

    public async Task<PayrollRowDto> Handle(GetPayrollSlipQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.EmployeeId, ct)
            ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);
        var saved = await _db.EmployeePayrolls.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmployeeId == e.Id && p.Year == request.Year && p.Month == request.Month, ct);
        var att = (await AttendanceCountsAsync(new List<Guid> { e.Id }, request.Year, request.Month, ct)).GetValueOrDefault(e.Id);
        var prior = (await PriorMonthAsync(new List<Guid> { e.Id }, request.Year, request.Month, ct)).GetValueOrDefault(e.Id);
        var comm = e.UserId is { } uid
            ? (await CommissionByUserAsync(new List<Employee> { e }, request.Year, request.Month, ct)).GetValueOrDefault(uid)
            : 0m;
        var (configs, names) = await LoadCallCenterInfoAsync(new[] { e.CallCenterId }, ct);
        return Build(e, request.Year, request.Month, saved, att, prior, comm,
            CfgFor(e.CallCenterId, configs), NameFor(e.CallCenterId, names));
    }

    public async Task<PayrollRowDto> Handle(SavePayrollCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == request.EmployeeId, ct)
            ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);
        var p = await _db.EmployeePayrolls
            .FirstOrDefaultAsync(x => x.EmployeeId == e.Id && x.Year == request.Year && x.Month == request.Month, ct);
        if (p is null)
        {
            p = new EmployeePayroll { AgencyId = e.AgencyId, EmployeeId = e.Id, Year = request.Year, Month = request.Month };
            _db.EmployeePayrolls.Add(p);
        }
        var (configs, names) = await LoadCallCenterInfoAsync(new[] { e.CallCenterId }, ct);
        var cfg = CfgFor(e.CallCenterId, configs);
        Apply(p, request.Input, cfg);
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Build(e, request.Year, request.Month, p, null, null, 0m, cfg, NameFor(e.CallCenterId, names));
    }

    // ── Draft assembly ──────────────────────────────────────────────────────

    private static PayrollRowDto Build(Employee e, int year, int month, EmployeePayroll? saved,
        AttendanceCounts? att, EmployeePayroll? prior, decimal liveCommission,
        CallCenterPayrollConfig cfg, string? callCenterName)
    {
        // Saved values win; otherwise derive a draft (attendance live, commission live, basic+advance carried).
        var basic = saved?.BasicSalary ?? prior?.BasicSalary ?? 0m;
        var commission = saved?.MonthlyCommissions ?? liveCommission;
        var advance = saved?.AdvanceSalary ?? prior?.AdvanceSalary ?? 0m;
        var a = att ?? default;

        var present = saved?.PresentDays ?? a.Present;
        var late = saved?.LateComing ?? a.Late;
        var half = saved?.HalfDays ?? a.Half;
        var leave = saved?.LeavesApproved ?? a.Leave;
        var absent = saved?.AbsentDays ?? a.Absent;
        var ncns = saved?.Ncns ?? a.Ncns;
        var working = saved?.WorkingDays ?? DateTime.DaysInMonth(year, month);

        var punctuality = saved?.Punctuality ?? 0m;
        var dailyBonus = saved?.DailyBonus ?? 0m;
        var transport = saved?.TransportAllowance ?? 0m;
        var special = saved?.SpecialAllowance ?? 0m;
        var docks = saved?.Docks ?? 0m;

        // Per-line deduction amounts are ALWAYS derived from a day's pay (basic ÷ working days) ×
        // the count × the call centre's rule — so they track the daily wage no matter how basic or
        // working-days changed (edit modal, bulk "Set pay", or a direct save). A FINALIZED row is a
        // frozen snapshot and keeps its stored amounts; everything else recomputes (self-healing).
        var auto = PayrollDeductions.Auto(basic, working, late, half, absent, ncns, cfg);
        var isFinal = saved?.Finalized ?? false;
        var lateAmt = isFinal ? saved!.LateComingAmount : auto.Late;
        var halfAmt = isFinal ? saved!.HalfDaysAmount : auto.Half;
        var absentAmt = isFinal ? saved!.AbsentDaysAmount : auto.Absent;
        var ncnsAmt = isFinal ? saved!.NcnsAmount : auto.Ncns;

        var gross = basic + punctuality + dailyBonus + commission + transport + special;
        var deductions = advance + docks + lateAmt + halfAmt + absentAmt + ncnsAmt;
        var net = gross - deductions;

        return new PayrollRowDto(e.Id, e.FullName, e.AgentCode, e.CallCenterId, callCenterName, year, month,
            basic, punctuality, dailyBonus, commission, transport, special, advance, docks,
            working, present, leave, late, half, absent, ncns,
            lateAmt, halfAmt, absentAmt, ncnsAmt,
            gross, deductions, net, saved?.Notes, saved?.Finalized ?? false, saved is not null);
    }

    private static void Apply(EmployeePayroll p, SavePayrollInput i, CallCenterPayrollConfig cfg)
    {
        // A row that was already finalized is a frozen snapshot: keep its stored deduction amounts even
        // if the centre's rates changed later, so an unrelated edit (e.g. a Notes typo) can't silently
        // re-price it. Recompute only for drafts and for the draft→finalized transition (freezing the
        // then-current figures).
        var wasFinalized = p.Finalized;
        p.BasicSalary = i.BasicSalary; p.Punctuality = i.Punctuality; p.DailyBonus = i.DailyBonus;
        p.MonthlyCommissions = i.MonthlyCommissions; p.TransportAllowance = i.TransportAllowance;
        p.SpecialAllowance = i.SpecialAllowance; p.AdvanceSalary = i.AdvanceSalary; p.Docks = i.Docks;
        p.WorkingDays = i.WorkingDays; p.PresentDays = i.PresentDays; p.LeavesApproved = i.LeavesApproved;
        p.LateComing = i.LateComing; p.HalfDays = i.HalfDays; p.AbsentDays = i.AbsentDays; p.Ncns = i.Ncns;
        if (!(wasFinalized && i.Finalized))
        {
            // Attendance-driven deduction amounts are derived server-side from the daily wage
            // (basic ÷ working days) × count × rule — never taken from the client — so they can never
            // drift out of step with the basic/working-days that were just saved.
            var auto = PayrollDeductions.Auto(i.BasicSalary, i.WorkingDays, i.LateComing, i.HalfDays, i.AbsentDays, i.Ncns, cfg);
            p.LateComingAmount = auto.Late; p.HalfDaysAmount = auto.Half;
            p.AbsentDaysAmount = auto.Absent; p.NcnsAmount = auto.Ncns;
        }
        p.Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim();
        p.Finalized = i.Finalized;
    }

    private IQueryable<Employee> EmployeesQuery(Guid? callCenterId)
    {
        var q = _db.Employees.AsNoTracking().AsQueryable();
        if (callCenterId is { } cc) q = q.Where(e => e.CallCenterId == cc);
        return q.OrderBy(e => e.FullName);
    }

    // Every call centre without its own saved config falls back to this default rule set.
    private static readonly CallCenterPayrollConfig DefaultConfig = new();

    /// <summary>Loads the deduction config + display name for each employee's call centre in one pass.</summary>
    private async Task<(Dictionary<Guid, CallCenterPayrollConfig> Configs, Dictionary<Guid, string> Names)>
        LoadCallCenterInfoAsync(IEnumerable<Guid?> callCenterIds, CancellationToken ct)
    {
        var ids = callCenterIds.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return (new(), new());
        var configs = (await _db.CallCenterPayrollConfigs.AsNoTracking()
                .Where(c => ids.Contains(c.CallCenterId)).ToListAsync(ct))
            .ToDictionary(c => c.CallCenterId);
        var names = (await _db.CallCenters.AsNoTracking()
                .Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.Name);
        return (configs, names);
    }

    private static CallCenterPayrollConfig CfgFor(Guid? ccId, Dictionary<Guid, CallCenterPayrollConfig> configs)
        => ccId is { } id && configs.TryGetValue(id, out var c) ? c : DefaultConfig;

    private static string? NameFor(Guid? ccId, Dictionary<Guid, string> names)
        => ccId is { } id && names.TryGetValue(id, out var n) ? n : null;

    private readonly record struct AttendanceCounts(int Present, int Absent, int Late, int Half, int Leave, int Ncns);

    private async Task<Dictionary<Guid, AttendanceCounts>> AttendanceCountsAsync(
        List<Guid> empIds, int year, int month, CancellationToken ct)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var rows = await _db.EmployeeAttendances.AsNoTracking()
            .Where(a => a.Date >= start && a.Date < end && empIds.Contains(a.EmployeeId))
            .Select(a => new { a.EmployeeId, a.Status }).ToListAsync(ct);
        return rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => new AttendanceCounts(
            g.Count(x => x.Status == AttendanceStatus.Present),
            g.Count(x => x.Status == AttendanceStatus.Absent),
            g.Count(x => x.Status == AttendanceStatus.Late),
            g.Count(x => x.Status == AttendanceStatus.HalfDay),
            g.Count(x => x.Status == AttendanceStatus.Leave),
            g.Count(x => x.Status == AttendanceStatus.Ncns)));
    }

    /// <summary>Prior month's saved payroll per employee — for carrying basic + advance forward.</summary>
    private async Task<Dictionary<Guid, EmployeePayroll>> PriorMonthAsync(
        List<Guid> empIds, int year, int month, CancellationToken ct)
    {
        var pm = month == 1 ? 12 : month - 1;
        var py = month == 1 ? year - 1 : year;
        return (await _db.EmployeePayrolls.AsNoTracking()
                .Where(p => p.Year == py && p.Month == pm && empIds.Contains(p.EmployeeId)).ToListAsync(ct))
            .ToDictionary(p => p.EmployeeId);
    }

    /// <summary>Live commission for the month, summed per linked user (in memory — SQLite decimal SUM).</summary>
    private async Task<Dictionary<Guid, decimal>> CommissionByUserAsync(
        List<Employee> employees, int year, int month, CancellationToken ct)
    {
        var userIds = employees.Where(e => e.UserId is { } && e.UserId != Guid.Empty)
            .Select(e => e.UserId!.Value).Distinct().ToList();
        if (userIds.Count == 0) return new();
        var agencyIds = employees.Select(e => e.AgencyId).Distinct().ToList();
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        // Payroll spans the WHOLE agency (Employee is agency-scoped), but CommissionEntry is a
        // CallCenterEntity — without bypassing the filter, a call-centre-pinned HR/processor would see
        // 0 commission for agents in OTHER centres and under-pay them. Re-add tenant + soft-delete.
        var rows = await _db.CommissionEntries.AsNoTracking().IgnoreQueryFilters()
            .Where(c => agencyIds.Contains(c.AgencyId) && !c.IsDeleted
                        && userIds.Contains(c.AgentUserId)
                        && c.EarnedAt >= start && c.EarnedAt < end)
            .Select(c => new { c.AgentUserId, c.Amount }).ToListAsync(ct);
        return rows.GroupBy(r => r.AgentUserId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
    }
}
