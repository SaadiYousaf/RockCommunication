using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
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
        Apply(p, request.Input);
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var (configs, names) = await LoadCallCenterInfoAsync(new[] { e.CallCenterId }, ct);
        return Build(e, request.Year, request.Month, p, null, null, 0m,
            CfgFor(e.CallCenterId, configs), NameFor(e.CallCenterId, names));
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

        // Per-line deduction amounts. A saved payroll keeps whatever HR chose (incl. an override);
        // an unsaved draft AUTO-CALCULATES them from the call centre's configured deduction rules.
        var auto = PayrollDeductions.Auto(basic, working, late, half, absent, ncns, cfg);
        var lateAmt = saved?.LateComingAmount ?? auto.Late;
        var halfAmt = saved?.HalfDaysAmount ?? auto.Half;
        var absentAmt = saved?.AbsentDaysAmount ?? auto.Absent;
        var ncnsAmt = saved?.NcnsAmount ?? auto.Ncns;

        var gross = basic + punctuality + dailyBonus + commission + transport + special;
        var deductions = advance + docks + lateAmt + halfAmt + absentAmt + ncnsAmt;
        var net = gross - deductions;

        return new PayrollRowDto(e.Id, e.FullName, e.AgentCode, e.CallCenterId, callCenterName, year, month,
            basic, punctuality, dailyBonus, commission, transport, special, advance, docks,
            working, present, leave, late, half, absent, ncns,
            lateAmt, halfAmt, absentAmt, ncnsAmt,
            gross, deductions, net, saved?.Notes, saved?.Finalized ?? false, saved is not null);
    }

    private static void Apply(EmployeePayroll p, SavePayrollInput i)
    {
        p.BasicSalary = i.BasicSalary; p.Punctuality = i.Punctuality; p.DailyBonus = i.DailyBonus;
        p.MonthlyCommissions = i.MonthlyCommissions; p.TransportAllowance = i.TransportAllowance;
        p.SpecialAllowance = i.SpecialAllowance; p.AdvanceSalary = i.AdvanceSalary; p.Docks = i.Docks;
        p.WorkingDays = i.WorkingDays; p.PresentDays = i.PresentDays; p.LeavesApproved = i.LeavesApproved;
        p.LateComing = i.LateComing; p.HalfDays = i.HalfDays; p.AbsentDays = i.AbsentDays; p.Ncns = i.Ncns;
        p.LateComingAmount = i.LateComingAmount; p.HalfDaysAmount = i.HalfDaysAmount;
        p.AbsentDaysAmount = i.AbsentDaysAmount; p.NcnsAmount = i.NcnsAmount;
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
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var rows = await _db.CommissionEntries.AsNoTracking()
            .Where(c => userIds.Contains(c.AgentUserId)
                        && c.EarnedAt >= start && c.EarnedAt < end && !c.IsDeleted)
            .Select(c => new { c.AgentUserId, c.Amount }).ToListAsync(ct);
        return rows.GroupBy(r => r.AgentUserId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
    }
}
